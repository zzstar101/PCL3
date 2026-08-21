using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Artifacts;
using PCL3.Minecraft.Java;
using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Natives;
using PCL3.Minecraft.Runtime;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class ArtifactAndAssetTests
{
    [TestMethod]
    public void VersionJson_ParsesClientAndAssetIndexMetadata()
    {
        var metadata = MinecraftVersionJson.Parse("""
        {
          "id": "1.21.4",
          "assets": "19",
          "downloads": {
            "client": {
              "sha1": "0123456789abcdef0123456789abcdef01234567",
              "size": 123,
              "url": "https://example.invalid/client.jar"
            }
          },
          "assetIndex": {
            "id": "19",
            "sha1": "89abcdef0123456789abcdef0123456789abcdef",
            "size": 456,
            "totalSize": 789,
            "url": "https://example.invalid/19.json"
          }
        }
        """);

        Assert.AreEqual("19", metadata.Assets);
        Assert.AreEqual(123L, metadata.Downloads?.Client?.Size);
        Assert.AreEqual("19", metadata.AssetIndex?.Id);
        Assert.AreEqual(789L, metadata.AssetIndex?.TotalSize);
    }

    [TestMethod]
    public void PrimaryPlanner_IncludesLibrariesNativesClientAndAssetIndex()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var metadata = MinecraftVersionJson.Parse("""
            {
              "id": "1.21.4",
              "mainClass": "net.minecraft.client.main.Main",
              "javaVersion": { "component": "java-runtime-delta", "majorVersion": 21 },
              "downloads": {
                "client": {
                  "sha1": "0123456789abcdef0123456789abcdef01234567",
                  "size": 100,
                  "url": "https://example.invalid/client.jar"
                }
              },
              "assetIndex": {
                "id": "19",
                "sha1": "89abcdef0123456789abcdef0123456789abcdef",
                "size": 200,
                "url": "https://example.invalid/19.json"
              },
              "libraries": [
                { "name": "com.example:demo:1.0.0" },
                {
                  "name": "org.lwjgl:lwjgl:3.3.3",
                  "natives": { "linux": "natives-linux" }
                }
              ]
            }
            """);
            var chain = new MinecraftVersionChain(new[] { metadata });
            var context = new MinecraftRuleContext(
                new PlatformTarget(PlatformOperatingSystem.Linux, PlatformArchitecture.X64),
                "test",
                new Dictionary<string, bool>());
            var runtime = MinecraftRuntimePlanner.Build(
                chain,
                context,
                root,
                Path.Combine(root, "natives"),
                new[] { new JavaRuntimeDescriptor("/java", 21, PlatformArchitecture.X64) });

            var plan = MinecraftPrimaryArtifactPlanner.Build(chain, runtime);

            Assert.IsTrue(plan.Artifacts.Any(a => a.Purpose == MinecraftArtifactPurpose.Library));
            Assert.IsTrue(plan.Artifacts.Any(a => a.Purpose == MinecraftArtifactPurpose.NativeLibrary));
            Assert.IsTrue(plan.Artifacts.Any(a => a.Purpose == MinecraftArtifactPurpose.Client));
            Assert.IsTrue(plan.Artifacts.Any(a => a.Purpose == MinecraftArtifactPurpose.AssetIndex));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AssetIndex_DeduplicatesObjectsByHashAndBuildsCanonicalPaths()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            const string hash = "0123456789abcdef0123456789abcdef01234567";
            var index = MinecraftAssetIndexJson.Parse($$"""
            {
              "virtual": true,
              "objects": {
                "minecraft/lang/en_us.json": { "hash": "{{hash}}", "size": 42 },
                "duplicate/name": { "hash": "{{hash}}", "size": 42 }
              }
            }
            """);

            var plan = MinecraftAssetObjectPlanner.Build(index, root);

            Assert.IsTrue(index.Virtual);
            Assert.AreEqual(1, plan.Artifacts.Count);
            StringAssert.EndsWith(
                plan.Artifacts[0].LocalPath,
                Path.Combine("assets", "objects", "01", hash));
            Assert.AreEqual(
                $"https://resources.download.minecraft.net/01/{hash}",
                plan.Artifacts[0].Sources.Single());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Verifier_DetectsValidHashAndHashMismatch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var file = Path.Combine(root, "artifact.bin");
            var bytes = Encoding.UTF8.GetBytes("pcl3-artifact");
            await File.WriteAllBytesAsync(file, bytes);
#pragma warning disable CA5350 // Test mirrors Mojang's SHA-1 manifest format.
            var expected = Convert.ToHexStringLower(SHA1.HashData(bytes));
#pragma warning restore CA5350

            var valid = new MinecraftArtifactRequest(
                "test",
                MinecraftArtifactPurpose.Library,
                file,
                Array.Empty<string>(),
                expected,
                bytes.Length);
            var invalid = valid with
            {
                Sha1 = "0000000000000000000000000000000000000000"
            };

            Assert.IsTrue((await MinecraftArtifactVerifier.VerifyAsync(valid)).IsValid);
            Assert.AreEqual(
                MinecraftArtifactVerificationStatus.HashMismatch,
                (await MinecraftArtifactVerifier.VerifyAsync(invalid)).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SafeNativeExtractor_ExcludesMetaInfAndRejectsZipSlip()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var goodZip = Path.Combine(root, "good.zip");
            using (var archive = ZipFile.Open(goodZip, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "META-INF/MANIFEST.MF", "ignored");
                WriteEntry(archive, "org/lwjgl/libnative.so", "native");
            }

            var destination = Path.Combine(root, "natives");
            await SafeNativeExtractor.ExtractAsync(new NativeExtractionPlan(
                destination,
                new[]
                {
                    new NativeExtractionArchive(goodZip, new[] { "META-INF/" }, "test")
                }));

            Assert.IsFalse(File.Exists(Path.Combine(destination, "META-INF", "MANIFEST.MF")));
            Assert.IsTrue(File.Exists(Path.Combine(destination, "org", "lwjgl", "libnative.so")));

            var badZip = Path.Combine(root, "bad.zip");
            using (var archive = ZipFile.Open(badZip, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "../escape.dll", "bad");
            }

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                SafeNativeExtractor.ExtractAsync(new NativeExtractionPlan(
                    destination,
                    new[]
                    {
                        new NativeExtractionArchive(badZip, Array.Empty<string>(), "test")
                    })));
            Assert.IsFalse(File.Exists(Path.Combine(root, "escape.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(value);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
