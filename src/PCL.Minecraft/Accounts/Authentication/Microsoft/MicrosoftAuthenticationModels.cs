using System.Net;

namespace PCL3.Minecraft.Accounts.Authentication.Microsoft;

public sealed class MicrosoftAuthenticationOptions
{
    public MicrosoftAuthenticationOptions(
        string clientId,
        string tenant = "consumers",
        string scope = "XboxLive.signin offline_access")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        if (!Guid.TryParse(clientId, out _))
        {
            throw new ArgumentException(
                "Microsoft application client ID must be a GUID.",
                nameof(clientId));
        }

        if (!string.Equals(tenant, "consumers", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Minecraft Microsoft authentication requires the consumers tenant.",
                nameof(tenant));
        }

        var scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!scopes.Contains("XboxLive.signin", StringComparer.OrdinalIgnoreCase) ||
            !scopes.Contains("offline_access", StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Minecraft Microsoft authentication requires XboxLive.signin and offline_access scopes.",
                nameof(scope));
        }

        ClientId = clientId;
        Tenant = tenant;
        Scope = string.Join(' ', scopes.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public string ClientId { get; }

    public string Tenant { get; }

    public string Scope { get; }

    public Uri DeviceCodeEndpoint => new(
        $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/devicecode");

    public Uri TokenEndpoint => new(
        $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0/token");
}

public sealed class MicrosoftDeviceCodeChallenge
{
    public MicrosoftDeviceCodeChallenge(
        string deviceCode,
        string userCode,
        Uri verificationUri,
        DateTimeOffset expiresAt,
        TimeSpan pollingInterval,
        string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(userCode);
        ArgumentNullException.ThrowIfNull(verificationUri);

        if (!verificationUri.IsAbsoluteUri || verificationUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Microsoft device-code verification URI must be an absolute HTTPS URI.",
                nameof(verificationUri));
        }

        if (pollingInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval));
        }

        DeviceCode = deviceCode;
        UserCode = userCode;
        VerificationUri = verificationUri;
        ExpiresAt = expiresAt;
        PollingInterval = pollingInterval;
        Message = message;
    }

    public string DeviceCode { get; }

    public string UserCode { get; }

    public Uri VerificationUri { get; }

    public DateTimeOffset ExpiresAt { get; }

    public TimeSpan PollingInterval { get; }

    public string? Message { get; }

    public override string ToString() =>
        $"MicrosoftDeviceCodeChallenge(UserCode={UserCode}, VerificationUri={VerificationUri}, ExpiresAt={ExpiresAt:O}, PollingInterval={PollingInterval}, DeviceCode=<redacted>)";
}

public sealed class MicrosoftOAuthToken
{
    public MicrosoftOAuthToken(
        string accessToken,
        string refreshToken,
        DateTimeOffset expiresAt,
        string? scope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
        Scope = scope;
    }

    public string AccessToken { get; }

    public string RefreshToken { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string? Scope { get; }

    public override string ToString() =>
        $"MicrosoftOAuthToken(ExpiresAt={ExpiresAt:O}, Scope={Scope}, AccessToken=<redacted>, RefreshToken=<redacted>)";
}

public enum MicrosoftDeviceCodePollStatus
{
    Pending,
    Authorized,
    Declined,
    Expired,
    SlowDown,
    Failed
}

public sealed record MicrosoftDeviceCodePollResult(
    MicrosoftDeviceCodePollStatus Status,
    MicrosoftOAuthToken? Token = null,
    string? ErrorCode = null);

public enum MicrosoftAuthenticationStage
{
    DeviceCode,
    MicrosoftToken,
    XboxLive,
    Xsts,
    MinecraftServices,
    MinecraftProfile,
    CredentialStore
}

public sealed class MicrosoftAuthenticationException : Exception
{
    public MicrosoftAuthenticationException(
        MicrosoftAuthenticationStage stage,
        string message,
        HttpStatusCode? statusCode = null,
        string? errorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Stage = stage;
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public MicrosoftAuthenticationStage Stage { get; }

    public HttpStatusCode? StatusCode { get; }

    public string? ErrorCode { get; }
}

public sealed record MicrosoftMinecraftSessionResult(
    MinecraftSession Session,
    DateTimeOffset MinecraftAccessTokenExpiresAt);
