// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: guarantees that *every* copy written to a message box is accounted for in the delivery
// queue, whichever code path wrote it (text, service messages, history copies), so it is purged on
// acknowledgement or expiry instead of accumulating as server-side history.

using Ferrite.Data.Repositories;

namespace Ferrite.Services.Zerogram;

public sealed class ZgMessageTracker : IMessageTracker
{
    private readonly ZgQueueStore _queue;
    private readonly ZerogramOptions _options;
    private readonly TimeProvider _time;

    public ZgMessageTracker(ZgQueueStore queue, ZerogramOptions options, TimeProvider time)
    {
        _queue = queue;
        _options = options;
        _time = time;
    }

    public void OnStored(long ownerId, int messageId, int storedBytes)
    {
        long now = _time.GetUtcNow().ToUnixTimeSeconds();
        _queue.TrackIfAbsent(ownerId, messageId, storedBytes, now, now + (long)_options.QueueTtl.TotalSeconds);
    }
}
