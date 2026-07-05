using DashboardAgents.Core.Models;

namespace DashboardAgents.BlueprintAgent;

/// <summary>Mirrors buildUserPrompt(mode, requirements, schema, ...) from the original app.js.</summary>
public static class UserPromptBuilder
{
    public static string Build(BlueprintGenerationOptions options)
    {
        var goalBlock = !string.IsNullOrWhiteSpace(options.Requirements) && options.Mode == "schema"
            ? $"BUSINESS GOAL:\n{options.Requirements}\n\n"
            : "";

        var inputBlock = options.Mode == "requirements"
            ? $"BUSINESS REQUIREMENTS:\n{options.Requirements ?? "(none provided)"}"
            : $"{goalBlock}DATASET SCHEMA / HEADERS:\n{options.SchemaText ?? "(none provided)"}";

        return $"""
            {inputBlock}

            Generate the complete Analytics Blueprint now, following all 8 mandatory workflow steps
            and the exact JSON schema. Populate every mandatory field — missing any field is a
            schema violation. Run the full 9-gate self-review before returning your response.
            """;
    }
}
