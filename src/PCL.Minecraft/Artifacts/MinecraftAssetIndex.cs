using System.Text.Json;

namespace PCL3.Minecraft.Artifacts;

public sealed record MinecraftAssetObject(
    string Name,
    string Hash,
    long Size);

public sealed record MinecraftAssetIndex(
    IReadOnlyList<MinecraftAssetObject> Objects,
    bool Virtual,
    bool MapToResources);

public static class MinecraftAssetIndexJson
{
    public static MinecraftAssetIndex Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("Asset index root must be an object.");
        }

        if (!root.TryGetProperty("objects", out var objectsElement) ||
            objectsElement.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("Asset index must contain an 'objects' object.");
        }

        var objects = new List<MinecraftAssetObject>();
        foreach (var property in objectsElement.EnumerateObject())
        {
            if (property.Value.ValueKind is not JsonValueKind.Object)
            {
                throw new JsonException($"Asset '{property.Name}' must be an object.");
            }

            if (!property.Value.TryGetProperty("hash", out var hashElement) ||
                hashElement.ValueKind is not JsonValueKind.String)
            {
                throw new JsonException($"Asset '{property.Name}' is missing a string hash.");
            }

            var hash = hashElement.GetString()!.ToLowerInvariant();
            if (hash.Length != 40 || !hash.All(Uri.IsHexDigit))
            {
                throw new JsonException($"Asset '{property.Name}' has an invalid SHA-1 hash.");
            }

            if (!property.Value.TryGetProperty("size", out var sizeElement) ||
                !sizeElement.TryGetInt64(out var size) ||
                size < 0)
            {
                throw new JsonException($"Asset '{property.Name}' has an invalid size.");
            }

            objects.Add(new MinecraftAssetObject(property.Name, hash, size));
        }

        return new MinecraftAssetIndex(
            objects.OrderBy(asset => asset.Name, StringComparer.Ordinal).ToArray(),
            GetOptionalBoolean(root, "virtual"),
            GetOptionalBoolean(root, "map_to_resources"));
    }

    private static bool GetOptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is JsonValueKind.Null)
        {
            return false;
        }

        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new JsonException($"'{propertyName}' must be a boolean.");
        }

        return property.GetBoolean();
    }
}

public static class MinecraftAssetObjectPlanner
{
    private const string ResourceBaseUrl = "https://resources.download.minecraft.net/";

    public static MinecraftArtifactAcquisitionPlan Build(
        MinecraftAssetIndex assetIndex,
        string minecraftDirectory)
    {
        ArgumentNullException.ThrowIfNull(assetIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);

        var assetsDirectory = Path.Combine(Path.GetFullPath(minecraftDirectory), "assets", "objects");
        var byHash = new Dictionary<string, MinecraftArtifactRequest>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assetIndex.Objects)
        {
            var prefix = asset.Hash[..2];
            if (!byHash.TryGetValue(asset.Hash, out var request))
            {
                request = new MinecraftArtifactRequest(
                    $"asset:{asset.Hash}",
                    MinecraftArtifactPurpose.AssetObject,
                    Path.Combine(assetsDirectory, prefix, asset.Hash),
                    new[] { $"{ResourceBaseUrl}{prefix}/{asset.Hash}" },
                    asset.Hash,
                    asset.Size).Normalize();
                byHash.Add(asset.Hash, request);
            }
            else if (request.Size != asset.Size)
            {
                throw new InvalidDataException(
                    $"Asset hash '{asset.Hash}' is associated with conflicting sizes.");
            }
        }

        return new MinecraftArtifactAcquisitionPlan(byHash.Values.ToArray());
    }
}
