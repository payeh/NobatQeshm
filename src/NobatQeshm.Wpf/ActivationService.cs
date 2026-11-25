using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NobatQeshm.Wpf;

internal sealed class ActivationService
{
    internal const int ActivationDays = 45;
    internal const int CodeLength = 5;
    private const int AllowedAttempts = 5;
    internal static IReadOnlyList<string> Destinations { get; } = Array.AsReadOnly(new[] { "09361263005", "09352543005" });
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    private static readonly byte[] LeasePurpose = Encoding.UTF8.GetBytes("NobatQeshm.ActivationLease.v1");

    private readonly string _leasePath;
    private readonly Func<string, string, CancellationToken, Task> _sendSms;
    private readonly TimeProvider _clock;
    private PendingCode? _pending;
    private DateTimeOffset? _nextSendAt;
    private bool _sending;

    internal ActivationService() : this(AppPaths.ActivationLease, ActivationSms.SendAsync, TimeProvider.System) { }

    internal ActivationService(string leasePath, Func<string, string, CancellationToken, Task> sendSms, TimeProvider clock)
    {
        _leasePath = leasePath;
        _sendSms = sendSms;
        _clock = clock;
    }

    internal DateTimeOffset? ActiveUntilUtc()
    {
        try
        {
            if (!File.Exists(_leasePath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_leasePath), LeasePurpose, DataProtectionScope.CurrentUser);
            var lease = JsonSerializer.Deserialize<ActivationLease>(plain);
            var now = _clock.GetUtcNow();
            return lease is not null && Destinations.Contains(lease.Phone) && lease.IssuedAt <= now
                && lease.ExpiresAt > now && lease.ExpiresAt == lease.IssuedAt.AddDays(ActivationDays)
                ? lease.ExpiresAt : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    internal TimeSpan ResendRemaining()
    {
        if (_nextSendAt is not { } nextSendAt) return TimeSpan.Zero;
        var remaining = nextSendAt - _clock.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    internal bool HasCodeFor(string? destination) =>
        _pending is { } pending && pending.Phone == destination && pending.ExpiresAt > _clock.GetUtcNow();

    internal async Task SendCodeAsync(string destination, CancellationToken cancellationToken)
    {
        if (!Destinations.Contains(destination))
            throw new InvalidOperationException("یکی از شماره‌های فهرست را انتخاب کنید.");
        if (_sending || ResendRemaining() > TimeSpan.Zero)
            throw new InvalidOperationException("تا پایان زمان ارسال مجدد صبر کنید.");

        _sending = true;
        try
        {
            var code = RandomNumberGenerator.GetInt32(100_000).ToString("D5", System.Globalization.CultureInfo.InvariantCulture);
            await _sendSms(destination, code, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var now = _clock.GetUtcNow();
            _nextSendAt = now + ResendInterval;
            _pending = new PendingCode(destination, Hash(code), now + CodeLifetime, AllowedAttempts);
        }
        finally { _sending = false; }
    }

    internal bool TryActivate(string? destination, string code)
    {
        if (_pending is not { } pending || !HasCodeFor(destination)) return false;
        var digits = JsonHelpers.NormalizeDigits(code);
        if (digits.Length != CodeLength || !digits.All(char.IsAsciiDigit)) return false;
        if (!CryptographicOperations.FixedTimeEquals(pending.Hash, Hash(digits)))
        {
            _pending = pending.AttemptsRemaining > 1 ? pending with { AttemptsRemaining = pending.AttemptsRemaining - 1 } : null;
            return false;
        }

        var issuedAt = _clock.GetUtcNow();
        var lease = new ActivationLease(pending.Phone, issuedAt, issuedAt.AddDays(ActivationDays));
        SaveLease(lease);
        _pending = null;
        return true;
    }

    private void SaveLease(ActivationLease lease)
    {
        var encoded = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(lease), LeasePurpose, DataProtectionScope.CurrentUser);
        var temporaryPath = _leasePath + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, encoded);
            File.Move(temporaryPath, _leasePath, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private sealed record ActivationLease(string Phone, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
    private sealed record PendingCode(string Phone, byte[] Hash, DateTimeOffset ExpiresAt, int AttemptsRemaining);
}
