using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Artifacts;
using PCL3.Minecraft.Natives;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class ArtifactSecurityTests
{
    [TestMethod]
    public async Task HttpAcquirer_RejectsHttpsRedirectedToHttpByDefault()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var payload = Encoding.UTF8.GetBytes("redirected");
            using var client = new HttpClient(new FinalUriHandler(
                payload,
                new Uri("http://downgrade.invalid/artifact.jar")));
            var acquirer = new HttpMinecraftArtifactAcquirer(
                client,
                new MinecraftArtifactAcquirerOptions(AttemptsPerSource: 1));
            var target = Path.Combine(root, "artifact.jar");
            var request = new MinecraftArtifactRequest(
                "redirect",
                MinecraftArtifactPurpose.Library,
                target,
                new[] { "https://example.invalid/artifact.jar" },
                ComputeSha1(payload),
                payload.Length);

            var result = await acquirer.AcquireAsync(
                new MinecraftArtifactAcquisitionPlan(new[] { request }));

            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(File.Exists(target));
            StringAssert.Contains(result.Items.Single().Error, "insecure HTTP");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task SafeNativeExtractor_RejectsEntryAboveConfiguredExpansionLimit()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = Path.Combine(root, "native.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("native.dll");
                await using var stream = entry.Open();
                await stream.WriteAsync(Encoding.UTF8.GetBytes("too-large"));
            }

            var plan = new NativeExtractionPlan(
                Path.Combine(root, "natives"),
                new[]
                {
                    new NativeExtractionArchive(archivePath, Array.Empty<string>(), "test")
                });

            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                SafeNativeExtractor.ExtractAsync(
                    plan,
                    options: new SafeNativeExtractionOptions(MaxEntryBytes: 3)));
            Assert.IsFalse(File.Exists(Path.Combine(root, "natives", "native.dll")));
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
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-security-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FinalUriHandler(byte[] payload, Uri finalUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(payload);
            content.Headers.ContentLength = payload.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri),
                Content = content
            });
        }
    }
}
