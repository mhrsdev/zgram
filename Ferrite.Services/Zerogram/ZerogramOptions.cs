// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: configuration for the authenticator-account and delivery-queue layers.

namespace Ferrite.Services.Zerogram;

public sealed record ZerogramOptions
{
    /// <summary>Directory holding zerogram.db and the master key. Defaults to the Ferrite data path.</summary>
    public string DataPath { get; init; } = "data";

    /// <summary>Label shown in Google Authenticator.</summary>
    public string Issuer { get; init; } = "Zerogram";

    /// <summary>Base64 AES-256 key that encrypts TOTP secrets. When null a key file is created under DataPath.</summary>
    public string? MasterKeyBase64 { get; init; }

    public int TotpPeriodSeconds { get; init; } = 30;
    public int TotpDigits { get; init; } = 6;

    /// <summary>Accepted clock drift in whole TOTP steps on either side of the current one.</summary>
    public int TotpWindowSteps { get; init; } = 1;

    public int RecoveryCodeCount { get; init; } = 10;

    /// <summary>How long a not-yet-activated registration reserves its username.</summary>
    public TimeSpan PendingRegistrationTtl { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Failed code attempts per username before a lockout starts.</summary>
    public int MaxFailedAttempts { get; init; } = 5;

    public TimeSpan LockoutBase { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan LockoutMax { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Hard cap on auth attempts (any outcome) per client IP inside <see cref="IpWindow"/>.</summary>
    public int MaxAttemptsPerIp { get; init; } = 300;

    public TimeSpan IpWindow { get; init; } = TimeSpan.FromMinutes(10);

    public int MaxRegistrationsPerIp { get; init; } = 30;
    public TimeSpan RegistrationWindow { get; init; } = TimeSpan.FromHours(1);

    public int GroupMemberLimit { get; init; } = 50;

    /// <summary>Password of the server owner (>= 12 characters). Owner sign-in is disabled while unset.</summary>
    public string? OwnerPassword { get; init; }

    /// <summary>Human-readable server name embedded in the connection message.</summary>
    public string ServerName { get; init; } = "Zerogram";

    /// <summary>Public key bootstrap port (HTTP, key + fingerprint only). 0 disables it.</summary>
    public int BootstrapPort { get; init; } = 5223;
    public string BootstrapBind { get; init; } = "0.0.0.0";

    /// <summary>Address clients should use in the connection message when it differs from Ferrite's own.</summary>
    public string? PublicHost { get; init; }
    public int PublicPort { get; init; }

    public int OwnerMaxFailuresPerIp { get; init; } = 10;
    public int OwnerMaxFailuresTotal { get; init; } = 100;
    public TimeSpan OwnerFailureWindow { get; init; } = TimeSpan.FromMinutes(10);
    public int GroupTokenMaxFailures { get; init; } = 10;

    /// <summary>Undelivered-message queue: expiry, per-recipient and global caps.</summary>
    public TimeSpan QueueTtl { get; init; } = TimeSpan.FromDays(7);
    public int QueueMaxMessagesPerRecipient { get; init; } = 10_000;
    public long QueueMaxBytesPerRecipient { get; init; } = 64L * 1024 * 1024;
    public long QueueMaxTotalBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public TimeSpan QueueSweepInterval { get; init; } = TimeSpan.FromMinutes(5);

    public static ZerogramOptions FromEnvironment(Func<string, string?> read, string dataPath)
    {
        static int Int(string? v, int d) => int.TryParse(v, out int x) && x > 0 ? x : d;
        static long Long(string? v, long d) => long.TryParse(v, out long x) && x > 0 ? x : d;
        var o = new ZerogramOptions { DataPath = dataPath };
        return o with
        {
            Issuer = read("ZEROGRAM_ISSUER") is { Length: > 0 } i ? i : o.Issuer,
            MasterKeyBase64 = read("ZEROGRAM_MASTER_KEY") is { Length: > 0 } k ? k : null,
            MaxFailedAttempts = Int(read("ZEROGRAM_MAX_FAILED_ATTEMPTS"), o.MaxFailedAttempts),
            // Accepted clock drift in TOTP steps (default 1). Larger values weaken the 30 s code lifetime.
            TotpWindowSteps = Int(read("ZEROGRAM_TOTP_WINDOW_STEPS"), o.TotpWindowSteps),
            LockoutBase = TimeSpan.FromSeconds(Int(read("ZEROGRAM_LOCKOUT_BASE_SECONDS"), (int)o.LockoutBase.TotalSeconds)),
            MaxAttemptsPerIp = Int(read("ZEROGRAM_MAX_ATTEMPTS_PER_IP"), o.MaxAttemptsPerIp),
            MaxRegistrationsPerIp = Int(read("ZEROGRAM_MAX_REGISTRATIONS_PER_IP"), o.MaxRegistrationsPerIp),
            GroupMemberLimit = Int(read("ZEROGRAM_GROUP_MEMBER_LIMIT"), o.GroupMemberLimit),
            OwnerPassword = read("ZEROGRAM_OWNER_PASSWORD") is { Length: > 0 } ownerPassword ? ownerPassword : null,
            ServerName = read("ZEROGRAM_SERVER_NAME") is { Length: > 0 } serverName ? serverName : o.ServerName,
            BootstrapPort = int.TryParse(read("ZEROGRAM_BOOTSTRAP_PORT"), out int bp) && bp >= 0
                ? bp
                : (int.TryParse(read("FERRITE_PORT"), out int fp) ? fp : 5222) + 1,
            BootstrapBind = read("ZEROGRAM_BOOTSTRAP_BIND") is { Length: > 0 } bb ? bb : o.BootstrapBind,
            PublicHost = read("ZEROGRAM_PUBLIC_HOST") is { Length: > 0 } ph ? ph : null,
            PublicPort = Int(read("ZEROGRAM_PUBLIC_PORT"), 0),
            QueueTtl = read("ZEROGRAM_QUEUE_TTL_SECONDS") is { Length: > 0 } ttlSeconds
                ? TimeSpan.FromSeconds(Int(ttlSeconds, (int)o.QueueTtl.TotalSeconds))
                : TimeSpan.FromHours(Int(read("ZEROGRAM_QUEUE_TTL_HOURS"), (int)o.QueueTtl.TotalHours)),
            QueueMaxMessagesPerRecipient = Int(read("ZEROGRAM_QUEUE_MAX_MESSAGES_PER_USER"), o.QueueMaxMessagesPerRecipient),
            QueueMaxBytesPerRecipient = Long(read("ZEROGRAM_QUEUE_MAX_BYTES_PER_USER"), o.QueueMaxBytesPerRecipient),
            QueueMaxTotalBytes = Long(read("ZEROGRAM_QUEUE_MAX_TOTAL_BYTES"), o.QueueMaxTotalBytes),
            QueueSweepInterval = TimeSpan.FromSeconds(Int(read("ZEROGRAM_QUEUE_SWEEP_SECONDS"), (int)o.QueueSweepInterval.TotalSeconds)),
        };
    }
}
