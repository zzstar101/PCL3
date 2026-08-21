using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PCL3.Minecraft.Accounts.Authentication.Microsoft;

public sealed class MicrosoftAuthenticationClient
{
    private static readonly Uri XboxUserAuthenticationEndpoint =
        new("https://user.auth.xboxlive.com/user/authenticate");
    private static readonly Uri XstsAuthorizationEndpoint =
        new("https://xsts.auth.xboxlive.com/xsts/authorize");
    private static readonly Uri MinecraftLoginEndpoint =
        new("https://api.minecraftservices.com/authentication/login_with_xbox");
    private static readonly Uri MinecraftProfileEndpoint =
        new("https://api.minecraftservices.com/minecraft/profile");

    private readonly HttpClient _httpClient;
    private readonly MicrosoftAuthenticationOptions _options;
    private readonly TimeProvider _timeProvider;

    public MicrosoftAuthenticationClient(
        HttpClient httpClient,
        MicrosoftAuthenticationOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public MicrosoftAuthenticationOptions Options => _options;

    public async Task<MicrosoftDeviceCodeChallenge> BeginDeviceCodeAsync(
        CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["scope"] = _options.Scope
        });
        using var response = await _httpClient.PostAsync(
            _options.DeviceCodeEndpoint,
            content,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpExceptionAsync(
                MicrosoftAuthenticationStage.DeviceCode,
                response,
                cancellationToken).ConfigureAwait(false);
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var deviceCode = GetRequiredString(root, "device_code", MicrosoftAuthenticationStage.DeviceCode);
        var userCode = GetRequiredString(root, "user_code", MicrosoftAuthenticationStage.DeviceCode);
        var verificationUriValue = GetRequiredString(
            root,
            "verification_uri",
            MicrosoftAuthenticationStage.DeviceCode);
        var expiresIn = GetRequiredPositiveInt32(
            root,
            "expires_in",
            MicrosoftAuthenticationStage.DeviceCode);
        var interval = GetRequiredPositiveInt32(
            root,
            "interval",
            MicrosoftAuthenticationStage.DeviceCode);

        if (!Uri.TryCreate(verificationUriValue, UriKind.Absolute, out var verificationUri))
        {
            throw ProtocolError(
                MicrosoftAuthenticationStage.DeviceCode,
                "Microsoft device-code response contains an invalid verification URI.");
        }

        return new MicrosoftDeviceCodeChallenge(
            deviceCode,
            userCode,
            verificationUri,
            _timeProvider.GetUtcNow().AddSeconds(expiresIn),
            TimeSpan.FromSeconds(interval),
            TryGetString(root, "message"));
    }

    public async Task<MicrosoftDeviceCodePollResult> PollDeviceCodeAsync(
        MicrosoftDeviceCodeChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        if (_timeProvider.GetUtcNow() >= challenge.ExpiresAt)
        {
            return new MicrosoftDeviceCodePollResult(MicrosoftDeviceCodePollStatus.Expired);
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = _options.ClientId,
            ["device_code"] = challenge.DeviceCode
        });
        using var response = await _httpClient.PostAsync(
            _options.TokenEndpoint,
            content,
            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            return new MicrosoftDeviceCodePollResult(
                MicrosoftDeviceCodePollStatus.Authorized,
                ParseOAuthToken(document.RootElement, fallbackRefreshToken: null));
        }

        if (response.StatusCode is HttpStatusCode.BadRequest)
        {
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var error = TryGetString(document.RootElement, "error") ?? "unknown_error";
            return new MicrosoftDeviceCodePollResult(
                error switch
                {
                    "authorization_pending" => MicrosoftDeviceCodePollStatus.Pending,
                    "authorization_declined" => MicrosoftDeviceCodePollStatus.Declined,
                    "expired_token" => MicrosoftDeviceCodePollStatus.Expired,
                    "slow_down" => MicrosoftDeviceCodePollStatus.SlowDown,
                    _ => MicrosoftDeviceCodePollStatus.Failed
                },
                ErrorCode: error);
        }

        throw await CreateHttpExceptionAsync(
            MicrosoftAuthenticationStage.MicrosoftToken,
            response,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<MicrosoftOAuthToken> WaitForDeviceCodeAsync(
        MicrosoftDeviceCodeChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var interval = challenge.PollingInterval;
        while (_timeProvider.GetUtcNow() < challenge.ExpiresAt)
        {
            await Task.Delay(interval, _timeProvider, cancellationToken).ConfigureAwait(false);
            var result = await PollDeviceCodeAsync(challenge, cancellationToken).ConfigureAwait(false);
            switch (result.Status)
            {
                case MicrosoftDeviceCodePollStatus.Authorized:
                    return result.Token ?? throw ProtocolError(
                        MicrosoftAuthenticationStage.MicrosoftToken,
                        "Microsoft token response did not contain a token.");
                case MicrosoftDeviceCodePollStatus.Pending:
                    continue;
                case MicrosoftDeviceCodePollStatus.SlowDown:
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case MicrosoftDeviceCodePollStatus.Declined:
                    throw new MicrosoftAuthenticationException(
                        MicrosoftAuthenticationStage.MicrosoftToken,
                        "Microsoft device-code authorization was declined by the user.",
                        errorCode: result.ErrorCode);
                case MicrosoftDeviceCodePollStatus.Expired:
                    throw new MicrosoftAuthenticationException(
                        MicrosoftAuthenticationStage.MicrosoftToken,
                        "Microsoft device code expired before authorization completed.",
                        errorCode: result.ErrorCode);
                default:
                    throw new MicrosoftAuthenticationException(
                        MicrosoftAuthenticationStage.MicrosoftToken,
                        $"Microsoft device-code authorization failed with error '{result.ErrorCode ?? "unknown_error"}'.",
                        errorCode: result.ErrorCode);
            }
        }

        throw new MicrosoftAuthenticationException(
            MicrosoftAuthenticationStage.MicrosoftToken,
            "Microsoft device code expired before authorization completed.",
            errorCode: "expired_token");
    }

    public async Task<MicrosoftOAuthToken> RefreshMicrosoftTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _options.ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = _options.Scope
        });
        using var response = await _httpClient.PostAsync(
            _options.TokenEndpoint,
            content,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpExceptionAsync(
                MicrosoftAuthenticationStage.MicrosoftToken,
                response,
                cancellationToken).ConfigureAwait(false);
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseOAuthToken(document.RootElement, refreshToken);
    }

    public async Task<MicrosoftMinecraftSessionResult> ExchangeForMinecraftSessionAsync(
        MicrosoftOAuthToken microsoftToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microsoftToken);

        var xboxLive = await AuthenticateXboxLiveAsync(
            microsoftToken.AccessToken,
            cancellationToken).ConfigureAwait(false);
        var xsts = await AuthorizeXstsAsync(
            xboxLive.Token,
            cancellationToken).ConfigureAwait(false);
        var userHash = !string.IsNullOrWhiteSpace(xsts.UserHash)
            ? xsts.UserHash
            : xboxLive.UserHash;
        var minecraftToken = await LoginMinecraftAsync(
            userHash,
            xsts.Token,
            cancellationToken).ConfigureAwait(false);
        var profile = await GetMinecraftProfileAsync(
            minecraftToken.AccessToken,
            cancellationToken).ConfigureAwait(false);

        var session = new MinecraftSession(
            profile.Name,
            profile.Id,
            minecraftToken.AccessToken,
            userType: "msa",
            xuid: xsts.Xuid,
            clientId: _options.ClientId);

        return new MicrosoftMinecraftSessionResult(
            session,
            _timeProvider.GetUtcNow().AddSeconds(minecraftToken.ExpiresIn));
    }

    private async Task<XboxToken> AuthenticateXboxLiveAsync(
        string microsoftAccessToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + microsoftAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        };

        using var request = CreateJsonPost(XboxUserAuthenticationEndpoint, payload);
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpExceptionAsync(
                MicrosoftAuthenticationStage.XboxLive,
                response,
                cancellationToken).ConfigureAwait(false);
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseXboxToken(document.RootElement, MicrosoftAuthenticationStage.XboxLive);
    }

    private async Task<XboxToken> AuthorizeXstsAsync(
        string xboxLiveToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xboxLiveToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        };

        using var request = CreateJsonPost(XstsAuthorizationEndpoint, payload);
        request.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpExceptionAsync(
                MicrosoftAuthenticationStage.Xsts,
                response,
                cancellationToken).ConfigureAwait(false);
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return ParseXboxToken(document.RootElement, MicrosoftAuthenticationStage.Xsts);
    }

    private async Task<MinecraftAccessToken> LoginMinecraftAsync(
        string userHash,
        string xstsToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        };

        using var request = CreateJsonPost(MinecraftLoginEndpoint, payload);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpExceptionAsync(
                MicrosoftAuthenticationStage.MinecraftServices,
                response,
                cancellationToken).ConfigureAwait(false);
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        return new MinecraftAccessToken(
            GetRequiredString(root, "access_token", MicrosoftAuthenticationStage.MinecraftServices),
            GetRequiredPositiveInt32(root, "expires_in", MicrosoftAuthenticationStage.MinecraftServices));
    }

    private async Task<MinecraftProfile> GetMinecraftProfileAsync(
        string minecraftAccessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpExceptionAsync(
                MicrosoftAuthenticationStage.MinecraftProfile,
                response,
                cancellationToken).ConfigureAwait(false);
        }

        using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        return new MinecraftProfile(
            GetRequiredString(root, "id", MicrosoftAuthenticationStage.MinecraftProfile),
            GetRequiredString(root, "name", MicrosoftAuthenticationStage.MinecraftProfile));
    }

    private MicrosoftOAuthToken ParseOAuthToken(
        JsonElement root,
        string? fallbackRefreshToken)
    {
        var accessToken = GetRequiredString(
            root,
            "access_token",
            MicrosoftAuthenticationStage.MicrosoftToken);
        var refreshToken = TryGetString(root, "refresh_token") ?? fallbackRefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw ProtocolError(
                MicrosoftAuthenticationStage.MicrosoftToken,
                "Microsoft token response did not include a refresh token. Ensure offline_access was granted.");
        }

        var expiresIn = GetRequiredPositiveInt32(
            root,
            "expires_in",
            MicrosoftAuthenticationStage.MicrosoftToken);
        return new MicrosoftOAuthToken(
            accessToken,
            refreshToken,
            _timeProvider.GetUtcNow().AddSeconds(expiresIn),
            TryGetString(root, "scope"));
    }

    private static XboxToken ParseXboxToken(
        JsonElement root,
        MicrosoftAuthenticationStage stage)
    {
        var token = GetRequiredString(root, "Token", stage);
        if (!root.TryGetProperty("DisplayClaims", out var displayClaims) ||
            displayClaims.ValueKind is not JsonValueKind.Object ||
            !displayClaims.TryGetProperty("xui", out var xui) ||
            xui.ValueKind is not JsonValueKind.Array ||
            xui.GetArrayLength() == 0)
        {
            throw ProtocolError(stage, "Xbox token response did not contain xui display claims.");
        }

        var claim = xui[0];
        var userHash = GetRequiredString(claim, "uhs", stage);
        return new XboxToken(token, userHash, TryGetString(claim, "xid"));
    }

    private static HttpRequestMessage CreateJsonPost<T>(Uri uri, T payload) =>
        new(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload)
        };

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new MicrosoftAuthenticationException(
                MicrosoftAuthenticationStage.MinecraftServices,
                "Authentication endpoint returned malformed JSON.",
                response.StatusCode,
                innerException: exception);
        }
    }

    private static async Task<MicrosoftAuthenticationException> CreateHttpExceptionAsync(
        MicrosoftAuthenticationStage stage,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? errorCode = null;
        try
        {
            using var document = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            errorCode = TryGetString(document.RootElement, "error") ??
                TryGetInt64(document.RootElement, "XErr")?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (MicrosoftAuthenticationException)
        {
            // Keep HTTP failures useful even when the endpoint returned non-JSON content.
        }

        var suffix = string.IsNullOrWhiteSpace(errorCode)
            ? string.Empty
            : $" Error code: {errorCode}.";
        return new MicrosoftAuthenticationException(
            stage,
            $"Authentication request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{suffix}",
            response.StatusCode,
            errorCode);
    }

    private static string GetRequiredString(
        JsonElement element,
        string propertyName,
        MicrosoftAuthenticationStage stage)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw ProtocolError(stage, $"Authentication response is missing string '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static int GetRequiredPositiveInt32(
        JsonElement element,
        string propertyName,
        MicrosoftAuthenticationStage stage)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            !property.TryGetInt32(out var value) ||
            value <= 0)
        {
            throw ProtocolError(stage, $"Authentication response has invalid integer '{propertyName}'.");
        }

        return value;
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;

    private static long? TryGetInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;

    private static MicrosoftAuthenticationException ProtocolError(
        MicrosoftAuthenticationStage stage,
        string message) =>
        new(stage, message);

    private sealed record XboxToken(string Token, string UserHash, string? Xuid);

    private sealed record MinecraftAccessToken(string AccessToken, int ExpiresIn);

    private sealed record MinecraftProfile(string Id, string Name);
}
