using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Libraries;
using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Natives;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MinecraftLibraryResolverTests
{
    [TestMethod]
    public void VersionJson_ParsesDownloadMetadataAndExtractRules()
    {
        const string json = """
        {
          "id": "test",
          "libraries": [
            {
              "name": "org.lwjgl:lwjgl:3.3.3",
              "natives": { "windows": "natives-windows-${arch}" },
              "downloads": {
                "artifact": {
                  "path": "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar",
                  "url": "https://example.invalid/lwjgl.jar",
                  "sha1": "abc",
                  "size": 123
                },
                "classifiers": {
                  "natives-windows-64": {
                    "path": "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows-64.jar",
                    "sha1": "def",
                    "size": 456
                  }
                }
              },
              "extract": { "exclude": ["META-INF/"] }
            }
          ]
        }
        """;

        var metadata = MinecraftVersionJson.Parse(json);
        var library = metadata.Libraries.Single();

        Assert.AreEqual("abc", library.Downloads?.Artifact?.Sha1);
        Assert.AreEqual(123L, library.Downloads?.Artifact?.Size);
        Assert.IsTrue(library.Downloads?.Classifiers.ContainsKey("natives-windows-64"));
        CollectionAssert.AreEqual(
            new[] { "META-INF/" },
            library.Extract?.Exclude.ToArray());
    }

    [TestMethod]
    public void Resolve_IncludesRegularArtifactAndPlatformNative()
    {
        var library = CreateLibrary(
            "org.lwjgl:lwjgl:3.3.3",
            natives: new Dictionary<string, string>
            {
                ["windows"] = "natives-windows-${arch}"
            },
            classifiers: new Dictionary<string, MinecraftDownloadArtifact>
            {
                ["natives-windows-64"] = new(
                    "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3-natives-windows-64.jar",
                    null,
                    "native-sha",
                    400)
            });
        var chain = CreateChain(library);
        var context = CreateContext(PlatformOperatingSystem.Windows, PlatformArchitecture.X64);
        var librariesDirectory = CreateTemporaryDirectory();

        try
        {
            var resolution = MinecraftLibraryResolver.Resolve(chain, context, librariesDirectory);

            Assert.AreEqual(1, resolution.ClasspathArtifacts.Count);
            Assert.AreEqual(1, resolution.NativeArtifacts.Count);
            Assert.AreEqual("natives-windows-64", resolution.NativeArtifacts[0].Coordinate.Classifier);
            Assert.AreEqual("native-sha", resolution.NativeArtifacts[0].Sha1);
        }
        finally
        {
            Directory.Delete(librariesDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Resolve_PrefersNewerVersionButKeepsChildOnTie()
    {
        var child = CreateMetadata(
            "child",
            CreateLibrary("com.example:demo:2.0.0", repositoryUrl: "https://child.invalid"));
        var parent = CreateMetadata(
            "parent",
            CreateLibrary("com.example:demo:1.9.9", repositoryUrl: "https://parent.invalid"));
        var chain = new MinecraftVersionChain(new[] { child, parent });
        var context = CreateContext(PlatformOperatingSystem.Linux, PlatformArchitecture.X64);
        var librariesDirectory = CreateTemporaryDirectory();

        try
        {
            var resolution = MinecraftLibraryResolver.Resolve(chain, context, librariesDirectory);

            Assert.AreEqual(1, resolution.ClasspathArtifacts.Count);
            Assert.AreEqual("2.0.0", resolution.ClasspathArtifacts[0].Coordinate.Version);
            StringAssert.StartsWith(resolution.ClasspathArtifacts[0].Url, "https://child.invalid/");
        }
        finally
        {
            Directory.Delete(librariesDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Resolve_AppliesLibraryRulesBeforeAddingArtifacts()
    {
        var library = CreateLibrary(
            "com.example:windows-only:1.0.0",
            rules: new[]
            {
                new MinecraftRule(
                    MinecraftRuleAction.Allow,
                    new MinecraftOsRule(Name: "windows"))
            });
        var chain = CreateChain(library);
        var context = CreateContext(PlatformOperatingSystem.Linux, PlatformArchitecture.X64);
        var librariesDirectory = CreateTemporaryDirectory();

        try
        {
            var resolution = MinecraftLibraryResolver.Resolve(chain, context, librariesDirectory);
            Assert.AreEqual(0, resolution.ClasspathArtifacts.Count);
            Assert.AreEqual(0, resolution.NativeArtifacts.Count);
        }
        finally
        {
            Directory.Delete(librariesDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Resolve_RejectsDownloadPathTraversal()
    {
        var library = CreateLibrary(
            "com.example:unsafe:1.0.0",
            artifact: new MinecraftDownloadArtifact("../escape.jar", null, null, null));
        var chain = CreateChain(library);
        var context = CreateContext(PlatformOperatingSystem.Linux, PlatformArchitecture.X64);
        var librariesDirectory = CreateTemporaryDirectory();

        try
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
                MinecraftLibraryResolver.Resolve(chain, context, librariesDirectory));
        }
        finally
        {
            Directory.Delete(librariesDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ClasspathAndNativePlan_ArePlatformNeutralData()
    {
        var librariesDirectory = CreateTemporaryDirectory();
        try
        {
            var artifact = new ResolvedMinecraftArtifact(
                MavenCoordinate.Parse("com.example:demo:1.0.0"),
                Path.Combine(librariesDirectory, "demo.jar"),
                null,
                null,
                null,
                "test");
            var native = new ResolvedMinecraftNativeArtifact(
                MavenCoordinate.Parse("com.example:native:1.0.0:natives-linux"),
                Path.Combine(librariesDirectory, "native.jar"),
                null,
                null,
                null,
                new[] { "META-INF/" },
                "test");

            var classpath = MinecraftClasspathBuilder.Build(new[] { artifact });
            var plan = MinecraftNativeExtractionPlanner.Create(
                new[] { native },
                Path.Combine(librariesDirectory, "natives"));

            Assert.AreEqual(artifact.LocalPath, classpath);
            Assert.AreEqual(1, plan.Archives.Count);
            CollectionAssert.AreEqual(
                new[] { "META-INF/" },
                plan.Archives[0].Excludes.ToArray());
        }
        finally
        {
            Directory.Delete(librariesDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("1.9.9", "1.10.0", -1)]
    [DataRow("3.3.3", "3.3.3", 0)]
    [DataRow("4.2", "4.2.1", -1)]
    [DataRow("31.1-jre", "30.1-jre", 1)]
    public void MavenVersionComparer_UsesNaturalNumericOrdering(
        string left,
        string right,
        int expectedSign)
    {
        var actual = Math.Sign(MavenVersionComparer.Instance.Compare(left, right));
        Assert.AreEqual(expectedSign, actual);
    }

    private static MinecraftLibrary CreateLibrary(
        string name,
        string? repositoryUrl = null,
        IReadOnlyList<MinecraftRule>? rules = null,
        IReadOnlyDictionary<string, string>? natives = null,
        MinecraftDownloadArtifact? artifact = null,
        IReadOnlyDictionary<string, MinecraftDownloadArtifact>? classifiers = null) =>
        new(
            name,
            repositoryUrl,
            rules ?? Array.Empty<MinecraftRule>(),
            natives ?? new Dictionary<string, string>(),
            new MinecraftLibraryDownloads(
                artifact,
                classifiers ?? new Dictionary<string, MinecraftDownloadArtifact>()),
            new MinecraftLibraryExtract(new[] { "META-INF/" }));

    private static MinecraftVersionChain CreateChain(MinecraftLibrary library) =>
        new(new[] { CreateMetadata("test", library) });

    private static MinecraftVersionMetadata CreateMetadata(
        string id,
        params MinecraftLibrary[] libraries) =>
        new(
            id,
            "release",
            "example.Main",
            null,
            null,
            Array.Empty<MinecraftArgument>(),
            Array.Empty<MinecraftArgument>(),
            null,
            libraries);

    private static MinecraftRuleContext CreateContext(
        PlatformOperatingSystem operatingSystem,
        PlatformArchitecture architecture) =>
        new(
            new PlatformTarget(operatingSystem, architecture),
            "1.0",
            new Dictionary<string, bool>());

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
