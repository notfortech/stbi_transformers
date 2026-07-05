using System.Reflection;

namespace DashboardAgents.TweakAgent;

public static class TweakPromptLoader
{
    private static readonly Assembly Assembly = typeof(TweakPromptLoader).Assembly;
    private static string? _cached;

    public static string Instructions
    {
        get
        {
            if (_cached is not null) return _cached;

            var resourceName = Assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("UseCaseTweakAgentInstructions.md", StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException("Embedded resource UseCaseTweakAgentInstructions.md not found.");

            using var stream = Assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            _cached = reader.ReadToEnd();
            return _cached;
        }
    }
}
