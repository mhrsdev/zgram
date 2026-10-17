// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2022-2026 Aykut Alparslan KOC

using RocksDbSharp;

namespace Ferrite.Data.Repositories;

public class RocksDBContext : IDisposable
{
    private static readonly List<WeakReference<RocksDBContext>> Open = new();
    private static readonly object BarrierGate = new();
    private static readonly byte[] BarrierKey = "\0zerogram-durability-barrier"u8.ToArray();
    private static readonly WriteOptions SyncWrite = new WriteOptions().SetSync(true);
    private static long _barrierRequested;
    private static long _barrierCompleted;

    /// <summary>
    /// Forces every open RocksDB write-ahead log to stable storage. Concurrent callers share one flush:
    /// a caller returns as soon as a flush that began after its request has finished.
    /// </summary>
    public static void SyncAll()
    {
        long mine = Interlocked.Increment(ref _barrierRequested);
        lock (BarrierGate)
        {
            if (Volatile.Read(ref _barrierCompleted) >= mine) return;
            long target = Volatile.Read(ref _barrierRequested);
            lock (Open)
            {
                foreach (var weak in Open)
                {
                    if (weak.TryGetTarget(out var context) && !context._disposed) context._db.Put(BarrierKey, BarrierKey, context._cf, SyncWrite);
                }
            }
            Volatile.Write(ref _barrierCompleted, target);
        }
    }

    /// <summary>Compacts the whole key space so deleted values are physically dropped.</summary>
    public static void CompactAll()
    {
        lock (Open)
        {
            foreach (var weak in Open)
            {
                if (weak.TryGetTarget(out var context) && !context._disposed)
                    context._db.CompactRange((byte[]?)null, (byte[]?)null, context._cf);
            }
        }
    }

    private readonly RocksDb _db;
    private readonly ColumnFamilyHandle _cf;
    private bool _disposed;
    public RocksDBContext()
    {
        _db = RocksDb.Open( new DbOptions().SetCreateIfMissing(true).SetDbWriteBufferSize(32UL << 20), "ferrite", new ColumnFamilies());
        _cf = _db.GetDefaultColumnFamily();
        lock (Open) Open.Add(new WeakReference<RocksDBContext>(this));
    }
    public RocksDBContext(string path)
    {
        _db = RocksDb.Open( new DbOptions().SetCreateIfMissing(true).SetDbWriteBufferSize(32UL << 20), path, new ColumnFamilies());
        _cf = _db.GetDefaultColumnFamily();
        lock (Open) Open.Add(new WeakReference<RocksDBContext>(this));
    }
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _db.Put(key, value, _cf);
    }
    public byte[] Get(ReadOnlySpan<byte> key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _db.Get(key, _cf);
    }
    public void Delete(ReadOnlySpan<byte> key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _db.Remove(key, _cf);
    }
    public void DeleteWithPrefix(byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _db.RemoveWithPrefix(key, _cf);
    }
    public IEnumerable<byte[]> Iterate(byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var iter = _db.NewIterator(_cf);
        iter.Seek(key);
        while(iter.Valid())
        {
            var iterKey = iter.Key();
            if (iterKey.Length < key.Length) yield break;
            if (!key.AsSpan().SequenceEqual(iterKey.AsSpan(0, key.Length)))
            {
                yield break;
            }
            yield return iter.Value();
            iter.Next();
        }
    }
    public IEnumerable<byte[]> IterateKeys(byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var iter = _db.NewIterator(_cf);
        iter.Seek(key);
        while(iter.Valid())
        {
            var iterKey = iter.Key();
            if (iterKey.Length < key.Length) yield break;
            if (!key.AsSpan().SequenceEqual(iterKey.AsSpan(0, key.Length)))
            {
                yield break;
            }
            yield return iterKey;
            iter.Next();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _db.Dispose();
    }
}