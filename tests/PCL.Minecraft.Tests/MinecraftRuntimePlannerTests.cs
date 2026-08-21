using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Java;
using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Runtime;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MinecraftRuntimePlannerTests
{
    [TestMethod]
    public void Build_ComposesInheritanceLibrariesNativesAndJavaSelection()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var child = CreateMetadata(
                "fabric-loader-0.16.10-1.21.4",
                new MinecraftJavaVersion("java-runtime-delta", 21),
                new MinecraftLibrary(
                    "net.fabricmc:fabric-loader:0.16.10",
                    "https://maven.fabricmc.net/",
                    Array.Empty<MinecraftRule>(),
                    new Dictionary<string, string>()));
            var parent = CreateMetadata(
                "1.21.4",
                null,
                new MinecraftLibrary(
                    "org.lwjgl:lwjgl:3.3.3",
                    null,
                    Array.Empty<MinecraftRule>(),
                    new Dictionary<string, string>
                    {
                        ["linux"] = "natives-linux"
                    },
                    new MinecraftLibraryDownloads(
                        null,
                        new Dictionary<string, MinecraftDownloadArtifact>
                        {
                            ["natives-linux"] = new(
                                "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-linux.jar",
                                null,
                                null,
                                null)
                        }),
                    new MinecraftLibraryExtract(new[] { "META-INF/" })));
            var chain = new MinecraftVersionChain(new[] { child, parent });
            var context = new MinecraftRuleContext(
                new PlatformTarget(PlatformOperatingSystem.Linux, PlatformArchitecture.X64),
                "6.12",
                new Dictionary<string, bool>());
            JavaRuntimeDescriptor[] runtimes =
            [
                new("/java-17", 17, PlatformArchitecture.X64),
                new("/java-21", 21, PlatformArchitecture.X64, ExecutablePath: "/java-21/bin/java")
            ];

            var plan = MinecraftRuntimePlanner.Build(
                chain,
                context,
                root,
                Path.Combine(root, "natives"),
                runtimes,
                Path.Combine(root, "versions", "1.21.4", "1.21.4.jar"));

            Assert.AreEqual(2, plan.Libraries.ClasspathArtifacts.Count);
            Assert.AreEqual(1, plan.Libraries.NativeArtifacts.Count);
            Assert.AreEqual(21, plan.RequireJavaRuntime().MajorVersion);
            Assert.IsTrue(plan.Classpath.Contains("fabric-loader-0.16.10.jar", StringComparison.Ordinal));
            Assert.IsTrue(plan.Classpath.Contains("lwjgl-3.3.3.jar", StringComparison.Ordinal));

            var variables = plan.CreateLaunchVariables();
            Assert.AreEqual(plan.Classpath, variables["classpath"]);
            Assert.AreEqual(plan.NativeExtraction.DestinationDirectory, variables["natives_directory"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Build_LeavesJavaSelectionEmptyWhenNoCompatibleRuntimeExists()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var chain = new MinecraftVersionChain(new[]
            {
                CreateMetadata(
                    "modern",
                    new MinecraftJavaVersion("java-runtime-delta", 21))
            });
            var context = new MinecraftRuleContext(
                new PlatformTarget(PlatformOperatingSystem.Windows, PlatformArchitecture.X64),
                "10.0",
                new Dictionary<string, bool>());

            var plan = MinecraftRuntimePlanner.Build(
                chain,
                context,
                root,
                Path.Combine(root, "natives"),
                new[] { new JavaRuntimeDescriptor("/java-17", 17, PlatformArchitecture.X64) });

            Assert.IsNull(plan.JavaSelection);
            Assert.ThrowsExactly<InvalidOperationException>(() => plan.RequireJavaRuntime());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MinecraftVersionMetadata CreateMetadata(
        string id,
        MinecraftJavaVersion? javaVersion,
        params MinecraftLibrary[] libraries) =>
        new(
            id,
            "release",
            "example.Main",
            null,
            javaVersion,
            Array.Empty<MinecraftArgument>(),
            Array.Empty<MinecraftArgument>(),
            null,
            libraries);

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
