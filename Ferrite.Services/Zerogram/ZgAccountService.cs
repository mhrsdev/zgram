// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: username + authenticator (TOTP) accounts.
//
// There is no fixed password, SMS code or third-party identity provider. The e-mail address is stored
// for the operator's reference only: it is unverified and is never used for recovery or login.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Ferrite.Crypto;
using Ferrite.Data.Counters;
using Ferrite.Data.Repositories;
using Ferrite.Data.Search;
using Ferrite.Services.Account;
using Ferrite.Services.Auth;
using Ferrite.Services.SecretChats;
using Ferrite.TL;
using Ferrite.TL.baseLayer;
using Ferrite.TL.baseLayer.dto;
using Ferrite.Utils;
using OtpNet;
using TLAuthorization = Ferrite.TL.baseLayer.auth.TLAuthorization;

namespace Ferrite.Services.Zerogram;

public enum ZgStatus
{
    Ok,
    UsernameInvalid,
    UsernameOccupied,
    DisplayNameInvalid,
    EmailInvalid,
    CodeInvalid,
    Flood,
    Internal,
}

public sealed record ZgResult<T>(ZgStatus Status, T? Value = default, int FloodSeconds = 0)
{
    public bool Ok => Status == ZgStatus.Ok;
    public static ZgResult<T> Success(T value) => new(ZgStatus.Ok, value);
    public static ZgResult<T> Fail(ZgStatus status) => new(status);
    public static ZgResult<T> Flooded(int seconds) => new(ZgStatus.Flood, default, Math.Max(1, seconds));
}

public sealed record ZgRegistration(string Issuer, string Username, string DisplayName,
    string OtpAuthUri, string ManualKey, int Period, int Digits);

public sealed record ZgActivation(IReadOnlyList<string> RecoveryCodes, TLAuthorization Authorization);

public sealed class ZgAccountService
{
    private static readonly Regex UsernameShape = new("^[a-z][a-z0-9_]{4,31}$", RegexOptions.Compiled);
    private static readonly Regex EmailShape = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "admin", "administrator", "root", "support", "telegram", "zerogram", "system", "service",
        "moderator", "security", "official", "notifications",
    };

    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I

    private readonly ZerogramOptions _options;
    private readonly ZgStore _store;
    private readonly SecretVault _vault;
    private readonly TimeProvider _time;
    private readonly IRandomGenerator _random;
    private readonly IUserRepository _users;
    private readonly ISearchEngine _search;
    private readonly IAtomicCounter _userIds;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthorizationCompletion _completion;
    private readonly IAuthorizationRepository _authorizations;
    private readonly ISecretChatAuthKeyCleanup _secretChatCleanup;
    private readonly ProfileStore? _profiles;
    private readonly ILogger _log;

    public ZgAccountService(ZerogramOptions options, ZgStore store, SecretVault vault, TimeProvider time,
        IRandomGenerator random, IUserRepository users, ISearchEngine search, ICounterFactory counters,
        IUnitOfWork unitOfWork, IAuthorizationCompletion completion, IAuthorizationRepository authorizations,
        ISecretChatAuthKeyCleanup secretChatCleanup, ILogger log, ProfileStore? profiles = null)
    {
        _options = options;
        _store = store;
        _vault = vault;
        _time = time;
        _random = random;
        _users = users;
        _search = search;
        _userIds = counters.GetCounter("counter_user_id");
        _unitOfWork = unitOfWork;
        _completion = completion;
        _authorizations = authorizations;
        _secretChatCleanup = secretChatCleanup;
        _profiles = profiles;
        _log = log;
    }

    private long Now => _time.GetUtcNow().ToUnixTimeSeconds();

    public ZerogramOptions Options => _options;

    // ------------------------------------------------------------------ registration

    public async ValueTask<ZgResult<ZgRegistration>> RegisterAsync(long authKeyId, string ip,
        string usernameRaw, string displayNameRaw, string emailRaw)
    {
        string username = NormalizeUsername(usernameRaw);
        if (!UsernameShape.IsMatch(username) || Reserved.Contains(username))
            return ZgResult<ZgRegistration>.Fail(ZgStatus.UsernameInvalid);
        if (!TryNormalizeDisplayName(displayNameRaw, out string displayName))
            return ZgResult<ZgRegistration>.Fail(ZgStatus.DisplayNameInvalid);
        string email = (emailRaw ?? "").Trim().ToLowerInvariant();
        if (email.Length > 254 || !EmailShape.IsMatch(email))
            return ZgResult<ZgRegistration>.Fail(ZgStatus.EmailInvalid);

        long now = Now;
        if (CheckLimit("ip-reg", ip, _options.MaxRegistrationsPerIp, _options.RegistrationWindow, now) is { } wait)
            return ZgResult<ZgRegistration>.Flooded(wait);
        _store.RecordEvent("ip-reg", ip, now);

        // A Ferrite user (e.g. created before Zerogram policy was enabled) may already own the name.
        if (_users.GetUserByUsername(username) is { } existing)
        {
            existing.Dispose();
            return ZgResult<ZgRegistration>.Fail(ZgStatus.UsernameOccupied);
        }

        byte[] secret = KeyGeneration.GenerateRandomKey(20);
        byte[] sealedSecret = _vault.Seal(secret, username);
        long expires = now + (long)_options.PendingRegistrationTtl.TotalSeconds;
        if (!_store.TryReserve(username, displayName, email, sealedSecret, authKeyId, now, expires))
            return ZgResult<ZgRegistration>.Fail(ZgStatus.UsernameOccupied);

        return ZgResult<ZgRegistration>.Success(BuildRegistration(username, displayName, secret));
    }

    private ZgRegistration BuildRegistration(string username, string displayName, byte[] secret)
    {
        string uri = new OtpUri(OtpType.Totp, secret, username, _options.Issuer, OtpHashMode.Sha1,
            _options.TotpDigits, _options.TotpPeriodSeconds).ToString();
        return new ZgRegistration(_options.Issuer, username, displayName, uri,
            Base32Encoding.ToString(secret), _options.TotpPeriodSeconds, _options.TotpDigits);
    }

    // ------------------------------------------------------------------ activation (first code)

    public async ValueTask<ZgResult<ZgActivation>> ActivateAsync(long authKeyId, string ip, string usernameRaw,
        string code)
    {
        string username = NormalizeUsername(usernameRaw);
        long now = Now;
        if (CheckLimit("ip-auth", ip, _options.MaxAttemptsPerIp, _options.IpWindow, now) is { } wait)
            return ZgResult<ZgActivation>.Flooded(wait);
        _store.RecordEvent("ip-auth", ip, now);

        ZgAccount? account = _store.Get(username);
        if (account is null || account.State == ZgStore.Active ||
            (account.State == ZgStore.Pending && account.PendingExpires <= now))
        {
            BurnTime();
            return ZgResult<ZgActivation>.Fail(ZgStatus.CodeInvalid);
        }
        if (account.State == ZgStore.Pending && account.PendingAuthKey != authKeyId)
        {
            // Only the connection that registered may finish the registration.
            BurnTime();
            return ZgResult<ZgActivation>.Fail(ZgStatus.CodeInvalid);
        }
        if (account.LockedUntil > now) return ZgResult<ZgActivation>.Flooded((int)(account.LockedUntil - now));

        if (!VerifyAndConsume(account, code, now))
            return ZgResult<ZgActivation>.Fail(ZgStatus.CodeInvalid);

        try
        {
            long userId = account.UserId ?? await CreateFerriteUserAsync(account);
            var codes = GenerateRecoveryCodes();
            _store.Activate(username, userId, now, codes.Select(c => _vault.HashRecoveryCode(c)).ToList());
            await EnsureProfileAsync(userId, username, account.DisplayName);
            TLAuthorization? auth = await AuthorizeAsync(authKeyId, userId);
            if (auth is not { } authorization) return ZgResult<ZgActivation>.Fail(ZgStatus.Internal);
            await RevokeDevicesAsync(userId, keepAuthKeyId: authKeyId);
            _log.Information($"zerogram: account activated user={userId}");
            return ZgResult<ZgActivation>.Success(new ZgActivation(
                codes.Select(FormatRecoveryCode).ToList(), authorization));
        }
        catch (Exception e)
        {
            _log.Error(e, "zerogram: activation failed");
            return ZgResult<ZgActivation>.Fail(ZgStatus.Internal);
        }
    }

    // ------------------------------------------------------------------ login

    public async ValueTask<ZgResult<TLAuthorization>> LoginAsync(long authKeyId, string ip, string usernameRaw,
        string code)
    {
        string username = NormalizeUsername(usernameRaw);
        long now = Now;
        if (CheckLimit("ip-auth", ip, _options.MaxAttemptsPerIp, _options.IpWindow, now) is { } wait)
            return ZgResult<TLAuthorization>.Flooded(wait);
        _store.RecordEvent("ip-auth", ip, now);

        ZgAccount? account = _store.Get(username);
        if (account is null || account.State != ZgStore.Active || account.UserId is null)
        {
            // Unknown, inactive and wrong-code answers are identical (no username probing).
            BurnTime();
            return ZgResult<TLAuthorization>.Fail(ZgStatus.CodeInvalid);
        }
        if (account.LockedUntil > now) return ZgResult<TLAuthorization>.Flooded((int)(account.LockedUntil - now));
        if (!VerifyAndConsume(account, code, now))
            return ZgResult<TLAuthorization>.Fail(ZgStatus.CodeInvalid);

        try
        {
            long userId = account.UserId.Value;
            await EnsureProfileAsync(userId, username, account.DisplayName);
            TLAuthorization? auth = await AuthorizeAsync(authKeyId, userId);
            if (auth is not { } authorization) return ZgResult<TLAuthorization>.Fail(ZgStatus.Internal);
            // One active device per account: the newest login wins, every other session is revoked.
            await RevokeDevicesAsync(userId, keepAuthKeyId: authKeyId);
            return ZgResult<TLAuthorization>.Success(authorization);
        }
        catch (Exception e)
        {
            _log.Error(e, "zerogram: login failed");
            return ZgResult<TLAuthorization>.Fail(ZgStatus.Internal);
        }
    }

    // ------------------------------------------------------------------ recovery

    public async ValueTask<ZgResult<ZgRegistration>> RecoverAsync(long authKeyId, string ip, string usernameRaw,
        string recoveryCode)
    {
        string username = NormalizeUsername(usernameRaw);
        long now = Now;
        if (CheckLimit("ip-auth", ip, _options.MaxAttemptsPerIp, _options.IpWindow, now) is { } wait)
            return ZgResult<ZgRegistration>.Flooded(wait);
        _store.RecordEvent("ip-auth", ip, now);

        ZgAccount? account = _store.Get(username);
        if (account is null || account.UserId is null || account.State == ZgStore.Pending)
        {
            BurnTime();
            return ZgResult<ZgRegistration>.Fail(ZgStatus.CodeInvalid);
        }
        if (account.LockedUntil > now) return ZgResult<ZgRegistration>.Flooded((int)(account.LockedUntil - now));

        byte[] supplied = _vault.HashRecoveryCode(NormalizeRecoveryCode(recoveryCode));
        byte[]? matched = null;
        foreach (byte[] stored in _store.UnusedRecoveryHashes(username))
        {
            // Evaluate every candidate: no early exit that leaks which position matched.
            if (CryptographicOperations.FixedTimeEquals(stored, supplied)) matched = stored;
        }
        if (matched is null)
        {
            RecordFailure(username, now);
            return ZgResult<ZgRegistration>.Fail(ZgStatus.CodeInvalid);
        }

        byte[] secret = KeyGeneration.GenerateRandomKey(20);
        if (!_store.ConsumeRecoveryAndRotate(username, matched, _vault.Seal(secret, username), now))
        {
            // Lost a race with another use of the same code.
            RecordFailure(username, now);
            return ZgResult<ZgRegistration>.Fail(ZgStatus.CodeInvalid);
        }

        // The old TOTP secret is gone and every existing session is revoked, including the caller's.
        await RevokeDevicesAsync(account.UserId.Value, keepAuthKeyId: null);
        _log.Information($"zerogram: recovery used user={account.UserId}");
        return ZgResult<ZgRegistration>.Success(BuildRegistration(username, account.DisplayName, secret));
    }

    // ------------------------------------------------------------------ helpers

    private bool VerifyAndConsume(ZgAccount account, string codeRaw, long now)
    {
        string code = new string((codeRaw ?? "").Where(char.IsAsciiDigit).ToArray());
        byte[] secret;
        try { secret = _vault.Open(account.SecretSealed, account.Username); }
        catch (CryptographicException e)
        {
            _log.Error(e, $"zerogram: cannot open secret for {account.Username} (wrong master key?)");
            return false;
        }

        var totp = new Totp(secret, _options.TotpPeriodSeconds, OtpHashMode.Sha1, _options.TotpDigits);
        var window = new VerificationWindow(_options.TotpWindowSteps, _options.TotpWindowSteps);
        long step = 0;
        bool good = code.Length == _options.TotpDigits &&
                    totp.VerifyTotp(_time.GetUtcNow().UtcDateTime, code, out step, window);
        CryptographicOperations.ZeroMemory(secret);
        if (!good)
        {
            RecordFailure(account.Username, now);
            return false;
        }
        // Atomic: a code (or any earlier one) can be spent once, even with concurrent requests.
        if (!_store.TryConsumeStep(account.Username, step))
        {
            RecordFailure(account.Username, now);
            return false;
        }
        _store.ClearFailures(account.Username);
        return true;
    }

    private void RecordFailure(string username, long now) =>
        _store.RecordFailure(username, now, _options.MaxFailedAttempts,
            (long)_options.LockoutBase.TotalSeconds, (long)_options.LockoutMax.TotalSeconds);

    private int? CheckLimit(string scope, string key, int max, TimeSpan window, long now)
    {
        long since = now - (long)window.TotalSeconds;
        if (_store.CountEvents(scope, key, since) < max) return null;
        long oldest = _store.OldestEvent(scope, key, since) ?? now;
        return (int)Math.Max(1, oldest + (long)window.TotalSeconds - now);
    }

    /// <summary>Keeps the unknown-user path roughly as slow as the real one.</summary>
    private void BurnTime()
    {
        var dummy = new Totp(new byte[20], _options.TotpPeriodSeconds, OtpHashMode.Sha1, _options.TotpDigits);
        dummy.VerifyTotp(_time.GetUtcNow().UtcDateTime, "000000", out _,
            new VerificationWindow(_options.TotpWindowSteps, _options.TotpWindowSteps));
    }

    private async ValueTask<long> CreateFerriteUserAsync(ZgAccount account)
    {
        long userId = await _userIds.IncrementAndGet();
        if (userId == 0) userId = await _userIds.IncrementAndGet();
        PutNewUser(userId, account.DisplayName);
        if (!await _unitOfWork.SaveAsync()) throw new IOException("could not persist user");
        return userId;
    }

    private void PutNewUser(long userId, string displayName)
    {
        using var photo = UserProfilePhotoEmpty.Builder().Build();
        using TLUser user = User.Builder()
            .Id(userId)
            .Phone(Encoding.UTF8.GetBytes(SyntheticPhone(userId)))
            .FirstName(Encoding.UTF8.GetBytes(displayName))
            .LastName(Encoding.UTF8.GetBytes(""))
            .AccessHash(_random.NextLong())
            .Photo(photo.TLBytes!.Value.AsSpan())
            .Build();
        _users.PutUser(user);
    }

    /// <summary>Ferrite keys sessions by phone. Accounts have none, so each gets an internal, unique one.</summary>
    public static string SyntheticPhone(long userId) => "999" + userId.ToString("D9");

    private async ValueTask EnsureProfileAsync(long userId, string username, string displayName)
    {
        using TLUser? current = _users.GetUser(userId);
        if (current is null) throw new InvalidOperationException("user missing");
        string currentName = Encoding.UTF8.GetString(current.Value.AsUser().Username);
        if (!string.Equals(currentName, username, StringComparison.Ordinal))
        {
            _users.UpdateUsername(userId, username);
            if (!await _unitOfWork.SaveAsync()) throw new IOException("could not persist username");
            if (_profiles is not null) await _profiles.SyncPrimaryUsernameAsync(userId, username);
        }
        await _search.IndexUser(new UserSearchModel(userId, username, displayName, "", SyntheticPhone(userId)));
    }

    private async ValueTask<TLAuthorization?> AuthorizeAsync(long authKeyId, long userId)
    {
        if (!await _completion.CreateOrUpdatePendingAsync(authKeyId, userId, SyntheticPhone(userId)))
            return null;
        return await _completion.CompleteAsync(authKeyId);
    }

    private async ValueTask RevokeDevicesAsync(long userId, long? keepAuthKeyId)
    {
        string phone = SyntheticPhone(userId);
        var all = await _authorizations.GetAuthorizationsAsync(phone);
        var revoke = new List<long>();
        foreach (TLAuthInfo info in all)
        {
            long id = info.AsAuthInfo().AuthKeyId;
            if (keepAuthKeyId is { } keep && id == keep) continue;
            revoke.Add(id);
        }
        foreach (long id in revoke)
        {
            foreach (long imported in await _authorizations.GetAuthKeyIdsImportedFromAsync(phone, id))
            {
                await _secretChatCleanup.CleanupAsync(imported);
                _authorizations.DeleteAuthorization(imported);
            }
            await _secretChatCleanup.CleanupAsync(id);
            _authorizations.DeleteAuthorization(id);
        }
        if (revoke.Count > 0) await _unitOfWork.SaveAsync();
    }

    // ---- recovery-code formatting

    private List<string> GenerateRecoveryCodes()
    {
        var codes = new List<string>(_options.RecoveryCodeCount);
        while (codes.Count < _options.RecoveryCodeCount)
        {
            var chars = new char[20];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
            string c = new string(chars);
            if (!codes.Contains(c)) codes.Add(c);
        }
        return codes;
    }

    private static string FormatRecoveryCode(string c) =>
        $"{c[..5]}-{c[5..10]}-{c[10..15]}-{c[15..]}";

    private static string NormalizeRecoveryCode(string? c) =>
        new string((c ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string NormalizeUsername(string? u) => (u ?? "").Trim().TrimStart('@').ToLowerInvariant();

    private static bool TryNormalizeDisplayName(string? raw, out string name)
    {
        name = (raw ?? "").Trim();
        if (name.Length is < 1 or > 64) return false;
        foreach (char ch in name)
        {
            var cat = char.GetUnicodeCategory(ch);
            bool bidi = ch is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');
            bool control = cat is System.Globalization.UnicodeCategory.Control
                or System.Globalization.UnicodeCategory.PrivateUse
                or System.Globalization.UnicodeCategory.OtherNotAssigned;
            // Format characters are refused except ZWNJ/ZWJ, which Persian text needs.
            bool format = cat == System.Globalization.UnicodeCategory.Format && ch != '\u200C' && ch != '\u200D';
            if (bidi || control || format) return false;
        }
        return true;
    }
}
