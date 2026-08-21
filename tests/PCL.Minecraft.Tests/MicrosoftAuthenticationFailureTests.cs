using System.Net;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Accounts.Authentication;
using PCL3.Minecraft.Accounts.Authentication.Microsoft;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MicrosoftAuthenticationFailureTests
{
    private const string ClientId = "11111111-2222-3333-4444-555555555555";

    [TestMethod]
    public async Task DeviceCode_RejectsUnexpectedFinalEndpoint()
    {
        var handler = new DelegateHandler((request, _) =>
        {
            var response = Json(HttpStatusCode.OK, """
            {
              "device_code": "device-secret",
              "user_code": "ABCD",
              "verification_uri": "https://microsoft.com/devicelogin",
              "expires_in": 900,
              "interval": 5
            }
            """);
            response.RequestMessage = new HttpRequestMessage(
                request.Method,
                "https://unexpected.example/oauth2/v2.0/devicecode");
            return response;
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<MicrosoftAuthenticationException>(() =>
            client.BeginDeviceCodeAsync());

        Assert.AreEqual(MicrosoftAuthenticationStage.DeviceCode, exception.Stage);
        Assert.AreEqual("unexpected_endpoint", exception.ErrorCode);
        Assert.IsFalse(exception.Message.Contains("device-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DeviceCode_MalformedJsonPreservesDeviceCodeStage()
    {
        var handler = new DelegateHandler((request, _) =>
        {
            var response = Json(HttpStatusCode.OK, "{ not-json");
            response.RequestMessage = request;
            return response;
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsExactlyAsync<MicrosoftAuthenticationException>(() =>
            client.BeginDeviceCodeAsync());

        Assert.AreEqual(MicrosoftAuthenticationStage.DeviceCode, exception.Stage);
        Assert.AreEqual("malformed_json", exception.ErrorCode);
    }

    [TestMethod]
    public async Task Provider_SecretStoreReadFailureIsWrappedBeforeNetworkAccess()
    {
        var handler = new DelegateHandler((_, _) =>
            throw new AssertFailedException("Network must not be used when credential read fails."));
        using var httpClient = new HttpClient(handler);
        var provider = new MicrosoftMinecraftAccountProvider(
            CreateClient(httpClient),
            new ThrowingSecretStore(throwOnRead: true));
        var account = CreateAccount();

        var exception = await Assert.ThrowsExactlyAsync<MicrosoftAuthenticationException>(() =>
            provider.RefreshSessionAsync(account));

        Assert.AreEqual(MicrosoftAuthenticationStage.CredentialStore, exception.Stage);
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task Provider_SecretStoreWriteFailureStopsBeforeXboxAndRedactsTokens()
    {
        var handler = new DelegateHandler((request, call) =>
        {
            Assert.AreEqual(1, call);
            Assert.IsTrue(request.RequestUri?.AbsoluteUri.EndsWith(
                "/oauth2/v2.0/token",
                StringComparison.Ordinal));
            return Json(HttpStatusCode.OK, """
            {
              "token_type": "Bearer",
              "expires_in": 3600,
              "scope": "XboxLive.signin offline_access",
              "access_token": "new-ms-access-secret",
              "refresh_token": "new-refresh-secret"
            }
            """);
        });
        using var httpClient = new HttpClient(handler);
        var provider = new MicrosoftMinecraftAccountProvider(
            CreateClient(httpClient),
            new ThrowingSecretStore(
                readValue: "old-refresh-secret",
                throwOnWrite: true));

        var exception = await Assert.ThrowsExactlyAsync<MicrosoftAuthenticationException>(() =>
            provider.RefreshSessionAsync(CreateAccount()));

        Assert.AreEqual(MicrosoftAuthenticationStage.CredentialStore, exception.Stage);
        Assert.AreEqual(1, handler.RequestCount);
        Assert.IsFalse(exception.Message.Contains("old-refresh-secret", StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains("new-refresh-secret", StringComparison.Ordinal));
        Assert.IsFalse(exception.Message.Contains("new-ms-access-secret", StringComparison.Ordinal));
    }

    private static MicrosoftAuthenticationClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new MicrosoftAuthenticationOptions(ClientId));

    private static MinecraftAccountDescriptor CreateAccount() =>
        new(
            MicrosoftMinecraftAccountProvider.MicrosoftProviderId,
            "00112233445566778899aabbccddeeff",
            "Steve");

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class DelegateHandler(
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
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingSecretStore(
        string? readValue = null,
        bool throwOnRead = false,
        bool throwOnWrite = false) : IAccountSecretStore
    {
        public Task<string?> ReadSecretAsync(
            AccountSecretKey key,
            CancellationToken cancellationToken = default)
        {
            if (throwOnRead)
            {
                throw new IOException("synthetic secret-store read failure");
            }

            return Task.FromResult(readValue);
        }

        public Task WriteSecretAsync(
            AccountSecretKey key,
            string secret,
            CancellationToken cancellationToken = default)
        {
            if (throwOnWrite)
            {
                throw new IOException("synthetic secret-store write failure");
            }

            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(
            AccountSecretKey key,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
