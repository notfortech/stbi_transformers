using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using ReportAgent.Api.Models;

namespace ReportAgent.Api.Services;

/// <summary>
/// Runs generate_generic_report.py as a subprocess against an untrusted
/// user-uploaded file. This class is the trust boundary between the HTTP API
/// and the Python engine — every design choice here assumes the input is
/// hostile until proven otherwise.
///
/// Defense-in-depth layers:
///   1. Per-request random scratch directory, deleted in a finally block.
///   2. No shell is ever invoked — ProcessStartInfo.ArgumentList is used
///      exclusively, so there is no argument/command injection surface.
///   3. A minimal, explicit environment is passed to the child process (no
///      inherited secrets, no proxy variables, fixed PATH).
///   4. A hard wall-clock timeout kills the entire process tree.
///   5. stderr and the output file are read under byte caps.
///   6. This process runs as a non-root user inside a container with no
///      outbound network access (see Dockerfile) — the .NET code below does
///      not and cannot enforce that on its own.
/// </summary>
public sealed class PythonAgentRunner
{
    private readonly PythonAgentOptions _options;
    private readonly ILogger<PythonAgentRunner> _logger;

    public PythonAgentRunner(IOptions<PythonAgentOptions> options, ILogger<PythonAgentRunner> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReportGenerationResult> GenerateAsync(
        byte[] fileBytes,
        string fileName,
        string? templateId,
        string? filtersJson,
        CancellationToken requestAborted)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var scratchDir = Path.Combine(_options.ScratchRoot, requestId);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            Directory.CreateDirectory(scratchDir);
            try { File.SetUnixFileMode(scratchDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            catch (PlatformNotSupportedException) { /* non-Unix dev host */ }

            var inputPath = Path.Combine(scratchDir, ext == ".csv" ? "data.csv" : "data.xlsx");
            var outputPath = Path.Combine(scratchDir, "result.json");
            await File.WriteAllBytesAsync(inputPath, fileBytes, requestAborted);

            var psi = new ProcessStartInfo
            {
                FileName = _options.PythonExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                WorkingDirectory = scratchDir,
            };
            psi.ArgumentList.Add(_options.ScriptPath);
            if (ext == ".csv")
            {
                // A single CSV upload is one table — --csv-dir globs *.csv in
                // the given directory, so pointing it at the scratch dir works.
                psi.ArgumentList.Add("--csv-dir"); psi.ArgumentList.Add(scratchDir);
            }
            else
            {
                psi.ArgumentList.Add("--workbook"); psi.ArgumentList.Add(inputPath);
            }
            psi.ArgumentList.Add("--templates-dir"); psi.ArgumentList.Add(_options.TemplatesDir);
            if (!string.IsNullOrWhiteSpace(templateId))
            {
                psi.ArgumentList.Add("--template-id"); psi.ArgumentList.Add(templateId);
            }
            if (!string.IsNullOrWhiteSpace(filtersJson))
            {
                psi.ArgumentList.Add("--filters"); psi.ArgumentList.Add(filtersJson);
            }
            psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputPath);
            psi.ArgumentList.Add("--timeout"); psi.ArgumentList.Add(_options.TimeoutSeconds.ToString());
            psi.ArgumentList.Add("--max-memory-mb"); psi.ArgumentList.Add(_options.MaxMemoryMb.ToString());

            psi.Environment.Clear();
            psi.Environment["PATH"] = "/usr/bin:/bin";
            psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
            psi.Environment["HOME"] = scratchDir;

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds + 5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, timeoutCts.Token);

            var stderr = new BoundedCollector(_options.MaxCapturedOutputBytes);
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.Add(e.Data); };

            _logger.LogInformation("Starting report agent {RequestId}", requestId);
            process.Start();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process, requestId);
                var reason = requestAborted.IsCancellationRequested ? "client disconnected" : "timeout exceeded";
                return Fail("TIMEOUT_OR_CANCELLED", $"Report generation was aborted ({reason}).");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Report agent {RequestId} exited {Code}: {Stderr}", requestId, process.ExitCode, stderr.ToString());
                // Deliberately do not forward raw stderr to the caller — it may
                // echo fragments of the (untrusted) input back.
                return Fail("AGENT_ERROR", "The report agent could not generate a report from the supplied file.");
            }

            var fileInfo = new FileInfo(outputPath);
            if (!fileInfo.Exists)
                return Fail("NO_OUTPUT", "The report agent completed but produced no output file.");
            if (fileInfo.Length > _options.MaxResultOutputBytes)
                return Fail("OUTPUT_TOO_LARGE", "The generated report exceeded the maximum allowed size.");

            var json = await File.ReadAllTextAsync(outputPath, Encoding.UTF8, requestAborted);
            return new ReportGenerationResult(true, json, null, "OK");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled error generating report {RequestId}", requestId);
            return Fail("INTERNAL_ERROR", "An internal error occurred while generating the report.");
        }
        finally
        {
            TryDeleteDirectory(scratchDir, requestId);
        }
    }

    private static ReportGenerationResult Fail(string code, string message) => new(false, null, message, code);

    private void KillProcessTree(Process process, string requestId)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to kill process tree for {RequestId}", requestId);
        }
    }

    private void TryDeleteDirectory(string path, string requestId)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up scratch dir for {RequestId}", requestId);
        }
    }

    /// <summary>Caps how much stderr we ever hold in memory from the child
    /// process, so a runaway/compromised process can't exhaust the API's own
    /// memory via output flooding.</summary>
    private sealed class BoundedCollector
    {
        private readonly StringBuilder _sb = new();
        private readonly int _maxBytes;
        private int _bytes;

        public BoundedCollector(int maxBytes) => _maxBytes = maxBytes;

        public void Add(string line)
        {
            if (_bytes >= _maxBytes) return;
            _bytes += Encoding.UTF8.GetByteCount(line) + 1;
            if (_bytes <= _maxBytes) _sb.AppendLine(line);
        }

        public override string ToString() => _sb.ToString();
    }
}
