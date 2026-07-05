using System.Text.Json;

namespace DashboardAgents.BlueprintAgent;

public sealed class BlueprintParseException : Exception
{
    public BlueprintParseException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class JsonExtraction
{
    /// <summary>
    /// Defensively extracts a JSON object from raw LLM output. The system prompt instructs the
    /// model to return only JSON, but this strips ```json fences and surrounding prose if the
    /// model doesn't comply exactly, then validates the result actually parses.
    /// </summary>
    public static JsonDocument ExtractJsonDocument(string rawText)
    {
        var candidate = rawText.Trim();

        if (candidate.StartsWith("```"))
        {
            var firstNewline = candidate.IndexOf('\n');
            var lastFence = candidate.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > -1 && lastFence > firstNewline)
            {
                candidate = candidate[(firstNewline + 1)..lastFence].Trim();
            }
        }

        var firstBrace = candidate.IndexOf('{');
        var lastBrace = candidate.LastIndexOf('}');
        if (firstBrace == -1 || lastBrace == -1 || lastBrace <= firstBrace)
        {
            throw new BlueprintParseException("LLM response did not contain a recognizable JSON object.");
        }
        candidate = candidate[firstBrace..(lastBrace + 1)];

        try
        {
            return JsonDocument.Parse(candidate);
        }
        catch (JsonException ex)
        {
            throw new BlueprintParseException("LLM response was not valid JSON after fence stripping.", ex);
        }
    }
}
