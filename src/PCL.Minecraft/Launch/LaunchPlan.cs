namespace PCL3.Minecraft.Launch;

public sealed record LaunchPlan(
    string Executable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables);
