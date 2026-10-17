// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: the "connection message" the server owner hands to users. It carries the server address and
// public key in an opaque, authenticated (AES-256-GCM) text so users never see or retype the IP.
//
// The key below is a constant shared with the app (and therefore public, like this source). It keeps the
// address out of sight of ordinary users and makes the message tamper-evident; it is NOT a secret against
// someone who reverse-engineers the app or watches the network.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ferrite.Services.Zerogram;

public static class ZgConnectMessage
{
    public const string Prefix = "zg1.";
    private static readonly byte[] Key = SHA256.HashData("zerogram-connect-message-v1/shared-obfuscation-key"u8);
    private static readonly byte[] Aad = "zg-connect"u8.ToArray();

    public static string Seal(string serverName, string host, int port, byte[] publicKeyPkcs1)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1,
            n = serverName,
            h = host,
            p = port,
            k = Convert.ToBase64String(publicKeyPkcs1),
        });
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] output = new byte[12 + json.Length + 16];
        nonce.CopyTo(output, 0);
        using var gcm = new AesGcm(Key, 16);
        gcm.Encrypt(nonce, json, output.AsSpan(12, json.Length), output.AsSpan(12 + json.Length, 16), Aad);
        return Prefix + Base64Url(output);
    }

    public static bool TryOpen(string? message, out string serverName, out string host, out int port, out byte[] key)
    {
        serverName = host = ""; port = 0; key = [];
        try
        {
            string text = new string((message ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            byte[] raw = FromBase64Url(text[Prefix.Length..]);
            if (raw.Length < 12 + 16 + 2) return false;
            int length = raw.Length - 28;
            byte[] plain = new byte[length];
            using var gcm = new AesGcm(Key, 16);
            gcm.Decrypt(raw.AsSpan(0, 12), raw.AsSpan(12, length), raw.AsSpan(12 + length, 16), plain, Aad);
            using JsonDocument doc = JsonDocument.Parse(plain);
            JsonElement root = doc.RootElement;
            serverName = root.GetProperty("n").GetString() ?? "";
            host = root.GetProperty("h").GetString() ?? "";
            port = root.GetProperty("p").GetInt32();
            key = Convert.FromBase64String(root.GetProperty("k").GetString() ?? "");
            return host.Length > 0 && port is > 0 and < 65536 && key.Length > 0;
        }
        catch { return false; }
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
        return Convert.FromBase64String(b);
    }
}
