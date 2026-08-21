namespace PCL3.Minecraft.Accounts.Authentication;

public sealed class MinecraftAccountDescriptor
{
    public MinecraftAccountDescriptor(string providerId, string accountId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        ProviderId = providerId;
        AccountId = accountId;
        DisplayName = displayName;
    }

    public string ProviderId { get; }

    public string AccountId { get; }

    public string DisplayName { get; }

    public override string ToString() =>
        $"MinecraftAccountDescriptor(ProviderId={ProviderId}, AccountId={AccountId}, DisplayName={DisplayName})";
}

public sealed class MinecraftAccountSession
{
    public MinecraftAccountSession(
        MinecraftAccountDescriptor account,
        MinecraftSession session,
        DateTimeOffset? expiresAt = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(session);

        Account = account;
        Session = session;
        ExpiresAt = expiresAt;
    }

    public MinecraftAccountDescriptor Account { get; }

    public MinecraftSession Session { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public bool IsExpired(TimeProvider? timeProvider = null, TimeSpan? clockSkew = null)
    {
        if (ExpiresAt is null)
        {
            return false;
        }

        timeProvider ??= TimeProvider.System;
        var skew = clockSkew ?? TimeSpan.FromMinutes(2);
        if (skew < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(clockSkew));
        }

        return ExpiresAt <= timeProvider.GetUtcNow().Add(skew);
    }

    public override string ToString() =>
        $"MinecraftAccountSession(Account={Account}, Session={Session}, ExpiresAt={ExpiresAt:O})";
}

public sealed class AccountSecretKey
{
    public AccountSecretKey(string providerId, string accountId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ProviderId = providerId;
        AccountId = accountId;
        Name = name;
    }

    public string ProviderId { get; }

    public string AccountId { get; }

    public string Name { get; }

    public override string ToString() =>
        $"AccountSecretKey(ProviderId={ProviderId}, AccountId={AccountId}, Name={Name})";
}

/// <summary>
/// Boundary for platform-backed secure credential storage. Implementations should use
/// the operating system credential/keychain service and must not persist secrets as plaintext.
/// </summary>
public interface IAccountSecretStore
{
    Task<string?> ReadSecretAsync(
        AccountSecretKey key,
        CancellationToken cancellationToken = default);

    Task WriteSecretAsync(
        AccountSecretKey key,
        string secret,
        CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(
        AccountSecretKey key,
        CancellationToken cancellationToken = default);
}

public interface IMinecraftAccountProvider
{
    string ProviderId { get; }

    Task<MinecraftAccountSession> RefreshSessionAsync(
        MinecraftAccountDescriptor account,
        CancellationToken cancellationToken = default);

    Task RemoveAccountAsync(
        MinecraftAccountDescriptor account,
        CancellationToken cancellationToken = default);
}
