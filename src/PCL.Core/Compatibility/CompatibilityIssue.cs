namespace PCL3.Core.Compatibility;

public enum CompatibilitySeverity
{
    Information,
    Warning,
    Error
}

public sealed record CompatibilityIssue(
    string Code,
    CompatibilitySeverity Severity,
    string Message);

public sealed class CompatibilityReport
{
    public CompatibilityReport(IEnumerable<CompatibilityIssue> issues)
    {
        Issues = issues.ToArray();
    }

    public IReadOnlyList<CompatibilityIssue> Issues { get; }

    public bool IsCompatible =>
        Issues.All(issue => issue.Severity is not CompatibilitySeverity.Error);
}
