namespace PCL3.Minecraft.Accounts.Authentication.Microsoft;

public sealed class MicrosoftMinecraftAccountProvider : IMinecraftAccountProvider
{
    public const string MicrosoftProviderId = "microsoft";
    private const string RefreshTokenSecretName = "refresh_token";

    private readonly MicrosoftAuthenticationClient _client;
    private readonly IAccountSecretStore _secretStore;

    public MicrosoftMinecraftAccountProvider(
        MicrosoftAuthenticationClient client,
        IAccountSecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(secretStore);

        _client = client;
        _secretStore = secretStore;
    }

    public string ProviderId => MicrosoftProviderId;

    public Task<MicrosoftDeviceCodeChallenge> BeginDeviceCodeAsync(
        CancellationToken cancellationToken = default) =>
        _client.BeginDeviceCodeAsync(cancellationToken);

    public async Task<MinecraftAccountSession> CompleteDeviceCodeAsync(
        MicrosoftDeviceCodeChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var microsoftToken = await _client.WaitForDeviceCodeAsync(
            challenge,
            cancellationToken).ConfigureAwait(false);
        var minecraft = await _client.ExchangeForMinecraftSessionAsync(
            microsoftToken,
            cancellationToken).ConfigureAwait(false);
        var account = new MinecraftAccountDescriptor(
            ProviderId,
            minecraft.Session.PlayerUuid,
            minecraft.Session.PlayerName);

        await WriteRefreshTokenAsync(
            account,
            microsoftToken.RefreshToken,
            cancellationToken).ConfigureAwait(false);

        return new MinecraftAccountSession(
            account,
            minecraft.Session,
            minecraft.MinecraftAccessTokenExpiresAt);
    }

    public async Task<MinecraftAccountSession> RefreshSessionAsync(
        MinecraftAccountDescriptor account,
        CancellationToken cancellationToken = default)
    {
        ValidateAccount(account);
        var key = CreateRefreshTokenKey(account);
        string? refreshToken;
        try
        {
            refreshToken = await _secretStore.ReadSecretAsync(key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MicrosoftAuthenticationException(
                MicrosoftAuthenticationStage.CredentialStore,
                "Failed to read Microsoft refresh credentials from secure storage.",
                innerException: exception);
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new MicrosoftAuthenticationException(
                MicrosoftAuthenticationStage.CredentialStore,
                "No Microsoft refresh credential is stored for this account.",
                errorCode: "missing_refresh_token");
        }

        var microsoftToken = await _client.RefreshMicrosoftTokenAsync(
            refreshToken,
            cancellationToken).ConfigureAwait(false);

        // Refresh tokens may rotate. Persist the newest token before downstream Xbox/
        // Minecraft calls so a transient downstream failure does not discard it.
        await WriteRefreshTokenAsync(
            account,
            microsoftToken.RefreshToken,
            cancellationToken).ConfigureAwait(false);

        var minecraft = await _client.ExchangeForMinecraftSessionAsync(
            microsoftToken,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                minecraft.Session.PlayerUuid,
                account.AccountId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MicrosoftAuthenticationException(
                MicrosoftAuthenticationStage.MinecraftProfile,
                "Refreshed Microsoft credentials resolved to a different Minecraft profile.",
                errorCode: "profile_mismatch");
        }

        var refreshedAccount = new MinecraftAccountDescriptor(
            ProviderId,
            account.AccountId,
            minecraft.Session.PlayerName);
        return new MinecraftAccountSession(
            refreshedAccount,
            minecraft.Session,
            minecraft.MinecraftAccessTokenExpiresAt);
    }

    public async Task RemoveAccountAsync(
        MinecraftAccountDescriptor account,
        CancellationToken cancellationToken = default)
    {
        ValidateAccount(account);
        try
        {
            await _secretStore.DeleteSecretAsync(
                CreateRefreshTokenKey(account),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MicrosoftAuthenticationException(
                MicrosoftAuthenticationStage.CredentialStore,
                "Failed to remove Microsoft refresh credentials from secure storage.",
                innerException: exception);
        }
    }

    private async Task WriteRefreshTokenAsync(
        MinecraftAccountDescriptor account,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await _secretStore.WriteSecretAsync(
                CreateRefreshTokenKey(account),
                refreshToken,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new MicrosoftAuthenticationException(
                MicrosoftAuthenticationStage.CredentialStore,
                "Failed to write Microsoft refresh credentials to secure storage.",
                innerException: exception);
        }
    }

    private void ValidateAccount(MinecraftAccountDescriptor account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!string.Equals(account.ProviderId, ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Account provider '{account.ProviderId}' cannot be handled by '{ProviderId}'.",
                nameof(account));
        }
    }

    private static AccountSecretKey CreateRefreshTokenKey(MinecraftAccountDescriptor account) =>
        new(MicrosoftProviderId, account.AccountId, RefreshTokenSecretName);
}
