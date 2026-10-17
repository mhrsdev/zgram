// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: the server keeps a message only until the recipient's device confirms it stored it.
//
//   * every copy handed to a recipient is registered in the queue index before it is written
//   * the recipient's zg.ackMessages (sent after the app committed the message to its local database)
//     purges that copy, idempotently and silently (no deleteMessages update reaches any client)
//   * copies still unacknowledged after the retention period are purged by the sweeper
//   * the sender's own copy is purged as soon as the send is accepted; the idempotency record keeps
//     retransmissions from creating duplicates
//
// Nothing here logs or indexes message text.

using Ferrite.Data.Repositories;
using Ferrite.Services.Zerogram;
using Ferrite.TL;
using Ferrite.TL.baseLayer;
using Ferrite.Utils;

namespace Ferrite.Services.Zerogram;

public sealed class ZgSendRejectedException : Exception
{
    public string Code { get; }
    public ZgSendRejectedException(string code) : base(code) => Code = code;
}

/// <summary>Result of <see cref="ZgRetention.BeginSend"/>.</summary>
public sealed record ZgSendGate(long Sender, long RandomId, ZgSentRecord? Replay);

public sealed class ZgRetention
{
    private readonly ZerogramOptions _options;
    private readonly ZgQueueStore _queue;
    private readonly TimeProvider _time;
    private readonly IMessageRepository _messages;
    private readonly IMessageReactionsRepository _copies;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUpdatesContextFactory _contexts;
    private readonly IUpdatesService _updates;
    private readonly ILogger _log;

    public ZgRetention(ZerogramOptions options, ZgQueueStore queue, TimeProvider time,
        IMessageRepository messages, IMessageReactionsRepository copies, IUnitOfWork unitOfWork,
        IUpdatesContextFactory contexts, IUpdatesService updates, ILogger log)
    {
        _options = options;
        _queue = queue;
        _time = time;
        _messages = messages;
        _copies = copies;
        _unitOfWork = unitOfWork;
        _contexts = contexts;
        _updates = updates;
        _log = log;
    }

    private long Now => _time.GetUtcNow().ToUnixTimeSeconds();

    public ZgQueueStore Queue => _queue;

    // ------------------------------------------------------------------ send path

    /// <summary>
    /// Idempotency and capacity gate. A retransmitted (sender, random_id) that was already accepted
    /// returns the original result; a full recipient queue rejects the send *before* anything is
    /// stored, so the sender keeps the message as unsent rather than losing it silently.
    /// </summary>
    public async Task<ZgSendGate> BeginSendAsync(long sender, long randomId,
        IReadOnlyCollection<long> recipients, long estimatedBytes)
    {
        long now = Now;
        ZgBegin begin = _queue.BeginSend(sender, randomId, now, (long)_options.QueueTtl.TotalSeconds + 86400);
        if (begin.Kind == ZgBeginKind.Replay) return new ZgSendGate(sender, randomId, begin.Record);
        if (begin.Kind == ZgBeginKind.InFlight) throw new ZgSendRejectedException("MSG_SEND_IN_PROGRESS");

        // An earlier attempt died mid-way: remove whatever it wrote before starting over.
        var stale = _queue.RowsOfAttempt(sender, randomId);
        if (stale.Count > 0)
        {
            foreach (var row in stale) await PurgeCopyAsync(row.Recipient, row.MsgId);
            if (stale[0].SenderMsgId > 0) await PurgeCopyAsync(sender, stale[0].SenderMsgId);
            _queue.Abort(sender, randomId);
            _queue.BeginSend(sender, randomId, now, (long)_options.QueueTtl.TotalSeconds + 86400);
        }

        try
        {
            foreach (long recipient in recipients)
            {
                var (count, bytes) = _queue.Usage(recipient);
                if (count >= _options.QueueMaxMessagesPerRecipient ||
                    bytes + estimatedBytes > _options.QueueMaxBytesPerRecipient)
                    throw new ZgSendRejectedException("RECIPIENT_QUEUE_FULL");
            }
            if (_queue.TotalBytes() + estimatedBytes * Math.Max(1, recipients.Count) > _options.QueueMaxTotalBytes)
                throw new ZgSendRejectedException("SERVER_QUEUE_FULL");
        }
        catch
        {
            _queue.Abort(sender, randomId);
            throw;
        }
        return new ZgSendGate(sender, randomId, null);
    }

    /// <summary>Registers a recipient copy; call after its id is allocated and before it is written.</summary>
    public void RegisterCopy(ZgSendGate gate, long recipient, int recipientMsgId, int senderMsgId,
        int senderPeerType, long senderPeerId, long size)
    {
        long now = Now;
        _queue.AddRow(gate.Sender, gate.RandomId, recipient, recipientMsgId, senderMsgId,
            senderPeerType, senderPeerId, size, now, now + (long)_options.QueueTtl.TotalSeconds);
    }

    public async Task CompleteSendAsync(ZgSendGate gate, int senderMsgId, int pts, int date, long logicalId)
    {
        _queue.Complete(gate.Sender, gate.RandomId, senderMsgId, pts, date);
        // The sender's device already holds the message (it learns the id from this very response).
        await PurgeCopyAsync(gate.Sender, senderMsgId);
        _copies.DeleteMessageCopies(logicalId);
        await _unitOfWork.SaveAsync();
        Ferrite.Data.Repositories.RocksDBContext.SyncAll();
    }

    public void AbortSend(ZgSendGate gate) => _queue.Abort(gate.Sender, gate.RandomId);

    // ------------------------------------------------------------------ acknowledgement

    /// <returns>how many queue entries this call removed (repeats return 0).</returns>
    public async Task<int> AckAsync(long userId, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return 0;
        var rows = _queue.Remove(userId, ids.Take(10_000));
        foreach (var row in rows) await PurgeCopyAsync(row.Recipient, row.MsgId);
        if (rows.Count > 0)
        {
            await _unitOfWork.SaveAsync();
            Ferrite.Data.Repositories.RocksDBContext.SyncAll();
            await AdvanceAndNotifyAsync(rows);
        }
        return rows.Count;
    }

    private async Task AdvanceAndNotifyAsync(IReadOnlyList<ZgQueueRow> rows)
    {
        long now = Now;
        foreach (var group in rows.GroupBy(r => (r.Sender, r.SenderPeerType, r.SenderPeerId)))
        {
            int ackedMax = group.Max(r => r.SenderMsgId);
            int? mark = _queue.AdvanceDelivered(group.Key.Sender, group.Key.SenderPeerType,
                group.Key.SenderPeerId, ackedMax, now);
            if (mark is not { } maxId) continue;
            try
            {
                await NotifyDeliveredAsync(group.Key.Sender, group.Key.SenderPeerType, group.Key.SenderPeerId, maxId);
            }
            catch (Exception e)
            {
                // The mark is durable; the sender fetches it with zg.getDeliveryState on reconnect.
                _log.Warning(e, "zerogram: delivered notification failed");
            }
        }
    }

    private async Task NotifyDeliveredAsync(long sender, int peerType, long peerId, int maxId)
    {
        var context = _contexts.GetUpdatesContext(null, sender);
        int pts = await context.IncrementPts();
        using TLPeer peer = Ferrite.Services.Users.PeerResolver.BuildPeer((TLPeer.PeerType)peerType, peerId);
        TLUpdate update = Ferrite.TL.baseLayer.UpdateReadHistoryOutbox.Builder()
            .Peer(peer.AsSpan())
            .MaxId(maxId)
            .Pts(pts)
            .PtsCount(1)
            .Build();
        await _updates.EnqueueUpdate(sender, update);
        await context.SettlePts(pts, pts);
    }

    // ------------------------------------------------------------------ expiry

    public async Task<int> SweepAsync()
    {
        int total = 0;
        while (true)
        {
            var expired = _queue.TakeExpired(Now, 500);
            if (expired.Count == 0) break;
            foreach (var row in expired) await PurgeCopyAsync(row.Recipient, row.MsgId);
            await _unitOfWork.SaveAsync();
            total += expired.Count;
            _log.Information($"zerogram: expired {expired.Count} undelivered message(s)");
        }
        long now = Now;
        _queue.PruneSent(now);
        _queue.PruneDelivered(now - 180L * 86400);
        if (total > 0) Ferrite.Data.Repositories.RocksDBContext.SyncAll();
        return total;
    }

    // ------------------------------------------------------------------ purge primitive

    /// <summary>Silent removal of one stored copy (no update is queued for any client).</summary>
    public ValueTask PurgeCopyAsync(long ownerId, int messageId)
    {
        _messages.DeleteMessage(ownerId, messageId);
        return ValueTask.CompletedTask;
    }
}
