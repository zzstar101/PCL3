using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Java;
using PCL3.Minecraft.Libraries;
using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Runtime;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class LaunchRuntimeGoldenTests
{
    [TestMethod]
    public void FabricInheritance_PreservesChildLaunchSemanticsAndParentRuntime()
    {
        var child = MinecraftVersionJson.Parse("""
        {
          "id": "fabric-loader-0.16.10-1.21.4",
          "inheritsFrom": "1.21.4",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            { "name": "net.fabricmc:fabric-loader:0.16.10", "url": "https://maven.fabricmc.net/" },
            { "name": "net.fabricmc:intermediary:1.21.4", "url": "https://maven.fabricmc.net/" }
          ]
        }
        """);
        var parent = MinecraftVersionJson.Parse("""
        {
          "id": "1.21.4",
          "mainClass": "net.minecraft.client.main.Main",
          "javaVersion": { "component": "java-runtime-delta", "majorVersion": 21 },
          "libraries": [
            { "name": "com.google.guava:guava:32.1.2-jre" },
            {
              "name": "org.lwjgl:lwjgl:3.3.3",
              "natives": { "linux": "natives-linux" },
              "downloads": {
                "classifiers": {
                  "natives-linux": {
                    "path": "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-linux.jar"
                  }
                }
              },
              "extract": { "exclude": ["META-INF/"] }
            }
          ]
        }
        """);
        var chain = new MinecraftVersionChain(new[] { child, parent });
        var context = CreateContext(PlatformOperatingSystem.Linux, PlatformArchitecture.X64);
        var root = CreateTemporaryDirectory();

        try
        {
            var plan = MinecraftRuntimePlanner.Build(
                chain,
                context,
                root,
                Path.Combine(root, "natives"),
                new[] { new JavaRuntimeDescriptor("/java-21", 21, PlatformArchitecture.X64) });

            Assert.AreEqual("net.fabricmc.loader.impl.launch.knot.KnotClient", chain.EffectiveMainClass);
            Assert.AreEqual(21, plan.JavaRequirement.PreferredMajorVersion);
            Assert.AreEqual(4, plan.Libraries.ClasspathArtifacts.Count);
            Assert.AreEqual(1, plan.Libraries.NativeArtifacts.Count);
            Assert.AreEqual("natives-linux", plan.Libraries.NativeArtifacts[0].Coordinate.Classifier);
            Assert.IsTrue(plan.Libraries.ClasspathArtifacts.Any(artifact =>
                artifact.Coordinate.Group == "net.fabricmc" &&
                artifact.Coordinate.Artifact == "fabric-loader"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ForgeInheritance_PrefersNewerChildLibraryConflict()
    {
        var child = MinecraftVersionJson.Parse("""
        {
          "id": "1.20.1-forge-47.3.10",
          "inheritsFrom": "1.20.1",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.minecraftforge:forge:1.20.1-47.3.10", "url": "https://maven.minecraftforge.net/" },
            { "name": "org.ow2.asm:asm:9.5" }
          ]
        }
        """);
        var parent = MinecraftVersionJson.Parse("""
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "javaVersion": { "component": "java-runtime-gamma", "majorVersion": 17 },
          "libraries": [
            { "name": "org.ow2.asm:asm:9.4" },
            { "name": "com.mojang:authlib:4.0.43" }
          ]
        }
        """);
        var chain = new MinecraftVersionChain(new[] { child, parent });
        var context = CreateContext(PlatformOperatingSystem.Windows, PlatformArchitecture.X64);
        var root = CreateTemporaryDirectory();

        try
        {
            var resolution = MinecraftLibraryResolver.Resolve(
                chain,
                context,
                Path.Combine(root, "libraries"));
            var asm = resolution.ClasspathArtifacts.Single(artifact =>
                artifact.Coordinate.Group == "org.ow2.asm" &&
                artifact.Coordinate.Artifact == "asm");

            Assert.AreEqual("9.5", asm.Coordinate.Version);
            Assert.AreEqual("1.20.1-forge-47.3.10", asm.SourceVersionId);
            Assert.AreEqual("cpw.mods.bootstraplauncher.BootstrapLauncher", chain.EffectiveMainClass);
            Assert.AreEqual(17, MinecraftJavaRequirementResolver.Resolve(chain).PreferredMajorVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void NeoForgeInheritance_ResolvesArm64MacNativeWithoutArchTemplate()
    {
        var child = MinecraftVersionJson.Parse("""
        {
          "id": "neoforge-21.4.147",
          "inheritsFrom": "1.21.4",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "libraries": [
            { "name": "net.neoforged:neoforge:21.4.147", "url": "https://maven.neoforged.net/releases/" },
            { "name": "org.ow2.asm:asm:9.7.1" }
          ]
        }
        """);
        var parent = MinecraftVersionJson.Parse("""
        {
          "id": "1.21.4",
          "mainClass": "net.minecraft.client.main.Main",
          "javaVersion": { "component": "java-runtime-delta", "majorVersion": 21 },
          "libraries": [
            { "name": "org.ow2.asm:asm:9.7" },
            {
              "name": "org.lwjgl:lwjgl:3.3.3",
              "natives": { "osx": "natives-macos-arm64" },
              "extract": { "exclude": ["META-INF/"] }
            }
          ]
        }
        """);
        var chain = new MinecraftVersionChain(new[] { child, parent });
        var context = CreateContext(PlatformOperatingSystem.MacOS, PlatformArchitecture.Arm64);
        var root = CreateTemporaryDirectory();

        try
        {
            var resolution = MinecraftLibraryResolver.Resolve(
                chain,
                context,
                Path.Combine(root, "libraries"));
            var asm = resolution.ClasspathArtifacts.Single(artifact =>
                artifact.Coordinate.Group == "org.ow2.asm" &&
                artifact.Coordinate.Artifact == "asm");

            Assert.AreEqual("9.7.1", asm.Coordinate.Version);
            Assert.AreEqual(1, resolution.NativeArtifacts.Count);
            Assert.AreEqual("natives-macos-arm64", resolution.NativeArtifacts[0].Coordinate.Classifier);
            Assert.IsTrue(resolution.ClasspathArtifacts.Any(artifact =>
                artifact.Coordinate.Group == "net.neoforged" &&
                artifact.Coordinate.Artifact == "neoforge"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MinecraftRuleContext CreateContext(
        PlatformOperatingSystem operatingSystem,
        PlatformArchitecture architecture) =>
        new(
            new PlatformTarget(operatingSystem, architecture),
            "test",
            new Dictionary<string, bool>());

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-golden-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
