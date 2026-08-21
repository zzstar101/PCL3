using PCL3.Minecraft.Accounts;
using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Runtime;

namespace PCL3.Minecraft.Launch;

public sealed record MinecraftLaunchContext(
    MinecraftVersionChain VersionChain,
    MinecraftRuleContext RuleContext,
    MinecraftRuntimePlan RuntimePlan,
    MinecraftSession Session,
    string GameDirectory,
    string LauncherName = "PCL3",
    string LauncherVersion = "dev",
    string? JavaExecutableOverride = null,
    int? ResolutionWidth = null,
    int? ResolutionHeight = null,
    IReadOnlyList<string>? ExtraJvmArguments = null,
    IReadOnlyList<string>? ExtraGameArguments = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    IReadOnlyDictionary<string, string>? AdditionalVariables = null)
{
    public override string ToString() =>
        $"MinecraftLaunchContext(Version={VersionChain.Selected.Id}, GameDirectory={GameDirectory}, Session={Session}, AdditionalVariables=<redacted>)";
}

public static class MinecraftLaunchVariableComposer
{
    public static IReadOnlyDictionary<string, string> Create(MinecraftLaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.VersionChain);
        ArgumentNullException.ThrowIfNull(context.RuleContext);
        ArgumentNullException.ThrowIfNull(context.RuntimePlan);
        ArgumentNullException.ThrowIfNull(context.Session);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.GameDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.LauncherName);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.LauncherVersion);

        ValidateResolution(context.ResolutionWidth, context.ResolutionHeight);

        var versionChain = context.VersionChain;
        var runtime = context.RuntimePlan;
        var session = context.Session;
        var gameDirectory = Path.GetFullPath(context.GameDirectory);
        var assetsRoot = Path.Combine(runtime.MinecraftDirectory, "assets");
        var assetsIndexName = versionChain.EffectiveAssetIndex?.Id ??
            versionChain.EffectiveAssetsId ??
            string.Empty;
        var gameAssets = string.IsNullOrWhiteSpace(versionChain.EffectiveAssetsId)
            ? assetsRoot
            : Path.Combine(assetsRoot, "virtual", versionChain.EffectiveAssetsId);
        var clientJarVersionId = versionChain.EffectiveClientJarVersionId;
        var primaryJar = Path.Combine(
            runtime.MinecraftDirectory,
            "versions",
            clientJarVersionId,
            $"{clientJarVersionId}.jar");
        var versionType = versionChain.Versions
            .Select(version => version.Type)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
            "release";

        var variables = new Dictionary<string, string>(
            runtime.CreateLaunchVariables(),
            StringComparer.Ordinal)
        {
            ["libraries_directory"] = runtime.LibrariesDirectory,
            ["launcher_name"] = context.LauncherName,
            ["launcher_version"] = context.LauncherVersion,
            ["version_name"] = versionChain.Selected.Id,
            ["version_type"] = versionType,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = assetsRoot,
            ["assets_index_name"] = assetsIndexName,
            ["game_assets"] = gameAssets,
            ["primary_jar"] = primaryJar,
            ["user_properties"] = session.UserPropertiesJson,
            ["auth_player_name"] = session.PlayerName,
            ["auth_uuid"] = session.PlayerUuid,
            ["auth_access_token"] = session.LaunchAccessToken,
            ["access_token"] = session.LaunchAccessToken,
            ["auth_session"] = session.LaunchAccessToken,
            ["user_type"] = session.UserType,
            ["auth_xuid"] = session.Xuid ?? string.Empty,
            ["clientid"] = session.ClientId ?? string.Empty
        };

        if (context.ResolutionWidth is { } width && context.ResolutionHeight is { } height)
        {
            variables["resolution_width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            variables["resolution_height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (context.AdditionalVariables is not null)
        {
            foreach (var pair in context.AdditionalVariables)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
                if (pair.Value is null)
                {
                    throw new ArgumentException($"Launch variable '{pair.Key}' has a null value.");
                }

                if (!variables.TryAdd(pair.Key, pair.Value))
                {
                    throw new InvalidOperationException(
                        $"Additional launch variable '{pair.Key}' conflicts with a canonical variable.");
                }
            }
        }

        return variables;
    }

    public static string ResolveJavaExecutable(MinecraftLaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.IsNullOrWhiteSpace(context.JavaExecutableOverride))
        {
            return context.JavaExecutableOverride;
        }

        var runtime = context.RuntimePlan.RequireJavaRuntime();
        if (!string.IsNullOrWhiteSpace(runtime.ExecutablePath))
        {
            return runtime.ExecutablePath;
        }

        return Path.Combine(
            runtime.HomePath,
            "bin",
            OperatingSystem.IsWindows() ? "java.exe" : "java");
    }

    private static void ValidateResolution(int? width, int? height)
    {
        if (width.HasValue != height.HasValue)
        {
            throw new ArgumentException("Custom resolution requires both width and height.");
        }

        if (width is <= 0 || height is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Custom resolution must be positive.");
        }
    }
}
