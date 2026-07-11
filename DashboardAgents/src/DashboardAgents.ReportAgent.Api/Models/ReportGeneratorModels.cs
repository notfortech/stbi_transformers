namespace ReportAgent.Api.Models;

public sealed record ReportGenerationResult(
    bool Success,
    string? ResultJson,
    string? ErrorMessage,
    string ErrorCode
);

public sealed record TemplateSummary(
    string Id,
    string Name,
    string? Industry,
    string? Description,
    Dictionary<string, int>? Requires
);

public sealed class PythonAgentOptions
{
    /// Path to the python3 interpreter inside the container's venv.
    public string PythonExecutable { get; set; } = "python3";

    /// Path to generate_generic_report.py inside the container.
    public string ScriptPath { get; set; } = "/app/python_agent/generate_generic_report.py";

    /// Path to the template registry directory inside the container.
    public string TemplatesDir { get; set; } = "/app/python_agent/templates";

    /// Base directory for ephemeral per-request scratch folders. Should be a
    /// dedicated, size-capped tmpfs mount — not the container's general /tmp.
    public string ScratchRoot { get; set; } = "/scratch";

    public int TimeoutSeconds { get; set; } = 90;
    public int MaxMemoryMb { get; set; } = 1536;

    /// Hard cap on how much of the child process's stderr we will ever
    /// buffer, to bound memory use if the process misbehaves.
    public int MaxCapturedOutputBytes { get; set; } = 10 * 1024 * 1024;

    /// Hard cap on the resulting JSON result size read back from disk.
    public long MaxResultOutputBytes { get; set; } = 10 * 1024 * 1024;
}
