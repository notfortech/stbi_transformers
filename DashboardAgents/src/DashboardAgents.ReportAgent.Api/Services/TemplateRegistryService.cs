using System.Text.Json;
using Microsoft.Extensions.Options;
using ReportAgent.Api.Models;

namespace ReportAgent.Api.Services;

/// <summary>
/// Reads the same templates/index.json the Python engine reads — single
/// source of truth for the template registry. Pure metadata; no dataset or
/// Python invocation is needed to list what's available.
/// </summary>
public sealed class TemplateRegistryService
{
    private readonly string _templatesDir;

    public TemplateRegistryService(IOptions<PythonAgentOptions> options)
    {
        _templatesDir = options.Value.TemplatesDir;
    }

    public List<TemplateSummary> ListTemplates()
    {
        var indexPath = Path.Combine(_templatesDir, "index.json");
        if (!File.Exists(indexPath))
            return [];

        var json = File.ReadAllText(indexPath);
        var entries = JsonSerializer.Deserialize<List<TemplateIndexEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        return entries
            .Select(e => new TemplateSummary(e.Id, e.Name, e.Industry, e.Description, e.Requires))
            .ToList();
    }

    private sealed record TemplateIndexEntry(
        string Id,
        string File,
        string Name,
        string? Industry,
        string? Description,
        Dictionary<string, int>? Requires);
}
