using System.Text.RegularExpressions;

namespace PCL3.Minecraft.Launch;

public static partial class MinecraftVariableExpander
{
    public static string Expand(
        string template,
        IReadOnlyDictionary<string, string> variables,
        bool throwOnMissing = true)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(variables);

        return PlaceholderRegex().Replace(template, match =>
        {
            var name = match.Groups[1].Value;

            if (variables.TryGetValue(name, out var value))
            {
                return value;
            }

            if (throwOnMissing)
            {
                throw new KeyNotFoundException(
                    $"Minecraft launch variable '{name}' was not provided for template '{template}'.");
            }

            return match.Value;
        });
    }

    [GeneratedRegex(@"\$\{([A-Za-z0-9_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
