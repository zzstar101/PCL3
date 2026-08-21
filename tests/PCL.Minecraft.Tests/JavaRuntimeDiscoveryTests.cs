using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Java;
using PCL3.Minecraft.Metadata;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class JavaRuntimeDiscoveryTests
{
    [TestMethod]
    public void Locator_FindsEnvironmentAndMinecraftRuntimeCandidates()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var environmentHome = Path.Combine(root, "env-jdk");
            var runtimeHome = Path.Combine(
                root,
                ".minecraft",
                "runtime",
                "java-runtime-gamma",
                "windows-x64",
                "java-runtime-gamma");
            CreateFakeJava(environmentHome, "java.exe");
            CreateFakeJava(runtimeHome, "java.exe");

            var options = new JavaDiscoveryOptions(
                new PlatformTarget(PlatformOperatingSystem.Windows, PlatformArchitecture.X64),
                root,
                Path.Combine(root, ".minecraft"),
                null,
                null,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["JAVA_HOME"] = environmentHome,
                    ["JDK_HOME"] = null,
                    ["PATH"] = null
                });

            var candidates = JavaInstallationLocator.Discover(options);

            Assert.AreEqual(2, candidates.Count);
            Assert.IsTrue(candidates.Any(candidate =>
                candidate.Source == JavaDiscoverySource.Environment));
            Assert.IsTrue(candidates.Any(candidate =>
                candidate.Source == JavaDiscoverySource.MinecraftRuntime));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(
        "java.version = 1.8.0_442\nos.arch = amd64\njava.vendor = Eclipse Adoptium",
        8,
        PlatformArchitecture.X64)]
    [DataRow(
        "java.version = 21.0.8\nos.arch = aarch64\njava.vendor = Azul Systems, Inc.",
        21,
        PlatformArchitecture.Arm64)]
    public void ProbeParser_ExtractsVersionAndArchitecture(
        string output,
        int expectedMajor,
        PlatformArchitecture expectedArchitecture)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var executable = Path.Combine(root, "bin", "java");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, string.Empty);

            var runtime = JavaRuntimeProbe.Parse(executable, output);

            Assert.AreEqual(expectedMajor, runtime.MajorVersion);
            Assert.AreEqual(expectedArchitecture, runtime.Architecture);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void RequirementResolver_UsesMetadataMajorAndLegacyJava8Fallback()
    {
        var modern = new MinecraftVersionChain(new[]
        {
            CreateMetadata("modern", new MinecraftJavaVersion("java-runtime-delta", 21))
        });
        var legacy = new MinecraftVersionChain(new[]
        {
            CreateMetadata("legacy", null)
        });

        Assert.AreEqual(21, MinecraftJavaRequirementResolver.Resolve(modern).PreferredMajorVersion);
        Assert.AreEqual(8, MinecraftJavaRequirementResolver.Resolve(legacy).PreferredMajorVersion);
    }

    [TestMethod]
    public void Selector_PrefersRequestedMajorAndNativeArchitecture()
    {
        var target = new PlatformTarget(PlatformOperatingSystem.MacOS, PlatformArchitecture.Arm64);
        var requirement = new JavaRequirement(17, PreferredMajorVersion: 21);
        JavaRuntimeDescriptor[] runtimes =
        [
            new("/java-21-x64", 21, PlatformArchitecture.X64),
            new("/java-17-arm64", 17, PlatformArchitecture.Arm64),
            new("/java-21-arm64", 21, PlatformArchitecture.Arm64)
        ];

        var selection = JavaRuntimeSelector.SelectBest(runtimes, requirement, target);

        Assert.IsNotNull(selection);
        Assert.AreEqual("/java-21-arm64", selection.Runtime.HomePath);
    }

    private static MinecraftVersionMetadata CreateMetadata(
        string id,
        MinecraftJavaVersion? javaVersion) =>
        new(
            id,
            "release",
            "example.Main",
            null,
            javaVersion,
            Array.Empty<MinecraftArgument>(),
            Array.Empty<MinecraftArgument>(),
            null,
            Array.Empty<MinecraftLibrary>());

    private static void CreateFakeJava(string home, string executableName)
    {
        var bin = Path.Combine(home, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, executableName), string.Empty);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-java-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
