using System.Reflection;

namespace DashboardAgents.BlueprintAgent;

/// <summary>Reads the embedded prompts/*.md files (copied verbatim from the original tool) at runtime.</summary>
public static class PromptLoader
{
    private static readonly Assembly Assembly = typeof(PromptLoader).Assembly;
    private static readonly Dictionary<string, string> Cache = new();

    public static string Load(string fileName)
    {
        if (Cache.TryGetValue(fileName, out var cached)) return cached;

        var resourceName = Assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Embedded prompt resource not found: {fileName}. Available: {string.Join(", ", Assembly.GetManifestResourceNames())}");

        using var stream = Assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        Cache[fileName] = text;
        return text;
    }

    public static string AgentInstructions => Load("AgentInstructions.md");
    public static string BusinessCapabilityMappings => Load("BusinessCapabilityMappings.md");
    public static string DashboardBlueprintSchema => Load("DashboardBlueprintSchema.md");
    public static string DashboardDesignRules => Load("DashboardDesignRules.md");
    public static string IndustryDetectionRules => Load("IndustryDetectionRules.md");
}
