using System.Text.Json;

namespace Gateway.Services;

public record PromptTemplate(string Name, string Version, string Template);

/// <summary>
/// The versioning story: prompts live as small JSON files on disk (one per
/// name+version), loaded once at startup. Requests pick a version explicitly
/// (safe for a prod client mid-rollout) or omit it to get the highest version
/// registered for that name (fine for a dev client). Nothing here talks to a
/// database — a prompt change is a file change and a redeploy, which is exactly
/// the trade-off called out in the README (auditable via git history, but not
/// hot-swappable without a restart).
/// </summary>
public class PromptRegistry
{
    private readonly Dictionary<string, List<PromptTemplate>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public PromptRegistry(string promptsDirectory)
    {
        if (!Directory.Exists(promptsDirectory))
            throw new DirectoryNotFoundException($"Prompts directory not found: {promptsDirectory}");

        foreach (var file in Directory.GetFiles(promptsDirectory, "*.json"))
        {
            var json = File.ReadAllText(file);
            var template = JsonSerializer.Deserialize<PromptTemplate>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (template is null) continue;

            if (!_byName.TryGetValue(template.Name, out var versions))
                _byName[template.Name] = versions = new List<PromptTemplate>();

            versions.Add(template);
        }

        if (_byName.Count == 0)
            throw new InvalidOperationException($"No prompt templates found in {promptsDirectory}.");
    }

    public PromptTemplate Resolve(string name, string? version)
    {
        if (!_byName.TryGetValue(name, out var versions) || versions.Count == 0)
            throw new KeyNotFoundException($"No prompt registered under name '{name}'.");

        if (version is null)
            return versions.OrderByDescending(v => v.Version, StringComparer.OrdinalIgnoreCase).First();

        return versions.FirstOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"Prompt '{name}' has no version '{version}'. Known versions: {string.Join(", ", versions.Select(v => v.Version))}");
    }

    public static string Render(string template, IDictionary<string, string>? variables)
    {
        if (variables is null || variables.Count == 0) return template;

        var rendered = template;
        foreach (var (key, value) in variables)
            rendered = rendered.Replace("{{" + key + "}}", value);

        return rendered;
    }
}
