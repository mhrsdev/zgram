// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: durable index of *undelivered* messages. Holds identifiers, sizes and deadlines only;
// message text lives solely in the message repository and is removed when the recipient acknowledges
// (or the deadline passes). Also keeps the (sender, random_id) idempotency record that makes a
// retransmitted send return the original result instead of creating a duplicate.

using Microsoft.Data.Sqlite;

namespace Ferrite.Services.Zerogram;

public enum ZgBeginKind { Fresh, Replay, InFlight }

public sealed record ZgSentRecord(int MsgId, int Pts, int Date);

public readonly record struct ZgBegin(ZgBeginKind Kind, ZgSentRecord? Record = null);

public sealed record ZgQueueRow(long Recipient, int MsgId, long Sender, int SenderMsgId,
    int SenderPeerType, long SenderPeerId, long Size, long Expires);

public sealed record ZgQueueStats(long Messages, long Bytes, long Recipients, long OldestAgeSeconds);

public sealed class ZgQueueStore
{
    private static readonly TimeSpan SendAttemptGrace = TimeSpan.FromSeconds(30);
    private readonly ZgStore _store;

    public ZgQueueStore(ZgStore store)
    {
        _store = store;
        using var c = store.Open();
        ZgStore.Exec(c, """
            CREATE TABLE IF NOT EXISTS sent(
              sender INTEGER NOT NULL,
              random_id INTEGER NOT NULL,
              state INTEGER NOT NULL,            -- 0 attempt in progress, 1 accepted
              msg_id INTEGER NOT NULL DEFAULT 0,
              pts INTEGER NOT NULL DEFAULT 0,
              date INTEGER NOT NULL DEFAULT 0,
              attempt_at INTEGER NOT NULL,
              expires INTEGER NOT NULL,
              PRIMARY KEY(sender, random_id)
            ) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS sent_expires ON sent(expires);
            CREATE TABLE IF NOT EXISTS queue(
              recipient INTEGER NOT NULL,
              msg_id INTEGER NOT NULL,
              sender INTEGER NOT NULL,
              origin_random INTEGER NOT NULL,
              sender_msg_id INTEGER NOT NULL DEFAULT 0,
              s_peer_type INTEGER NOT NULL,
              s_peer_id INTEGER NOT NULL,
              size INTEGER NOT NULL,
              created INTEGER NOT NULL,
              expires INTEGER NOT NULL,
              PRIMARY KEY(recipient, msg_id)
            ) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS queue_expires ON queue(expires);
            CREATE INDEX IF NOT EXISTS queue_origin ON queue(sender, origin_random);
            CREATE INDEX IF NOT EXISTS queue_sender_peer ON queue(sender, s_peer_type, s_peer_id, sender_msg_id);
            CREATE TABLE IF NOT EXISTS delivered(
              sender INTEGER NOT NULL,
              s_peer_type INTEGER NOT NULL,
              s_peer_id INTEGER NOT NULL,
              max_id INTEGER NOT NULL,
              acked_max INTEGER NOT NULL DEFAULT 0,
              updated INTEGER NOT NULL,
              PRIMARY KEY(sender, s_peer_type, s_peer_id)
            ) WITHOUT ROWID;
            """);
    }

    // ---------------------------------------------------------------- send gate

    public ZgBegin BeginSend(long sender, long randomId, long now, long idempotencyTtlSeconds)
    {
        using var c = _store.Open();
        using var tx = c.BeginTransaction(deferred: false);
        using (var cmd = c.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT state, msg_id, pts, date, attempt_at FROM sent WHERE sender=@s AND random_id=@r";
            cmd.Parameters.AddWithValue("@s", sender);
            cmd.Parameters.AddWithValue("@r", randomId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                int state = r.GetInt32(0);
                if (state == 1)
                    return new ZgBegin(ZgBeginKind.Replay,
                        new ZgSentRecord(r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
                if (now - r.GetInt64(4) < SendAttemptGrace.TotalSeconds)
                    return new ZgBegin(ZgBeginKind.InFlight);
                // Stale attempt (crash, or a lost connection mid-send): fall through and retry it.
            }
        }
        using (var up = c.CreateCommand())
        {
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO sent(sender, random_id, state, attempt_at, expires)
                VALUES(@s,@r,0,@n,@x)
                ON CONFLICT(sender, random_id) DO UPDATE SET state=0, attempt_at=@n, expires=@x
                """;
            up.Parameters.AddWithValue("@s", sender);
            up.Parameters.AddWithValue("@r", randomId);
            up.Parameters.AddWithValue("@n", now);
            up.Parameters.AddWithValue("@x", now + idempotencyTtlSeconds);
            up.ExecuteNonQuery();
        }
        tx.Commit();
        return new ZgBegin(ZgBeginKind.Fresh);
    }

    /// <summary>Copies written by an earlier, unfinished attempt of the same send.</summary>
    public IReadOnlyList<ZgQueueRow> RowsOfAttempt(long sender, long randomId)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectRows + " WHERE sender=@s AND origin_random=@r";
        cmd.Parameters.AddWithValue("@s", sender);
        cmd.Parameters.AddWithValue("@r", randomId);
        return ReadRows(cmd);
    }

    public void AddRow(long sender, long randomId, long recipient, int msgId, int senderMsgId,
        int peerType, long peerId, long size, long now, long expires)
    {
        using var c = _store.Open();
        ZgStore.Exec(c, """
            INSERT OR REPLACE INTO queue(recipient, msg_id, sender, origin_random, sender_msg_id,
              s_peer_type, s_peer_id, size, created, expires)
            VALUES(@rcp,@mid,@s,@r,@smid,@pt,@pi,@sz,@n,@x)
            """, ("@rcp", recipient), ("@mid", msgId), ("@s", sender), ("@r", randomId),
            ("@smid", senderMsgId), ("@pt", peerType), ("@pi", peerId), ("@sz", size),
            ("@n", now), ("@x", expires));
    }

    public void SetSenderMessageId(long sender, long randomId, int senderMsgId)
    {
        using var c = _store.Open();
        ZgStore.Exec(c, "UPDATE queue SET sender_msg_id=@m WHERE sender=@s AND origin_random=@r",
            ("@m", senderMsgId), ("@s", sender), ("@r", randomId));
    }

    public void Complete(long sender, long randomId, int msgId, int pts, int date)
    {
        using var c = _store.Open();
        ZgStore.Exec(c, "UPDATE sent SET state=1, msg_id=@m, pts=@p, date=@d WHERE sender=@s AND random_id=@r",
            ("@m", msgId), ("@p", pts), ("@d", date), ("@s", sender), ("@r", randomId));
    }

    public void Abort(long sender, long randomId)
    {
        using var c = _store.Open();
        using var tx = c.BeginTransaction();
        ZgStore.Exec(c, "DELETE FROM queue WHERE sender=@s AND origin_random=@r", ("@s", sender), ("@r", randomId));
        ZgStore.Exec(c, "DELETE FROM sent WHERE sender=@s AND random_id=@r AND state=0", ("@s", sender), ("@r", randomId));
        tx.Commit();
    }

    // ---------------------------------------------------------------- capacity

    public (long Messages, long Bytes) Usage(long recipient)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(size),0) FROM queue WHERE recipient=@r";
        cmd.Parameters.AddWithValue("@r", recipient);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt64(0), r.GetInt64(1));
    }

    public long TotalBytes()
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(size),0) FROM queue";
        return (long)cmd.ExecuteScalar()!;
    }

    // ---------------------------------------------------------------- acknowledgement / expiry

    private const string SelectRows =
        "SELECT recipient, msg_id, sender, sender_msg_id, s_peer_type, s_peer_id, size, expires FROM queue";

    private static List<ZgQueueRow> ReadRows(SqliteCommand cmd)
    {
        var rows = new List<ZgQueueRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new ZgQueueRow(r.GetInt64(0), r.GetInt32(1), r.GetInt64(2), r.GetInt32(3),
                r.GetInt32(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7)));
        return rows;
    }

    /// <summary>Removes and returns the recipient's rows. Unknown or already-removed ids are ignored.</summary>
    public IReadOnlyList<ZgQueueRow> Remove(long recipient, IEnumerable<int> msgIds)
    {
        var removed = new List<ZgQueueRow>();
        using var c = _store.Open();
        using var tx = c.BeginTransaction();
        foreach (int id in msgIds.Distinct())
        {
            using var sel = c.CreateCommand();
            sel.Transaction = tx;
            sel.CommandText = SelectRows + " WHERE recipient=@r AND msg_id=@m";
            sel.Parameters.AddWithValue("@r", recipient);
            sel.Parameters.AddWithValue("@m", id);
            var rows = ReadRows(sel);
            if (rows.Count == 0) continue;
            using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM queue WHERE recipient=@r AND msg_id=@m";
            del.Parameters.AddWithValue("@r", recipient);
            del.Parameters.AddWithValue("@m", id);
            del.ExecuteNonQuery();
            removed.AddRange(rows);
        }
        tx.Commit();
        return removed;
    }

    public IReadOnlyList<ZgQueueRow> TakeExpired(long now, int limit)
    {
        using var c = _store.Open();
        using var tx = c.BeginTransaction();
        using var sel = c.CreateCommand();
        sel.Transaction = tx;
        sel.CommandText = SelectRows + " WHERE expires<=@n ORDER BY expires LIMIT @l";
        sel.Parameters.AddWithValue("@n", now);
        sel.Parameters.AddWithValue("@l", limit);
        var rows = ReadRows(sel);
        foreach (var row in rows)
        {
            using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM queue WHERE recipient=@r AND msg_id=@m";
            del.Parameters.AddWithValue("@r", row.Recipient);
            del.Parameters.AddWithValue("@m", row.MsgId);
            del.ExecuteNonQuery();
        }
        tx.Commit();
        return rows;
    }

    /// <summary>Rows whose recipient account no longer exists or was revoked are handled by expiry.</summary>
    public IReadOnlyList<ZgQueueRow> AllRowsForRecipient(long recipient)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = SelectRows + " WHERE recipient=@r";
        cmd.Parameters.AddWithValue("@r", recipient);
        return ReadRows(cmd);
    }

    public void PruneSent(long now)
    {
        using var c = _store.Open();
        ZgStore.Exec(c, "DELETE FROM sent WHERE expires<@n", ("@n", now));
    }

    // ---------------------------------------------------------------- delivery marks

    /// <summary>
    /// Advances the contiguous "delivered up to" mark of (sender, peer) after <paramref name="ackedSenderIds"/>
    /// were acknowledged or expired. Returns the new mark when it moved forward.
    /// </summary>
    public int? AdvanceDelivered(long sender, int peerType, long peerId, int ackedMax, long now)
    {
        using var c = _store.Open();
        using var tx = c.BeginTransaction();
        int previous = 0, ackedStored = 0;
        using (var sel = c.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT max_id, acked_max FROM delivered WHERE sender=@s AND s_peer_type=@t AND s_peer_id=@p";
            sel.Parameters.AddWithValue("@s", sender);
            sel.Parameters.AddWithValue("@t", peerType);
            sel.Parameters.AddWithValue("@p", peerId);
            using var r = sel.ExecuteReader();
            if (r.Read()) { previous = r.GetInt32(0); ackedStored = r.GetInt32(1); }
        }
        int highestAcked = Math.Max(ackedStored, ackedMax);
        int? minPending = null;
        using (var pend = c.CreateCommand())
        {
            pend.Transaction = tx;
            pend.CommandText = "SELECT MIN(sender_msg_id) FROM queue WHERE sender=@s AND s_peer_type=@t AND s_peer_id=@p";
            pend.Parameters.AddWithValue("@s", sender);
            pend.Parameters.AddWithValue("@t", peerType);
            pend.Parameters.AddWithValue("@p", peerId);
            object? v = pend.ExecuteScalar();
            if (v is long l) minPending = (int)l;
        }
        int mark = minPending is { } m ? Math.Min(highestAcked, m - 1) : highestAcked;
        mark = Math.Max(mark, previous);
        ZgStore.Exec(c, """
            INSERT INTO delivered(sender, s_peer_type, s_peer_id, max_id, acked_max, updated)
            VALUES(@s,@t,@p,@m,@a,@n)
            ON CONFLICT(sender, s_peer_type, s_peer_id) DO UPDATE SET max_id=@m, acked_max=@a, updated=@n
            """, ("@s", sender), ("@t", peerType), ("@p", peerId), ("@m", mark), ("@a", highestAcked), ("@n", now));
        tx.Commit();
        return mark > previous ? mark : null;
    }

    public IReadOnlyList<(int PeerType, long PeerId, int MaxId)> DeliveryMarks(long sender)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT s_peer_type, s_peer_id, max_id FROM delivered WHERE sender=@s AND max_id>0";
        cmd.Parameters.AddWithValue("@s", sender);
        var list = new List<(int, long, int)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt32(0), r.GetInt64(1), r.GetInt32(2)));
        return list;
    }

    public void PruneDelivered(long olderThan)
    {
        using var c = _store.Open();
        ZgStore.Exec(c, "DELETE FROM delivered WHERE updated<@t", ("@t", olderThan));
    }

    public ZgQueueStats Stats(long now)
    {
        using var c = _store.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(size),0), COUNT(DISTINCT recipient), COALESCE(MIN(created), @n) FROM queue";
        cmd.Parameters.AddWithValue("@n", now);
        using var r = cmd.ExecuteReader();
        r.Read();
        return new ZgQueueStats(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), now - r.GetInt64(3));
    }
}
