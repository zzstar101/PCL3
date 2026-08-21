using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Artifacts;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class ArtifactAcquirerTests
{
    [TestMethod]
    public async Task HttpAcquirer_DownloadsVerifiesAndAtomicallyCommitsArtifact()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var payload = Encoding.UTF8.GetBytes("downloaded-artifact");
            var sha1 = ComputeSha1(payload);
            var handler = new StaticContentHandler(payload);
            using var client = new HttpClient(handler);
            var acquirer = new HttpMinecraftArtifactAcquirer(client);
            var target = Path.Combine(root, "libraries", "artifact.jar");
            var request = new MinecraftArtifactRequest(
                "library:test",
                MinecraftArtifactPurpose.Library,
                target,
                new[] { "https://example.invalid/artifact.jar" },
                sha1,
                payload.Length);

            var result = await acquirer.AcquireAsync(
                new MinecraftArtifactAcquisitionPlan(new[] { request }));

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(MinecraftArtifactAcquisitionStatus.Downloaded, result.Items.Single().Status);
            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(target));
            Assert.AreEqual(1, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task HttpAcquirer_SkipsAlreadyValidArtifactWithoutNetworkRequest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var payload = Encoding.UTF8.GetBytes("cached-artifact");
            var target = Path.Combine(root, "artifact.jar");
            await File.WriteAllBytesAsync(target, payload);
            var handler = new StaticContentHandler(Encoding.UTF8.GetBytes("should-not-be-used"));
            using var client = new HttpClient(handler);
            var acquirer = new HttpMinecraftArtifactAcquirer(client);
            var request = new MinecraftArtifactRequest(
                "cached",
                MinecraftArtifactPurpose.Library,
                target,
                new[] { "https://example.invalid/artifact.jar" },
                ComputeSha1(payload),
                payload.Length);

            var result = await acquirer.AcquireAsync(
                new MinecraftArtifactAcquisitionPlan(new[] { request }));

            Assert.AreEqual(MinecraftArtifactAcquisitionStatus.AlreadyValid, result.Items.Single().Status);
            Assert.AreEqual(0, handler.RequestCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task HttpAcquirer_LeavesExistingFileUntouchedWhenDownloadedHashIsInvalid()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var oldPayload = Encoding.UTF8.GetBytes("old-corrupt-cache");
            var newPayload = Encoding.UTF8.GetBytes("network-payload");
            var target = Path.Combine(root, "artifact.jar");
            await File.WriteAllBytesAsync(target, oldPayload);
            var handler = new StaticContentHandler(newPayload);
            using var client = new HttpClient(handler);
            var acquirer = new HttpMinecraftArtifactAcquirer(
                client,
                new MinecraftArtifactAcquirerOptions(AttemptsPerSource: 1));
            var request = new MinecraftArtifactRequest(
                "invalid",
                MinecraftArtifactPurpose.Library,
                target,
                new[] { "https://example.invalid/artifact.jar" },
                "0000000000000000000000000000000000000000",
                newPayload.Length);

            var result = await acquirer.AcquireAsync(
                new MinecraftArtifactAcquisitionPlan(new[] { request }));

            Assert.IsFalse(result.IsSuccess);
            CollectionAssert.AreEqual(oldPayload, await File.ReadAllBytesAsync(target));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task AssetMaterializer_WritesLegacyVirtualAndResourcesTrees()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            const string hash = "0123456789abcdef0123456789abcdef01234567";
            var objectPath = Path.Combine(root, "assets", "objects", "01", hash);
            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
            await File.WriteAllTextAsync(objectPath, "asset");
            var index = new MinecraftAssetIndex(
                new[] { new MinecraftAssetObject("minecraft/lang/en_us.json", hash, 5) },
                Virtual: true,
                MapToResources: true);

            var plan = MinecraftAssetMaterializationPlanner.Build(index, "legacy", root);
            await MinecraftAssetMaterializer.MaterializeAsync(plan);

            Assert.AreEqual(2, plan.Files.Count);
            Assert.IsTrue(File.Exists(Path.Combine(
                root, "assets", "virtual", "legacy", "minecraft", "lang", "en_us.json")));
            Assert.IsTrue(File.Exists(Path.Combine(
                root, "resources", "minecraft", "lang", "en_us.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void AssetMaterializationPlanner_RejectsTraversalLogicalName()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var index = new MinecraftAssetIndex(
                new[]
                {
                    new MinecraftAssetObject(
                        "../escape.txt",
                        "0123456789abcdef0123456789abcdef01234567",
                        1)
                },
                Virtual: true,
                MapToResources: false);

            Assert.ThrowsExactly<InvalidDataException>(() =>
                MinecraftAssetMaterializationPlanner.Build(index, "legacy", root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ComputeSha1(byte[] payload)
    {
#pragma warning disable CA5350 // Test mirrors Mojang's SHA-1 manifest format.
        return Convert.ToHexStringLower(SHA1.HashData(payload));
#pragma warning restore CA5350
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-acquire-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StaticContentHandler(byte[] payload) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var content = new ByteArrayContent(payload);
            content.Headers.ContentLength = payload.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = content
            });
        }
    }
}
