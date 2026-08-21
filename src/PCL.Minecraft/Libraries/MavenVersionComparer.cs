using System.Text.RegularExpressions;

namespace PCL3.Minecraft.Libraries;

/// <summary>
/// Natural version ordering for launcher library conflict resolution.
/// Numeric runs are compared numerically and numeric tokens sort after text tokens,
/// matching the important behavior of PCL2's library de-duplication without coupling
/// the launcher to a package-manager implementation.
/// </summary>
public sealed partial class MavenVersionComparer : IComparer<string>
{
    public static MavenVersionComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var left = Tokenize(x);
        var right = Tokenize(y);
        var count = Math.Max(left.Count, right.Count);

        for (var index = 0; index < count; index++)
        {
            if (index >= left.Count)
            {
                return RemainingIsZero(right, index) ? 0 : -1;
            }

            if (index >= right.Count)
            {
                return RemainingIsZero(left, index) ? 0 : 1;
            }

            var comparison = CompareToken(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static IReadOnlyList<string> Tokenize(string value) =>
        VersionTokenRegex()
            .Matches(value)
            .Select(match => match.Value)
            .ToArray();

    private static int CompareToken(string left, string right)
    {
        var leftNumeric = left.All(char.IsDigit);
        var rightNumeric = right.All(char.IsDigit);

        if (leftNumeric && rightNumeric)
        {
            var normalizedLeft = left.TrimStart('0');
            var normalizedRight = right.TrimStart('0');
            normalizedLeft = normalizedLeft.Length == 0 ? "0" : normalizedLeft;
            normalizedRight = normalizedRight.Length == 0 ? "0" : normalizedRight;

            var lengthComparison = normalizedLeft.Length.CompareTo(normalizedRight.Length);
            return lengthComparison != 0
                ? lengthComparison
                : string.CompareOrdinal(normalizedLeft, normalizedRight);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? 1 : -1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static bool RemainingIsZero(IReadOnlyList<string> tokens, int index) =>
        tokens.Skip(index).All(token => token.All(char.IsDigit) && token.All(character => character == '0'));

    [GeneratedRegex("[0-9]+|[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionTokenRegex();
}
