using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Accounts.Authentication;
using PCL3.Minecraft.Accounts.Authentication.Microsoft;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MicrosoftAuthenticationTests
{
    private const string ClientId = "11111111-2222-3333-4444-555555555555";

    [TestMethod]
    public async Task DeviceCode_UsesConsumersTenantRequiredScopesAndRedactsDeviceCode()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            Assert.AreEqual(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
                request.RequestUri?.AbsoluteUri);
            var form = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            StringAssert.Contains(form, "client_id=11111111-2222-3333-4444-555555555555");
            StringAssert.Contains(form, "XboxLive.signin");
            StringAssert.Contains(form, "offline_access");
            return Json(HttpStatusCode.OK, """
            {
              "device_code": "device-secret",
              "user_code": "ABCD-EFGH",
              "verification_uri": "https://microsoft.com/devicelogin",
              "expires_in": 900,
              "interval": 5,
              "message": "Sign in"
            }
            """);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var challenge = await client.BeginDeviceCodeAsync();

        Assert.AreEqual("ABCD-EFGH", challenge.UserCode);
        Assert.AreEqual(TimeSpan.FromSeconds(5), challenge.PollingInterval);
        Assert.IsFalse(challenge.ToString().Contains("device-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DeviceCodePoll_MapsAuthorizationPendingWithoutThrowing()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            Assert.IsTrue(request.RequestUri?.AbsoluteUri.EndsWith("/oauth2/v2.0/token", StringComparison.Ordinal));
            return Json(HttpStatusCode.BadRequest, """
            { "error": "authorization_pending", "error_description": "waiting" }
            """);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var challenge = new MicrosoftDeviceCodeChallenge(
            "device-secret",
            "ABCD",
            new Uri("https://microsoft.com/devicelogin"),
            DateTimeOffset.UtcNow.AddMinutes(10),
            TimeSpan.FromSeconds(5));

        var result = await client.PollDeviceCodeAsync(challenge);

        Assert.AreEqual(MicrosoftDeviceCodePollStatus.Pending, result.Status);
        Assert.AreEqual("authorization_pending", result.ErrorCode);
    }

    [TestMethod]
    public async Task ExchangeForMinecraftSession_ChainsXboxXstsMinecraftAndProfile()
    {
        var handler = CreateSuccessfulMinecraftChainHandler();
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var microsoftToken = new MicrosoftOAuthToken(
            "ms-access",
            "ms-refresh",
            DateTimeOffset.UtcNow.AddHours(1));

        var result = await client.ExchangeForMinecraftSessionAsync(microsoftToken);

        Assert.AreEqual("Steve", result.Session.PlayerName);
        Assert.AreEqual("00112233445566778899aabbccddeeff", result.Session.PlayerUuid);
        Assert.AreEqual("mc-access", result.Session.AccessToken);
        Assert.AreEqual("2814630418365389", result.Session.Xuid);
        Assert.AreEqual(ClientId, result.Session.ClientId);
        Assert.IsFalse(result.Session.ToString().Contains("mc-access", StringComparison.Ordinal));
        Assert.AreEqual(4, handler.RequestCount);
    }

    [TestMethod]
    public async Task Provider_RefreshesRotatesCredentialAndReturnsUpdatedSession()
    {
        var chainHandler = CreateSuccessfulMinecraftChainHandler(includeMicrosoftRefresh: true);
        using var httpClient = new HttpClient(chainHandler);
        var secretStore = new MemorySecretStore();
        var client = CreateClient(httpClient);
        var provider = new MicrosoftMinecraftAccountProvider(client, secretStore);
        var account = new MinecraftAccountDescriptor(
            MicrosoftMinecraftAccountProvider.MicrosoftProviderId,
            "00112233445566778899aabbccddeeff",
            "OldName");
        await secretStore.WriteSecretAsync(
            new AccountSecretKey(
                MicrosoftMinecraftAccountProvider.MicrosoftProviderId,
                account.AccountId,
                "refresh_token"),
            "old-refresh");

        var refreshed = await provider.RefreshSessionAsync(account);

        Assert.AreEqual("Steve", refreshed.Account.DisplayName);
        Assert.AreEqual("mc-access", refreshed.Session.AccessToken);
        Assert.AreEqual("new-refresh", secretStore.LastWrittenSecret);
        Assert.IsFalse(refreshed.ToString().Contains("mc-access", StringComparison.Ordinal));
        Assert.IsFalse(refreshed.ToString().Contains("new-refresh", StringComparison.Ordinal));
        Assert.AreEqual(5, chainHandler.RequestCount);
    }

    [TestMethod]
    public async Task XstsFailure_ReportsXErrWithoutIncludingCredentialBodies()
    {
        var handler = new ScriptedHandler((request, call) =>
        {
            return call switch
            {
                1 => Json(HttpStatusCode.OK, XboxResponse("xbl-token", "user-hash")),
                2 => Json(HttpStatusCode.Unauthorized, """
                    { "XErr": 2148916238, "Message": "child account" }
                    """),
                _ => throw new AssertFailedException("Unexpected request after XSTS failure.")
            };
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var microsoftToken = new MicrosoftOAuthToken(
            "ms-secret-token",
            "refresh-secret",
            DateTimeOffset.UtcNow.AddHours(1));

        var exception = await Assert.ThrowsExactlyAsync<MicrosoftAuthenticationException>(() =>
            client.ExchangeForMinecraftSessionAsync(microsoftToken));

        Assert.AreEqual(MicrosoftAuthenticationStage.Xsts, exception.Stage);
        Assert.AreEqual("2148916238", exception.ErrorCode);
        Assert.IsFalse(exception.Message.Contains("ms-secret-token", StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains("xbl-token", StringComparison.Ordinal));
    }

    private static MicrosoftAuthenticationClient CreateClient(HttpClient httpClient) =>
        new(
            httpClient,
            new MicrosoftAuthenticationOptions(ClientId));

    private static ScriptedHandler CreateSuccessfulMinecraftChainHandler(bool includeMicrosoftRefresh = false)
    {
        return new ScriptedHandler((request, call) =>
        {
            var offset = includeMicrosoftRefresh ? 1 : 0;
            if (includeMicrosoftRefresh && call == 1)
            {
                Assert.IsTrue(request.RequestUri?.AbsoluteUri.EndsWith("/oauth2/v2.0/token", StringComparison.Ordinal));
                return Json(HttpStatusCode.OK, """
                {
                  "token_type": "Bearer",
                  "expires_in": 3600,
                  "scope": "XboxLive.signin offline_access",
                  "access_token": "ms-access",
                  "refresh_token": "new-refresh"
                }
                """);
            }

            return (call - offset) switch
            {
                1 => Json(HttpStatusCode.OK, XboxResponse("xbl-token", "user-hash")),
                2 => Json(HttpStatusCode.OK, XboxResponse(
                    "xsts-token",
                    "user-hash",
                    "2814630418365389")),
                3 => Json(HttpStatusCode.OK, """
                    {
                      "username": "service-user",
                      "roles": [],
                      "access_token": "mc-access",
                      "token_type": "Bearer",
                      "expires_in": 7200
                    }
                    """),
                4 => Json(HttpStatusCode.OK, """
                    {
                      "id": "00112233445566778899aabbccddeeff",
                      "name": "Steve",
                      "skins": [],
                      "capes": []
                    }
                    """),
                _ => throw new AssertFailedException($"Unexpected authentication request #{call}.")
            };
        });
    }

    private static string XboxResponse(string token, string userHash, string? xuid = null)
    {
        var claim = xuid is null
            ? $$"""{ "uhs": "{{userHash}}" }"""
            : $$"""{ "uhs": "{{userHash}}", "xid": "{{xuid}}" }""";
        return $$"""
        {
          "IssueInstant": "2026-08-21T00:00:00Z",
          "NotAfter": "2026-08-22T00:00:00Z",
          "Token": "{{token}}",
          "DisplayClaims": { "xui": [ {{claim}} ] }
        }
        """;
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _requestCount);
            var response = responder(request, call);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class MemorySecretStore : IAccountSecretStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public string? LastWrittenSecret { get; private set; }

        public Task<string?> ReadSecretAsync(
            AccountSecretKey key,
            CancellationToken cancellationToken = default)
        {
            _secrets.TryGetValue(ToKey(key), out var secret);
            return Task.FromResult(secret);
        }

        public Task WriteSecretAsync(
            AccountSecretKey key,
            string secret,
            CancellationToken cancellationToken = default)
        {
            _secrets[ToKey(key)] = secret;
            LastWrittenSecret = secret;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(
            AccountSecretKey key,
            CancellationToken cancellationToken = default)
        {
            _secrets.Remove(ToKey(key));
            return Task.CompletedTask;
        }

        private static string ToKey(AccountSecretKey key) =>
            $"{key.ProviderId}:{key.AccountId}:{key.Name}";
    }
}
