// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2022-2026 Aykut Alparslan KOC

using FASTER.core;
using Ferrite.Data.Primitives;

namespace Ferrite.Data.Counters;

public class FasterCounter : IAtomicCounter
{
    private readonly FasterContext<string, long> _context;
    private string _name;
    private long _inc = 1;
    private bool _disposed = false;
    private readonly long _epoch;
    
    /// <param name="epochShifted">issued identifiers are shifted by <see cref="CounterEpoch.Offset"/>;
    /// pass false for values that merely mirror client input (e.g. read markers).</param>
    public FasterCounter(FasterContext<string, long> context, string name, bool epochShifted = true)
    {
        _context = context;
        _name = name;
        _epoch = epochShifted ? CounterEpoch.Offset : 0;
    }

    // Zerogram: a FASTER session registers itself with the store until it is disposed. The original code
    // kept one per counter object and counter objects are created per request, which leaked sessions
    // without bound under load. Sessions are now short-lived (one per operation).
    private long Apply(long input)
    {
        using var session = _context.Store.NewSession(new RMWSimpleFunctions<string, long>((a, b) => a + b));
        long output = 0;
        var status = session.RMW(ref _name, ref input, ref output);
        if (status.IsPending)
        {
            session.CompletePendingWithOutputs(out var outputs, wait: true);
            using (outputs)
            {
                while (outputs.Next()) output = outputs.Current.Output;
            }
        }
        return output;
    }
    
    public ValueTask<long> Get()
    {
        if (_disposed) throw new ObjectDisposedException(_name);
        long value = 0;
        using var session = _context.Store.NewSession(new RMWSimpleFunctions<string, long>((a, b) => a + b));
        var status = session.Read(ref _name, ref value);
        if (status.IsPending)
        {
            session.CompletePendingWithOutputs(out var outputs, wait: true);
            using (outputs)
            {
                while (outputs.Next()) value = outputs.Current.Output;
            }
        }
        return ValueTask.FromResult(value + _epoch);
    }

    public ValueTask<long> IncrementAndGet()
    {
        if (_disposed) throw new ObjectDisposedException(_name);
        return ValueTask.FromResult(Apply(_inc) + _epoch);
    }

    public ValueTask<long> IncrementByAndGet(long inc)
    {
        if (_disposed) throw new ObjectDisposedException(_name);
        return ValueTask.FromResult(Apply(inc) + _epoch);
    }

    public ValueTask<long> IncrementTo(long val)
    {
        if (_disposed) throw new ObjectDisposedException(_name);
        using var session = _context.Store.NewSession(new RMWSimpleFunctions<string, long>(
            (input, oldValue) => input > oldValue ? input : oldValue));
        session.RMW(_name, val - _epoch, out var output);
        return ValueTask.FromResult(output + _epoch);
    }

    class RMWSimpleFunctions<Key, Value> : SimpleFunctions<Key, Value>
    {
        public RMWSimpleFunctions(Func<Value, Value, Value> merger) : base(merger) { }

        public override bool InitialUpdater(ref Key key, ref Value input, ref Value value, ref Value output, ref RMWInfo rmwInfo)
        {
            base.InitialUpdater(ref key, ref input, ref value, ref output, ref rmwInfo);
            output = input;
            return true;
        }

        public override bool CopyUpdater(ref Key key, ref Value input, ref Value oldValue, ref Value newValue, ref Value output, ref RMWInfo rmwInfo)
        {
            base.CopyUpdater(ref key, ref input, ref oldValue, ref newValue, ref output, ref rmwInfo);
            output = newValue;
            return true;
        }

        public override bool InPlaceUpdater(ref Key key, ref Value input, ref Value value, ref Value output, ref RMWInfo rmwInfo)
        {
            base.InPlaceUpdater(ref key, ref input, ref value, ref output, ref rmwInfo);
            output = value; 
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _context.DisposeAsync();
    }
}