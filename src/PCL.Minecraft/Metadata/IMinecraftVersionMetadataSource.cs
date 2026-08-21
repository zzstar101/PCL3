namespace PCL3.Minecraft.Metadata;

public interface IMinecraftVersionMetadataSource
{
    ValueTask<MinecraftVersionMetadata?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);
}
