// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: one random 32-digit join token per basic group. The token is stored sealed (AES-GCM) and is
// looked up through a keyed hash, so a copy of zerogram.db alone does not reveal usable tokens.

using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Ferrite.Services.Zerogram;

public sealed record ZgGroupToken(long ChatId, string Token, long CreatorId, long CreatedAt);

public sealed class ZgGroupTokens
{
    public const int Digits = 32;

    private readonly ZgStore _store;
    private readonly SecretVault _vault;
    private readonly TimeProvider _time;

    public ZgGroupTokens(ZgStore store, SecretVault vault, TimeProvider time)
    {
        _store = store;
        _vault = vault;
        _time = time;
    }

    public static string Generate()
    {
        var chars = new char[Digits];
        for (int i = 0; i < chars.Length; i++) chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(chars);
    }

    /// <summary>Keeps digits only, so "1234-5678 ..." typed or pasted with separators still matches.</summary>
    public static string Normalize(string? token) => new((token ?? "").Where(char.IsAsciiDigit).ToArray());

    public static bool IsWellFormed(string normalized) => normalized.Length == Digits;

    private static string Aad(long chatId) => "group-token:" + chatId;

    public ZgGroupToken Ensure(long chatId, long creatorId)
    {
        if (Get(chatId) is { } existing) return existing;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string token = Generate();
            using var c = _store.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO group_tokens(chat_id, token_hash, token_sealed, creator_id, created_at) " +
                              "VALUES(@c, @h, @s, @u, @t)";
            cmd.Parameters.AddWithValue("@c", chatId);
            cmd.Parameters.AddWithValue("@h", _vault.HashToken(token));
            cmd.Parameters.AddWithValue("@s", _vault.Seal(System.Text.Encoding.ASCII.GetBytes(token), Aad(chatId)));
            cmd.Parameters.AddWithValue("@u", creatorId);
            cmd.Parameters.AddWithValue("@t", _time.GetUtcNow().ToUnixTimeSeconds());
            try { cmd.ExecuteNonQuery(); }
            catch (SqliteException) { continue; }
            if (Get(chatId) is { } created) return created;
        }
        throw new InvalidOperationException("could not allocate a group token");
    }

    public ZgGroupToken? Get(long chatId)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT chat_id, token_sealed, creator_id, created_at FROM group_tokens WHERE chat_id=@c";
        cmd.Parameters.AddWithValue("@c", chatId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public IReadOnlyList<ZgGroupToken> All()
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT chat_id, token_sealed, creator_id, created_at FROM group_tokens ORDER BY created_at";
        using var r = cmd.ExecuteReader();
        var list = new List<ZgGroupToken>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public long? FindChat(string normalizedToken)
    {
        if (!IsWellFormed(normalizedToken)) return null;
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT chat_id FROM group_tokens WHERE token_hash=@h";
        cmd.Parameters.AddWithValue("@h", _vault.HashToken(normalizedToken));
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    public ZgGroupToken? Rotate(long chatId)
    {
        if (Get(chatId) is null) return null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string token = Generate();
            using var c = _store.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE group_tokens SET token_hash=@h, token_sealed=@s WHERE chat_id=@c";
            cmd.Parameters.AddWithValue("@h", _vault.HashToken(token));
            cmd.Parameters.AddWithValue("@s", _vault.Seal(System.Text.Encoding.ASCII.GetBytes(token), Aad(chatId)));
            cmd.Parameters.AddWithValue("@c", chatId);
            try { cmd.ExecuteNonQuery(); }
            catch (SqliteException) { continue; }
            return Get(chatId);
        }
        throw new InvalidOperationException("could not rotate the group token");
    }

    private ZgGroupToken Read(SqliteDataReader r)
    {
        long chatId = r.GetInt64(0);
        byte[] plain = _vault.Open((byte[])r.GetValue(1), Aad(chatId));
        try { return new ZgGroupToken(chatId, System.Text.Encoding.ASCII.GetString(plain), r.GetInt64(2), r.GetInt64(3)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
