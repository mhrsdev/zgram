// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: durable SQLite store for authenticator accounts, recovery-code hashes and rate-limit events.
// Message *content* is never written here; see ZgDeliveryQueue for the undelivered-message index.

using Microsoft.Data.Sqlite;

namespace Ferrite.Services.Zerogram;

public sealed record ZgAccount(string Username, string DisplayName, string Email, string State,
    long? UserId, byte[] SecretSealed, long LastTotpStep, int FailedCount, long LockedUntil,
    int LockLevel, long? PendingAuthKey, long PendingExpires, long CreatedAt);

public sealed class ZgStore : IDisposable
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Reactivation = "reactivation";

    private readonly string _connectionString;
    private readonly SqliteConnection _keepAlive;

    public ZgStore(ZerogramOptions options)
    {
        Directory.CreateDirectory(options.DataPath);
        string path = Path.Combine(options.DataPath, "zerogram.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
        // Holds the WAL open for the process lifetime and applies the durability pragmas once.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        Exec(_keepAlive, "PRAGMA journal_mode=WAL;");
        Migrate();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        // synchronous=FULL: an accepted registration or queued message survives power loss.
        Exec(c, "PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;");
        return c;
    }

    private void Migrate()
    {
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS schema_info(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS accounts(
              username TEXT PRIMARY KEY,
              display_name TEXT NOT NULL,
              email TEXT NOT NULL,
              email_verified INTEGER NOT NULL DEFAULT 0,
              state TEXT NOT NULL,
              user_id INTEGER,
              secret_sealed BLOB NOT NULL,
              last_totp_step INTEGER NOT NULL DEFAULT 0,
              failed_count INTEGER NOT NULL DEFAULT 0,
              locked_until INTEGER NOT NULL DEFAULT 0,
              lock_level INTEGER NOT NULL DEFAULT 0,
              pending_auth_key INTEGER,
              pending_expires INTEGER NOT NULL DEFAULT 0,
              created_at INTEGER NOT NULL,
              activated_at INTEGER
            );
            CREATE UNIQUE INDEX IF NOT EXISTS accounts_user ON accounts(user_id) WHERE user_id IS NOT NULL;
            CREATE TABLE IF NOT EXISTS recovery_codes(
              username TEXT NOT NULL REFERENCES accounts(username) ON DELETE CASCADE,
              code_hash BLOB NOT NULL,
              created_at INTEGER NOT NULL,
              used_at INTEGER,
              PRIMARY KEY(username, code_hash)
            );
            CREATE TABLE IF NOT EXISTS rate_events(
              scope TEXT NOT NULL, key TEXT NOT NULL, ts INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS rate_events_lookup ON rate_events(scope, key, ts);
            CREATE TABLE IF NOT EXISTS group_tokens(
              chat_id INTEGER PRIMARY KEY,
              token_hash BLOB NOT NULL UNIQUE,
              token_sealed BLOB NOT NULL,
              creator_id INTEGER NOT NULL,
              created_at INTEGER NOT NULL
            );
            INSERT OR IGNORE INTO schema_info(key, value) VALUES('version', '1');
            """);
    }

    internal static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static ZgAccount Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
        r.IsDBNull(4) ? null : r.GetInt64(4), (byte[])r.GetValue(5), r.GetInt64(6),
        r.GetInt32(7), r.GetInt64(8), r.GetInt32(9),
        r.IsDBNull(10) ? null : r.GetInt64(10), r.GetInt64(11), r.GetInt64(12));

    private const string AccountColumns =
        "username, display_name, email, state, user_id, secret_sealed, last_totp_step, " +
        "failed_count, locked_until, lock_level, pending_auth_key, pending_expires, created_at";

    public ZgAccount? Get(string username)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {AccountColumns} FROM accounts WHERE username=@u";
        cmd.Parameters.AddWithValue("@u", username);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>
    /// Reserves a username with a fresh sealed secret. Fails when an active/reactivating account owns
    /// it, or an unexpired pending registration owned by a different auth key does.
    /// </summary>
    public bool TryReserve(string username, string displayName, string email, byte[] secretSealed,
        long authKeyId, long now, long pendingExpires)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO accounts(username, display_name, email, state, secret_sealed, pending_auth_key,
                                 pending_expires, created_at)
            VALUES(@u, @d, @e, 'pending', @s, @k, @x, @n)
            ON CONFLICT(username) DO UPDATE SET
              display_name=@d, email=@e, secret_sealed=@s, pending_auth_key=@k, pending_expires=@x,
              created_at=@n, last_totp_step=0, failed_count=0, locked_until=0, lock_level=0
            WHERE accounts.state='pending' AND (accounts.pending_expires <= @n OR accounts.pending_auth_key = @k);
            """;
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@d", displayName);
        cmd.Parameters.AddWithValue("@e", email);
        cmd.Parameters.AddWithValue("@s", secretSealed);
        cmd.Parameters.AddWithValue("@k", authKeyId);
        cmd.Parameters.AddWithValue("@x", pendingExpires);
        cmd.Parameters.AddWithValue("@n", now);
        bool ok = cmd.ExecuteNonQuery() == 1;
        tx.Commit();
        return ok;
    }

    /// <summary>Atomically consumes a TOTP step; false when that step or a later one was already used.</summary>
    public bool TryConsumeStep(string username, long step)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET last_totp_step=@s WHERE username=@u AND last_totp_step<@s";
        cmd.Parameters.AddWithValue("@s", step);
        cmd.Parameters.AddWithValue("@u", username);
        return cmd.ExecuteNonQuery() == 1;
    }

    public void RecordFailure(string username, long now, int maxFailed, long lockBaseSeconds, long lockMaxSeconds)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using var read = c.CreateCommand();
        read.Transaction = tx;
        read.CommandText = "SELECT failed_count, lock_level FROM accounts WHERE username=@u";
        read.Parameters.AddWithValue("@u", username);
        int failed, level;
        using (var r = read.ExecuteReader())
        {
            if (!r.Read()) return;
            failed = r.GetInt32(0) + 1;
            level = r.GetInt32(1);
        }
        long lockedUntil = 0;
        if (failed >= maxFailed)
        {
            level++;
            failed = 0;
            double seconds = Math.Min(lockMaxSeconds, lockBaseSeconds * Math.Pow(2, Math.Min(level - 1, 20)));
            lockedUntil = now + (long)seconds;
        }
        using var upd = c.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = lockedUntil > 0
            ? "UPDATE accounts SET failed_count=@f, lock_level=@l, locked_until=@t WHERE username=@u"
            : "UPDATE accounts SET failed_count=@f, lock_level=@l WHERE username=@u";
        upd.Parameters.AddWithValue("@f", failed);
        upd.Parameters.AddWithValue("@l", level);
        if (lockedUntil > 0) upd.Parameters.AddWithValue("@t", lockedUntil);
        upd.Parameters.AddWithValue("@u", username);
        upd.ExecuteNonQuery();
        tx.Commit();
    }

    public void ClearFailures(string username)
    {
        using var c = Open();
        Exec(c, "UPDATE accounts SET failed_count=0, lock_level=0, locked_until=0 WHERE username=@u",
            ("@u", username));
    }

    /// <summary>Marks the account active, binds the Ferrite user and replaces all recovery codes.</summary>
    public void Activate(string username, long userId, long now, IReadOnlyList<byte[]> codeHashes)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE accounts SET state='active', user_id=@id, activated_at=@n, pending_expires=0,
                  pending_auth_key=NULL, failed_count=0, lock_level=0, locked_until=0
                WHERE username=@u AND state IN ('pending','reactivation')
                """;
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.Parameters.AddWithValue("@n", now);
            cmd.Parameters.AddWithValue("@u", username);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("account is not activatable");
        }
        ReplaceRecoveryCodes(c, tx, username, now, codeHashes);
        tx.Commit();
    }

    private static void ReplaceRecoveryCodes(SqliteConnection c, SqliteTransaction tx, string username,
        long now, IReadOnlyList<byte[]> hashes)
    {
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM recovery_codes WHERE username=@u";
            del.Parameters.AddWithValue("@u", username);
            del.ExecuteNonQuery();
        }
        foreach (byte[] hash in hashes)
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO recovery_codes(username, code_hash, created_at) VALUES(@u,@h,@n)";
            ins.Parameters.AddWithValue("@u", username);
            ins.Parameters.AddWithValue("@h", hash);
            ins.Parameters.AddWithValue("@n", now);
            ins.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<byte[]> UnusedRecoveryHashes(string username)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT code_hash FROM recovery_codes WHERE username=@u AND used_at IS NULL";
        cmd.Parameters.AddWithValue("@u", username);
        var list = new List<byte[]>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((byte[])r.GetValue(0));
        return list;
    }

    /// <summary>
    /// Burns a recovery code and, in the same transaction, swaps in a new sealed secret and moves the
    /// account to 'reactivation'. The remaining unused codes stay valid until reactivation succeeds so a
    /// lost new secret cannot lock the owner out.
    /// </summary>
    public bool ConsumeRecoveryAndRotate(string username, byte[] codeHash, byte[] newSecretSealed, long now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        using (var burn = c.CreateCommand())
        {
            burn.Transaction = tx;
            burn.CommandText = "UPDATE recovery_codes SET used_at=@n WHERE username=@u AND code_hash=@h AND used_at IS NULL";
            burn.Parameters.AddWithValue("@n", now);
            burn.Parameters.AddWithValue("@u", username);
            burn.Parameters.AddWithValue("@h", codeHash);
            if (burn.ExecuteNonQuery() != 1) return false;
        }
        using (var rot = c.CreateCommand())
        {
            rot.Transaction = tx;
            rot.CommandText = """
                UPDATE accounts SET secret_sealed=@s, state='reactivation', last_totp_step=0,
                  failed_count=0, lock_level=0, locked_until=0
                WHERE username=@u AND state IN ('active','reactivation')
                """;
            rot.Parameters.AddWithValue("@s", newSecretSealed);
            rot.Parameters.AddWithValue("@u", username);
            if (rot.ExecuteNonQuery() != 1) return false;
        }
        tx.Commit();
        return true;
    }

    // ---- rate limiting ----

    public int CountEvents(string scope, string key, long since)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM rate_events WHERE scope=@s AND key=@k AND ts>=@t";
        cmd.Parameters.AddWithValue("@s", scope);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@t", since);
        return (int)(long)cmd.ExecuteScalar()!;
    }

    public long? OldestEvent(string scope, string key, long since)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MIN(ts) FROM rate_events WHERE scope=@s AND key=@k AND ts>=@t";
        cmd.Parameters.AddWithValue("@s", scope);
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@t", since);
        object? v = cmd.ExecuteScalar();
        return v is long l ? l : null;
    }

    public void RecordEvent(string scope, string key, long now)
    {
        using var c = Open();
        Exec(c, "INSERT INTO rate_events(scope,key,ts) VALUES(@s,@k,@t)",
            ("@s", scope), ("@k", key), ("@t", now));
    }

    public void PruneEvents(long olderThan)
    {
        using var c = Open();
        Exec(c, "DELETE FROM rate_events WHERE ts<@t", ("@t", olderThan));
        Exec(c, "DELETE FROM accounts WHERE state='pending' AND pending_expires>0 AND pending_expires<@t",
            ("@t", olderThan));
    }

    public void Dispose() => _keepAlive.Dispose();
}
