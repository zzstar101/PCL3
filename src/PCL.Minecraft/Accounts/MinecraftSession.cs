using System.Text.Json;

namespace PCL3.Minecraft.Accounts;

/// <summary>
/// Authentication-provider-neutral session data required by Minecraft's launch arguments.
/// Token acquisition/refresh belongs to account providers, not the launch core.
/// </summary>
public sealed class MinecraftSession
{
    public MinecraftSession(
        string playerName,
        string playerUuid,
        string? accessToken = null,
        string userType = "msa",
        string userPropertiesJson = "{}",
        string? xuid = null,
        string? clientId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerUuid);
        ArgumentException.ThrowIfNullOrWhiteSpace(userType);

        PlayerName = playerName;
        PlayerUuid = NormalizeUuid(playerUuid);
        AccessToken = accessToken;
        UserType = userType;
        UserPropertiesJson = NormalizeUserProperties(userPropertiesJson);
        Xuid = string.IsNullOrWhiteSpace(xuid) ? null : xuid;
        ClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId;
    }

    public string PlayerName { get; }

    /// <summary>Canonical lower-case UUID without hyphens.</summary>
    public string PlayerUuid { get; }

    /// <summary>
    /// Provider-issued Minecraft access token. This may be null for an offline session.
    /// Never include it in diagnostic ToString output.
    /// </summary>
    public string? AccessToken { get; }

    public string UserType { get; }

    public string UserPropertiesJson { get; }

    public string? Xuid { get; }

    public string? ClientId { get; }

    public string LaunchAccessToken => string.IsNullOrEmpty(AccessToken) ? "0" : AccessToken;

    public static MinecraftSession CreateOffline(string playerName, Guid playerUuid) =>
        new(playerName, playerUuid.ToString("N"), accessToken: null, userType: "legacy");

    public override string ToString() =>
        $"MinecraftSession(PlayerName={PlayerName}, PlayerUuid={PlayerUuid}, UserType={UserType}, AccessToken=<redacted>)";

    private static string NormalizeUuid(string value)
    {
        if (!Guid.TryParseExact(value, "N", out var uuid) && !Guid.TryParse(value, out uuid))
        {
            throw new ArgumentException("Minecraft player UUID must be a valid UUID.", nameof(value));
        }

        return uuid.ToString("N");
    }

    private static string NormalizeUserProperties(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
            {
                throw new ArgumentException("Minecraft user properties must be a JSON object.", nameof(value));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Minecraft user properties must contain valid JSON.", nameof(value), exception);
        }

        return value;
    }
}
