// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: AES-256-GCM envelope for TOTP secrets and a keyed hash for recovery codes.

using System.Security.Cryptography;
using System.Text;

namespace Ferrite.Services.Zerogram;

public sealed class SecretVault
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;
    private readonly byte[] _hashKey;
    private readonly byte[] _tokenKey;

    public SecretVault(ZerogramOptions options)
    {
        _key = LoadOrCreateKey(options);
        // Separate subkey so recovery-code hashes never share a key with encryption.
        _hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32,
            info: "zerogram/recovery-code-hash"u8.ToArray());
        _tokenKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, info: "zerogram/group-token-hash"u8.ToArray());
        _ownerSaltKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, info: "zerogram/owner-salt"u8.ToArray());
    }

    private readonly byte[] _ownerSaltKey;

    /// <summary>Lookup index for group tokens: the token itself is only stored sealed.</summary>
    public byte[] HashToken(string normalizedToken) =>
        HMACSHA256.HashData(_tokenKey, Encoding.UTF8.GetBytes(normalizedToken));

    /// <summary>Stable per-installation salt for the owner-password key derivation.</summary>
    public byte[] OwnerSalt() => HMACSHA256.HashData(_ownerSaltKey, "salt"u8.ToArray())[..16];

    private static byte[] LoadOrCreateKey(ZerogramOptions options)
    {
        if (!string.IsNullOrEmpty(options.MasterKeyBase64))
        {
            byte[] fromEnv = Convert.FromBase64String(options.MasterKeyBase64);
            if (fromEnv.Length != 32)
            {
                throw new ArgumentException("ZEROGRAM_MASTER_KEY must be 32 bytes, base64-encoded");
            }
            return fromEnv;
        }

        Directory.CreateDirectory(options.DataPath);
        string path = Path.Combine(options.DataPath, "zerogram-master.key");
        if (File.Exists(path))
        {
            byte[] stored = Convert.FromBase64String(File.ReadAllText(path).Trim());
            if (stored.Length != 32) throw new InvalidDataException($"{path} is not a 32-byte key");
            return stored;
        }

        byte[] created = RandomNumberGenerator.GetBytes(32);
        // CreateNew + owner-only mode: never overwrite an existing key, never world-readable.
        var fso = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        using (var stream = new FileStream(path, fso))
        {
            stream.Write(Encoding.ASCII.GetBytes(Convert.ToBase64String(created) + "\n"));
            stream.Flush(true);
        }
        return created;
    }

    /// <summary>nonce || ciphertext || tag, bound to the account name as associated data.</summary>
    public byte[] Seal(ReadOnlySpan<byte> plaintext, string associatedData)
    {
        byte[] output = new byte[NonceSize + plaintext.Length + TagSize];
        Span<byte> nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var gcm = new AesGcm(_key, TagSize);
        gcm.Encrypt(nonce, plaintext, output.AsSpan(NonceSize, plaintext.Length),
            output.AsSpan(NonceSize + plaintext.Length, TagSize),
            Encoding.UTF8.GetBytes(associatedData));
        return output;
    }

    public byte[] Open(ReadOnlySpan<byte> sealedData, string associatedData)
    {
        if (sealedData.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("sealed secret is truncated");
        }
        int length = sealedData.Length - NonceSize - TagSize;
        byte[] plaintext = new byte[length];
        using var gcm = new AesGcm(_key, TagSize);
        gcm.Decrypt(sealedData[..NonceSize], sealedData.Slice(NonceSize, length),
            sealedData.Slice(NonceSize + length, TagSize), plaintext,
            Encoding.UTF8.GetBytes(associatedData));
        return plaintext;
    }

    public byte[] HashRecoveryCode(string normalizedCode) =>
        HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(normalizedCode));
}
