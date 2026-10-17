// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: server-owner sign-in.
//
// The owner is identified by a 12-word phrase (BIP-39 words) created once with --owner-init. The server keeps
// only the key derived from it, and the phrase never travels over the network. Sign-in is a mutual HMAC proof
// keyed by that derived key K:
//   server -> client: server_proof = HMAC(K, "zg-owner-srv" | client_nonce | server_nonce | key_fingerprint)
//   client -> server: client_proof = HMAC(K, "zg-owner-cli" | client_nonce | server_nonce | key_fingerprint)
// Because the server's RSA key fingerprint is part of both proofs, an impostor that substituted its own key
// cannot produce a valid server proof, so the app learns that it talks to the real server. While the app stays
// installed the owner session persists and the phrase is not asked for again; a new phone (or a reinstall)
// needs the 12 words. The owner then
// signs in as the reserved "owner" account (a normal Ferrite user that also holds the admin privileges).

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Ferrite.Crypto;
using Ferrite.Services.Common;
using Ferrite.Utils;
using TLAuthorization = Ferrite.TL.baseLayer.auth.TLAuthorization;

namespace Ferrite.Services.Zerogram;

public sealed record ZgOwnerChallenge(byte[] ServerNonce, byte[] Salt, int Iterations, byte[] ServerProof);

public sealed class ZgOwnerService
{
    // The owner key comes from a 128-bit phrase, so no slow KDF is needed on the wire: salt/iterations in the
    // challenge are reserved (empty / 0) and ignored by clients.
    private const int NonceSize = 16;

    private readonly ZerogramOptions _options;
    private readonly ZgStore _store;
    private readonly SecretVault _vault;
    private readonly ZgAccountService _accounts;
    private readonly IKeyProvider _keys;
    private IRSAKey _rsa => _keys.GetKey(_keys.GetRSAFingerprints()[0])!;
    private readonly IDataCenter _dataCenter;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly byte[]? _key;
    private readonly ConcurrentDictionary<long, (byte[] Client, byte[] Server, long Expires)> _pending = new();

    public ZgOwnerService(ZerogramOptions options, ZgStore store, SecretVault vault, ZgAccountService accounts,
        IKeyProvider keys, IDataCenter dataCenter, TimeProvider time, ILogger log)
    {
        _options = options;
        _store = store;
        _vault = vault;
        _accounts = accounts;
        _keys = keys;
        _dataCenter = dataCenter;
        _time = time;
        _log = log;
        _key = ZgOwnerKeyFile.Load(options.DataPath, options.OwnerKeyHex);
        if (_key is null)
            _log.Information("zerogram: owner sign-in is disabled (run the --owner-init command to create the 12-word owner identity)");
    }

    public bool Enabled => _key is not null;

    private long Now => _time.GetUtcNow().ToUnixTimeSeconds();

    private byte[] Proof(string label, byte[] clientNonce, byte[] serverNonce)
    {
        byte[] data = new byte[label.Length + clientNonce.Length + serverNonce.Length + 8];
        int o = Encoding.ASCII.GetBytes(label, data);
        clientNonce.CopyTo(data, o); o += clientNonce.Length;
        serverNonce.CopyTo(data, o); o += serverNonce.Length;
        BitConverter.TryWriteBytes(data.AsSpan(o), _rsa.Fingerprint);
        return HMACSHA256.HashData(_key!, data);
    }

    public ZgResult<ZgOwnerChallenge> Hello(long authKeyId, string ip, byte[] clientNonce)
    {
        if (!Enabled) return ZgResult<ZgOwnerChallenge>.Fail(ZgStatus.OwnerDisabled);
        if (clientNonce.Length != NonceSize) return ZgResult<ZgOwnerChallenge>.Fail(ZgStatus.OwnerInvalid);
        long now = Now;
        if (_pending.Count > 4096)
            foreach (var stale in _pending.Where(p => p.Value.Expires < now).Select(p => p.Key).ToList())
                _pending.TryRemove(stale, out _);
        byte[] serverNonce = RandomNumberGenerator.GetBytes(NonceSize);
        _pending[authKeyId] = (clientNonce, serverNonce, now + 60);
        return ZgResult<ZgOwnerChallenge>.Success(new ZgOwnerChallenge(serverNonce, [], 0,
            Proof("zg-owner-srv", clientNonce, serverNonce)));
    }

    public async ValueTask<ZgResult<TLAuthorization>> LoginAsync(long authKeyId, string ip, byte[] clientNonce,
        byte[] serverNonce, byte[] clientProof)
    {
        if (!Enabled) return ZgResult<TLAuthorization>.Fail(ZgStatus.OwnerDisabled);
        long now = Now;
        long since = now - (long)_options.OwnerFailureWindow.TotalSeconds;
        if (_store.CountEvents("owner-fail", ip, since) >= _options.OwnerMaxFailuresPerIp ||
            _store.CountEvents("owner-fail", "*", since) >= _options.OwnerMaxFailuresTotal)
        {
            long oldest = _store.OldestEvent("owner-fail", ip, since) ?? now;
            return ZgResult<TLAuthorization>.Flooded((int)Math.Max(1, oldest + (long)_options.OwnerFailureWindow.TotalSeconds - now));
        }

        bool good = false;
        // A challenge can be answered once: it is consumed whatever the outcome.
        if (_pending.TryRemove(authKeyId, out var challenge) && challenge.Expires >= now &&
            CryptographicOperations.FixedTimeEquals(challenge.Client, clientNonce) &&
            CryptographicOperations.FixedTimeEquals(challenge.Server, serverNonce))
        {
            good = CryptographicOperations.FixedTimeEquals(clientProof, Proof("zg-owner-cli", clientNonce, serverNonce));
        }
        if (!good)
        {
            _store.RecordEvent("owner-fail", ip, now);
            _store.RecordEvent("owner-fail", "*", now);
            _log.Warning("zerogram: owner sign-in failed");
            return ZgResult<TLAuthorization>.Fail(ZgStatus.OwnerInvalid);
        }
        return await _accounts.OwnerSignInAsync(authKeyId);
    }

    public bool IsOwner(long userId) => _accounts.OwnerUserId() == userId;

    public byte[] PublicKeyPkcs1() => _rsa.Key!.ExportRSAPublicKey();

    public long Fingerprint => _rsa.Fingerprint;

    public string BuildConnectMessage() =>
        ZgConnectMessage.Seal(_options.ServerName, _options.PublicHost ?? _dataCenter.IpAddress,
            _options.PublicPort > 0 ? _options.PublicPort : _dataCenter.Port, PublicKeyPkcs1());
}
