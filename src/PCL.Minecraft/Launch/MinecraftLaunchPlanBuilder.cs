using PCL3.Minecraft.Metadata;

namespace PCL3.Minecraft.Launch;

public sealed record MinecraftLaunchRequest(
    MinecraftVersionChain VersionChain,
    MinecraftRuleContext RuleContext,
    string JavaExecutable,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string>? ExtraJvmArguments = null,
    IReadOnlyList<string>? ExtraGameArguments = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    bool ThrowOnMissingVariables = true)
{
    public override string ToString() =>
        $"MinecraftLaunchRequest(Version={VersionChain.Selected.Id}, JavaExecutable={JavaExecutable}, WorkingDirectory={WorkingDirectory}, Variables=<redacted>, ExtraJvmArguments={ExtraJvmArguments?.Count ?? 0}, ExtraGameArguments={ExtraGameArguments?.Count ?? 0})";
}

public static class MinecraftLaunchPlanBuilder
{
    public static LaunchPlan Build(MinecraftLaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var variables = MinecraftLaunchVariableComposer.Create(context);
        return Build(new MinecraftLaunchRequest(
            context.VersionChain,
            context.RuleContext,
            MinecraftLaunchVariableComposer.ResolveJavaExecutable(context),
            Path.GetFullPath(context.GameDirectory),
            variables,
            context.ExtraJvmArguments,
            context.ExtraGameArguments,
            context.EnvironmentVariables));
    }

    public static LaunchPlan Build(MinecraftLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JavaExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        var mainClass = request.VersionChain.EffectiveMainClass;

        if (string.IsNullOrWhiteSpace(mainClass))
        {
            throw new InvalidDataException(
                $"Minecraft version '{request.VersionChain.Selected.Id}' does not define an effective main class.");
        }

        var arguments = new List<string>();

        AddExpanded(
            arguments,
            MinecraftArgumentResolver.Resolve(
                request.VersionChain.EnumerateJvmArgumentsChildFirst(),
                request.RuleContext),
            request);

        AddExpanded(arguments, request.ExtraJvmArguments ?? Array.Empty<string>(), request);

        arguments.Add(mainClass);

        var legacyArguments = request.VersionChain.EffectiveLegacyMinecraftArguments;
        if (!string.IsNullOrWhiteSpace(legacyArguments))
        {
            AddExpanded(
                arguments,
                LegacyMinecraftArgumentTokenizer.Tokenize(legacyArguments),
                request);
        }

        AddExpanded(
            arguments,
            MinecraftArgumentResolver.Resolve(
                request.VersionChain.EnumerateGameArgumentsChildFirst(),
                request.RuleContext),
            request);

        AddExpanded(arguments, request.ExtraGameArguments ?? Array.Empty<string>(), request);

        var environment = request.EnvironmentVariables is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(request.EnvironmentVariables, StringComparer.Ordinal);

        return new LaunchPlan(
            request.JavaExecutable,
            Path.GetFullPath(request.WorkingDirectory),
            arguments,
            environment);
    }

    private static void AddExpanded(
        ICollection<string> destination,
        IEnumerable<string> source,
        MinecraftLaunchRequest request)
    {
        foreach (var argument in source)
        {
            destination.Add(MinecraftVariableExpander.Expand(
                argument,
                request.Variables,
                request.ThrowOnMissingVariables));
        }
    }
}
