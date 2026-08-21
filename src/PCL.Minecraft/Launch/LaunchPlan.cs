namespace PCL3.Minecraft.Launch;

public sealed record LaunchPlan(
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    /// <summary>
    /// Launch arguments may contain access tokens. Keep diagnostic formatting metadata-only.
    /// </summary>
    public override string ToString() =>
        $"LaunchPlan(Executable={Executable}, WorkingDirectory={WorkingDirectory}, Arguments=<redacted:{Arguments.Count}>, EnvironmentVariables={EnvironmentVariables.Count})";
}
