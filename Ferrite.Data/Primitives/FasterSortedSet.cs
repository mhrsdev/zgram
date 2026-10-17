// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2022-2026 Aykut Alparslan KOC

using FASTER.core;

namespace Ferrite.Data.Primitives;

public class FasterSortedSet<T> :IAsyncDisposable where T: IComparable<T>
{
    private readonly FasterContext<string, SortedSet<T>> _context;
    private readonly string _name;

    public FasterSortedSet(FasterContext<string, SortedSet<T>> context, string name)
    {
        _context = context;
        _name = name;
    }
    
    public IReadOnlySet<T> Get()
    {
        using var session = _context.Store.NewSession(new SortedSetFunctions<string, SortedSet<T>, T>(
            (set, l) =>
            {
                return set ??= new();
            }));
        var status = session.Read(_name, out var result);
        if (status.IsPending)
        {
            session.CompletePendingWithOutputs(out var outputs, wait: true);
            using (outputs)
            {
                while (outputs.Next()) result = outputs.Current.Output;
            }
        }
        return result ?? new SortedSet<T>();
    }

    public async ValueTask Add(T value)
    {
        using var session = _context.Store.NewSession(new SortedSetFunctions<string, SortedSet<T>, T>(
            (set, l) =>
            {
                set ??= new();
                set.Add(l);
                return set;
            }));
        if (session.RMW(_name, value).IsPending) session.CompletePending(wait: true);
    }
    
    public async ValueTask Remove(T value)
    {
        using var session = _context.Store.NewSession(new SortedSetFunctions<string, SortedSet<T>, T>(
            (set, l) =>
            {
                if (set == null)
                {
                    return null;
                }
                set.Remove(value);
                return set;
            }));
        if (session.RMW(_name, value).IsPending) session.CompletePending(wait: true);
    }
    
    public async ValueTask RemoveEqualOrLess(T value)
    {
        using var session = _context.Store.NewSession(new SortedSetFunctions<string, SortedSet<T>, T>(
            (set, l) =>
            {
                if (set == null)
                {
                    return null;
                }
                while (set.Count > 0 && set.Min.CompareTo(l) <= 0)
                {
                    set.Remove(set.Min);
                }
                return set;
            }));
        if (session.RMW(_name, value).IsPending) session.CompletePending(wait: true);
    }
    class SortedSetFunctions<Key, Value, Input> : FunctionsBase<Key, Value, Input, Value, Empty>
    {
        private readonly Func<Value, Input, Value> merger;
        public SortedSetFunctions() => merger = (l, r) => l;
        public SortedSetFunctions(Func<Value, Input, Value> merger) => this.merger = merger;
        public override bool ConcurrentReader(ref Key key, ref Input input, ref Value value, ref Value dst, ref ReadInfo readInfo)
        {
            dst = value;
            return true;
        }

        public override bool SingleReader(ref Key key, ref Input input, ref Value value, ref Value dst, ref ReadInfo readInfo)
        {
            dst = value;
            return true;
        }

        public override bool InitialUpdater(ref Key key, ref Input input, ref Value value, ref Value output, ref RMWInfo rmwInfo){ output = value = merger(value, input); return true; }
        public override bool CopyUpdater(ref Key key, ref Input input, ref Value oldValue, ref Value newValue, ref Value output, ref RMWInfo rmwInfo) { output = newValue = merger(oldValue, input); return true; }
        public override bool InPlaceUpdater(ref Key key, ref Input input, ref Value value, ref Value output, ref RMWInfo rmwInfo) { output = value = merger(value, input); return true; }
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
    }
}