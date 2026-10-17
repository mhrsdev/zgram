// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: BIP-39 style 12-word phrases for the server-owner identity. A phrase is 128 bits of random entropy
// plus a 4-bit checksum, written as 12 words of the standard English list, so it can be copied by hand and typos
// are caught. The owner key used by the sign-in proof is derived from the phrase and never leaves the server's
// key file (the phrase itself is shown once, at initialisation, and is not stored by the server).

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Ferrite.Services.Zerogram;

public static class Bip39
{
    public const int WordCount = 12;
    private static readonly Lazy<string[]> WordsLazy = new(LoadWords);
    private static readonly Lazy<Dictionary<string, int>> IndexLazy = new(() =>
        WordsLazy.Value.Select((w, i) => (w, i)).ToDictionary(x => x.w, x => x.i, StringComparer.Ordinal));

    public static IReadOnlyList<string> Words => WordsLazy.Value;

    private static string[] LoadWords()
    {
        using Stream stream = typeof(Bip39).Assembly.GetManifestResourceStream("Ferrite.Services.Zerogram.bip39-english.txt")
                              ?? throw new InvalidOperationException("BIP-39 word list is missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string[] words = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length != 2048) throw new InvalidDataException("BIP-39 word list must have 2048 words");
        return words;
    }

    public static string Generate()
    {
        byte[] entropy = RandomNumberGenerator.GetBytes(16);
        try { return FromEntropy(entropy); }
        finally { CryptographicOperations.ZeroMemory(entropy); }
    }

    public static string FromEntropy(byte[] entropy)
    {
        if (entropy.Length != 16) throw new ArgumentException("12 words need exactly 128 bits", nameof(entropy));
        byte checksum = SHA256.HashData(entropy)[0];
        // 128 entropy bits + 4 checksum bits = 132 bits = 12 groups of 11 bits.
        var bits = new bool[132];
        for (int i = 0; i < 128; i++) bits[i] = (entropy[i / 8] >> (7 - i % 8) & 1) == 1;
        for (int i = 0; i < 4; i++) bits[128 + i] = (checksum >> (7 - i) & 1) == 1;
        var words = new string[WordCount];
        for (int w = 0; w < WordCount; w++)
        {
            int index = 0;
            for (int b = 0; b < 11; b++) index = index << 1 | (bits[w * 11 + b] ? 1 : 0);
            words[w] = Words[index];
        }
        return string.Join(' ', words);
    }

    /// <summary>Lower-cases and collapses whitespace; returns false unless it is 12 valid words with a good checksum.</summary>
    public static bool TryNormalize(string? input, out string mnemonic)
    {
        mnemonic = "";
        string[] parts = (input ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != WordCount) return false;
        var bits = new bool[132];
        for (int w = 0; w < WordCount; w++)
        {
            if (!IndexLazy.Value.TryGetValue(parts[w].ToLowerInvariant(), out int index)) return false;
            for (int b = 0; b < 11; b++) bits[w * 11 + b] = (index >> (10 - b) & 1) == 1;
        }
        var entropy = new byte[16];
        for (int i = 0; i < 128; i++) if (bits[i]) entropy[i / 8] |= (byte)(1 << (7 - i % 8));
        byte checksum = SHA256.HashData(entropy)[0];
        for (int i = 0; i < 4; i++)
        {
            if (bits[128 + i] != ((checksum >> (7 - i) & 1) == 1)) return false;
        }
        mnemonic = string.Join(' ', parts.Select(p => p.ToLowerInvariant()));
        return true;
    }

    /// <summary>BIP-39 seed: PBKDF2-HMAC-SHA512(mnemonic, "mnemonic", 2048 iterations, 64 bytes).</summary>
    public static byte[] Seed(string normalizedMnemonic) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(normalizedMnemonic), "mnemonic"u8.ToArray(), 2048,
            HashAlgorithmName.SHA512, 64);

    /// <summary>The 32-byte key the owner proof is keyed with.</summary>
    public static byte[] OwnerKey(string normalizedMnemonic)
    {
        byte[] seed = Seed(normalizedMnemonic);
        try { return HMACSHA256.HashData(seed, "zerogram/owner/v1"u8.ToArray()); }
        finally { CryptographicOperations.ZeroMemory(seed); }
    }
}
