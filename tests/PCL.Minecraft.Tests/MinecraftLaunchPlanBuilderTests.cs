using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Launch;
using PCL3.Minecraft.Metadata;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MinecraftLaunchPlanBuilderTests
{
    [TestMethod]
    public void Build_UsesChildThenParentArgumentsAndExpandsVariables()
    {
        var child = Version(
            "loader",
            "vanilla",
            "loader.Main",
            jvmArguments:
            [
                Argument("-Dloader=true"),
                Argument(
                    ["-XstartOnFirstThread", "-Dnative=${natives_directory}"],
                    new MinecraftRule(
                        MinecraftRuleAction.Allow,
                        new MinecraftOsRule(Name: "osx")))
            ],
            gameArguments:
            [
                Argument("--loader-option"),
                Argument("${loader_value}")
            ]);

        var parent = Version(
            "vanilla",
            mainClass: "vanilla.Main",
            jvmArguments:
            [
                Argument("-Dparent=true")
            ],
            gameArguments:
            [
                Argument("--username"),
                Argument("${auth_player_name}")
            ]);

        var chain = new MinecraftVersionChain([child, parent]);
        var context = new MinecraftRuleContext(
            new PlatformTarget(
                PlatformOperatingSystem.MacOS,
                PlatformArchitecture.Arm64),
            "15.0",
            new Dictionary<string, bool>());

        var plan = MinecraftLaunchPlanBuilder.Build(new MinecraftLaunchRequest(
            chain,
            context,
            "/usr/bin/java",
            "/games/test",
            new Dictionary<string, string>
            {
                ["natives_directory"] = "/games/test/natives",
                ["loader_value"] = "enabled",
                ["auth_player_name"] = "Player"
            }));

        CollectionAssert.AreEqual(
            new[]
            {
                "-Dloader=true",
                "-XstartOnFirstThread",
                "-Dnative=/games/test/natives",
                "-Dparent=true",
                "loader.Main",
                "--loader-option",
                "enabled",
                "--username",
                "Player"
            },
            plan.Arguments.ToArray());
    }

    [TestMethod]
    public void Build_TokenizesLegacyArgumentsWithoutPlatformShellQuoting()
    {
        var version = new MinecraftVersionMetadata(
            "1.8.9",
            "release",
            "net.minecraft.client.main.Main",
            null,
            null,
            Array.Empty<MinecraftArgument>(),
            Array.Empty<MinecraftArgument>(),
            "--username ${auth_player_name} --gameDir \"${game_directory}\"",
            Array.Empty<MinecraftLibrary>());

        var plan = MinecraftLaunchPlanBuilder.Build(new MinecraftLaunchRequest(
            new MinecraftVersionChain([version]),
            new MinecraftRuleContext(
                new PlatformTarget(
                    PlatformOperatingSystem.Windows,
                    PlatformArchitecture.X64),
                "10.0",
                new Dictionary<string, bool>()),
            "java.exe",
            "C:/Games/Test",
            new Dictionary<string, string>
            {
                ["auth_player_name"] = "Player",
                ["game_directory"] = "C:/Games/Test Instance"
            }));

        CollectionAssert.AreEqual(
            new[]
            {
                "net.minecraft.client.main.Main",
                "--username",
                "Player",
                "--gameDir",
                "C:/Games/Test Instance"
            },
            plan.Arguments.ToArray());
    }

    [TestMethod]
    public void Build_ThrowsWhenRequiredVariableIsMissing()
    {
        var version = Version(
            "test",
            mainClass: "example.Main",
            gameArguments:
            [
                Argument("${missing}")
            ]);

        Assert.Throws<KeyNotFoundException>(() =>
            MinecraftLaunchPlanBuilder.Build(new MinecraftLaunchRequest(
                new MinecraftVersionChain([version]),
                new MinecraftRuleContext(
                    new PlatformTarget(
                        PlatformOperatingSystem.Linux,
                        PlatformArchitecture.X64),
                    "6.0",
                    new Dictionary<string, bool>()),
                "java",
                "/games/test",
                new Dictionary<string, string>())));
    }

    private static MinecraftArgument Argument(string value) =>
        new([value], Array.Empty<MinecraftRule>());

    private static MinecraftArgument Argument(
        IReadOnlyList<string> values,
        params MinecraftRule[] rules) =>
        new(values, rules);

    private static MinecraftVersionMetadata Version(
        string id,
        string? inheritsFrom = null,
        string? mainClass = null,
        IReadOnlyList<MinecraftArgument>? jvmArguments = null,
        IReadOnlyList<MinecraftArgument>? gameArguments = null) =>
        new(
            id,
            "release",
            mainClass,
            inheritsFrom,
            null,
            jvmArguments ?? Array.Empty<MinecraftArgument>(),
            gameArguments ?? Array.Empty<MinecraftArgument>(),
            null,
            Array.Empty<MinecraftLibrary>());
}
