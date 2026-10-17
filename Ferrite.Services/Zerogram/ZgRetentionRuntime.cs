// SPDX-License-Identifier: AGPL-3.0-or-later
// Zerogram: background sweeper for expired undelivered messages, plus a periodic RocksDB compaction so
// purged message bytes do not linger in SST files and become an accidental archive.

using Ferrite.Utils;

namespace Ferrite.Services.Zerogram;

public sealed class ZgRetentionRuntime
{
    private readonly ZgRetention _retention;
    private readonly ZerogramOptions _options;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public ZgRetentionRuntime(ZgRetention retention, ZerogramOptions options, ILogger log)
    {
        _retention = retention;
        _options = options;
        _log = log;
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_loop is not null) return;
            await _retention.SweepAsync();
            _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = RunAsync(_stopping.Token);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        Task? loop;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_loop is null) return;
            _stopping!.Cancel();
            loop = _loop;
            _loop = null;
        }
        finally { _gate.Release(); }
        try { await loop; } catch (OperationCanceledException) { }
    }

    private async Task RunAsync(CancellationToken token)
    {
        var compactEvery = TimeSpan.FromHours(24);
        var lastCompaction = DateTimeOffset.UtcNow;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.QueueSweepInterval, token);
                await _retention.SweepAsync();
                if (DateTimeOffset.UtcNow - lastCompaction > compactEvery)
                {
                    lastCompaction = DateTimeOffset.UtcNow;
                    Ferrite.Data.Repositories.RocksDBContext.CompactAll();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { _log.Error(e, "zerogram: retention sweep failed"); }
        }
    }
}
