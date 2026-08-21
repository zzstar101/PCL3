using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Metadata;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MinecraftVersionChainResolverTests
{
    [TestMethod]
    public async Task ResolveAsync_PreservesChildToParentOrder()
    {
        var source = new InMemorySource(
            Version("forge", "1.20.1", mainClass: "forge.Main"),
            Version("1.20.1", mainClass: "vanilla.Main"));

        var chain = await MinecraftVersionChainResolver.ResolveAsync("forge", source);

        CollectionAssert.AreEqual(
            new[] { "forge", "1.20.1" },
            chain.Versions.Select(version => version.Id).ToArray());
        Assert.AreEqual("forge.Main", chain.EffectiveMainClass);
    }

    [TestMethod]
    public async Task ResolveAsync_DetectsInheritanceCycle()
    {
        var source = new InMemorySource(
            Version("a", "b"),
            Version("b", "a"));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await MinecraftVersionChainResolver.ResolveAsync("a", source));
    }

    private static MinecraftVersionMetadata Version(
        string id,
        string? inheritsFrom = null,
        string? mainClass = null) =>
        new(
            id,
            "release",
            mainClass,
            inheritsFrom,
            null,
            Array.Empty<MinecraftArgument>(),
            Array.Empty<MinecraftArgument>(),
            null,
            Array.Empty<MinecraftLibrary>());

    private sealed class InMemorySource(params MinecraftVersionMetadata[] versions)
        : IMinecraftVersionMetadataSource
    {
        private readonly IReadOnlyDictionary<string, MinecraftVersionMetadata> _versions =
            versions.ToDictionary(version => version.Id, StringComparer.Ordinal);

        public ValueTask<MinecraftVersionMetadata?> GetAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _versions.TryGetValue(id, out var value);
            return ValueTask.FromResult(value);
        }
    }
}
