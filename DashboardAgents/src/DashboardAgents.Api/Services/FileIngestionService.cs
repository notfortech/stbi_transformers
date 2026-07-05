using System.Text;
using DashboardAgents.Core.Models;

namespace DashboardAgents.Api.Services;

public interface IFileIngestionService
{
    Task<SchemaSnapshot> IngestAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Parses CSV and TSV files into a SchemaSnapshot, inferring column types and
/// computing cardinality stats from the first 1000 rows without loading the full file.
/// </summary>
public sealed class FileIngestionService : IFileIngestionService
{
    private const int MaxSampleRows = 1000;
    private const int MaxDistinctSampleValues = 20;
    private const int DistinctSampleThreshold = 50;

    public Task<SchemaSnapshot> IngestAsync(byte[] fileBytes, string fileName, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".csv" or ".tsv" or ".txt"))
            throw new NotSupportedException($"File type '{ext}' is not supported. Upload a CSV or TSV file.");

        var separator = ext == ".tsv" ? '\t' : DetectSeparator(fileBytes);
        var snapshot = ParseCsv(fileBytes, fileName, separator);
        return Task.FromResult(snapshot);
    }

    private static char DetectSeparator(byte[] bytes)
    {
        var sample = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
        var commas = sample.Count(c => c == ',');
        var tabs = sample.Count(c => c == '\t');
        var semis = sample.Count(c => c == ';');
        if (tabs > commas && tabs > semis) return '\t';
        if (semis > commas) return ';';
        return ',';
    }

    private static SchemaSnapshot ParseCsv(byte[] bytes, string fileName, char separator)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException("The file appears to be empty or has no header row.");

        var headers = SplitLine(headerLine, separator);
        if (headers.Length == 0)
            throw new InvalidOperationException("Could not parse any column headers from the file.");

        // Collect samples per column
        var samples = headers.Select(_ => new List<string>()).ToArray();
        var rowCount = 0;

        string? line;
        while ((line = reader.ReadLine()) != null && rowCount < MaxSampleRows)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = SplitLine(line, separator);
            for (var i = 0; i < Math.Min(fields.Length, headers.Length); i++)
                samples[i].Add(fields[i]);
            rowCount++;
        }

        var columns = new List<ColumnMetadata>();
        for (var i = 0; i < headers.Length; i++)
        {
            var col = new ColumnMetadata
            {
                ColumnName = headers[i].Trim(),
                DataType = InferType(samples[i]),
                IsNullable = samples[i].Any(v => string.IsNullOrEmpty(v))
            };

            var distinctValues = samples[i].Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            col.DistinctValueCount = distinctValues.Count;

            if (distinctValues.Count <= DistinctSampleThreshold)
                col.DistinctSampleValues = distinctValues.Take(MaxDistinctSampleValues).ToList();

            columns.Add(col);
        }

        return new SchemaSnapshot
        {
            DatabaseName = Path.GetFileNameWithoutExtension(fileName),
            Provider = DbProvider.SqlServer, // treated as flat file
            Tables = new List<TableMetadata>
            {
                new()
                {
                    TableName = Path.GetFileNameWithoutExtension(fileName),
                    SchemaName = "file",
                    ApproximateRowCount = rowCount,
                    Columns = columns
                }
            }
        };
    }

    private static string[] SplitLine(string line, char sep)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == sep && !inQuotes)
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    private static string InferType(List<string> values)
    {
        var nonEmpty = values.Where(v => !string.IsNullOrEmpty(v)).Take(200).ToList();
        if (nonEmpty.Count == 0) return "text";

        if (nonEmpty.All(v => long.TryParse(v, out _))) return "integer";
        if (nonEmpty.All(v => decimal.TryParse(v, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _))) return "decimal";
        if (nonEmpty.All(v => bool.TryParse(v, out _) || v is "0" or "1" or "yes" or "no" or "y" or "n")) return "boolean";
        if (nonEmpty.All(v => DateTimeOffset.TryParse(v, out _))) return "datetime";
        return "text";
    }
}
