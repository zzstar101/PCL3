using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Metadata;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class FileSystemMinecraftVersionMetadataSourceTests
{
    [TestMethod]
    public async Task GetAsync_ReadsStandardVersionLayout()
    {
        using var fixture = new MinecraftDirectoryFixture();
        fixture.WriteVersion("1.21.8", """
        {
          "id": "1.21.8",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main"
        }
        """);

        var source = new FileSystemMinecraftVersionMetadataSource(fixture.Root);
        var metadata = await source.GetAsync("1.21.8");

        Assert.IsNotNull(metadata);
        Assert.AreEqual("1.21.8", metadata.Id);
        Assert.AreEqual("net.minecraft.client.main.Main", metadata.MainClass);
    }

    [TestMethod]
    public async Task GetAsync_RefreshesCacheWhenFileLengthChanges()
    {
        using var fixture = new MinecraftDirectoryFixture();
        fixture.WriteVersion("test", """
        { "id": "test", "mainClass": "a.Main" }
        """);

        var source = new FileSystemMinecraftVersionMetadataSource(fixture.Root);
        var first = await source.GetAsync("test");

        fixture.WriteVersion("test", """
        { "id": "test", "mainClass": "a.MuchLongerMainClass" }
        """);

        var second = await source.GetAsync("test");

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual("a.Main", first.MainClass);
        Assert.AreEqual("a.MuchLongerMainClass", second.MainClass);
    }

    [TestMethod]
    public async Task GetAsync_ReturnsNullWhenVersionDoesNotExist()
    {
        using var fixture = new MinecraftDirectoryFixture();
        var source = new FileSystemMinecraftVersionMetadataSource(fixture.Root);

        var metadata = await source.GetAsync("missing");

        Assert.IsNull(metadata);
    }

    [TestMethod]
    public void GetMetadataPath_RejectsTraversal()
    {
        using var fixture = new MinecraftDirectoryFixture();
        var source = new FileSystemMinecraftVersionMetadataSource(fixture.Root);

        Assert.ThrowsExactly<ArgumentException>(() =>
            source.GetMetadataPath("../outside"));
    }

    private sealed class MinecraftDirectoryFixture : IDisposable
    {
        public MinecraftDirectoryFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"pcl3-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void WriteVersion(string id, string json)
        {
            var directory = Path.Combine(Root, "versions", id);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{id}.json"), json);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
