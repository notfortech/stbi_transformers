using DashboardAgents.Core.Models;

namespace DashboardAgents.BlueprintAgent;

/// <summary>
/// Assembles the full system prompt for the Blueprint Generation Agent by concatenating the
/// same design documents the original JS tool used (prompts/*.md), plus the run-time context
/// (fiscal year, currency, RLS requirement, refresh cadence) that buildSystemPrompt_updated.js
/// used to inject dynamically.
/// </summary>
public static class SystemPromptBuilder
{
    public static string Build(BlueprintGenerationOptions options)
    {
        var fyEnd = options.FiscalYearStart switch
        {
            "July" => "30/06",
            "January" => "31/12",
            "April" => "31/03",
            "October" => "30/09",
            _ => "30/06"
        };

        var runtimeContext = $"""
            RUN-TIME CONTEXT FOR THIS GENERATION
            =====================================
            Explicit industry override: {options.IndustryExplicit ?? "(none — detect from input)"}
            Primary audience: {options.Audience}
            Currency: {options.Currency}
            Fiscal year starts: {options.FiscalYearStart} (fiscal year end: {fyEnd})
            RLS required: {options.RlsRequired}
            Refresh cadence: {options.RefreshCadence}

            You must produce your ENTIRE response as a single valid JSON object conforming exactly
            to the schema in DashboardBlueprintSchema.md below. Do not include markdown code fences,
            preamble, or any text outside the JSON object.
            """;

        return string.Join("\n\n═══════════════════════════════════════════════════════════\n\n", new[]
        {
            PromptLoader.AgentInstructions,
            PromptLoader.IndustryDetectionRules,
            PromptLoader.BusinessCapabilityMappings,
            PromptLoader.DashboardDesignRules,
            PromptLoader.DashboardBlueprintSchema,
            runtimeContext
        });
    }
}
