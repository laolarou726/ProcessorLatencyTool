using System;
using System.Threading;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

public class AtomicBoolean
{
    private int _value;

    public AtomicBoolean(bool initialValue = false)
    {
        _value = initialValue ? 1 : 0;
    }

    public bool Value
    {
        get => Interlocked.CompareExchange(ref _value, 0, 0) != 0;
        set => Interlocked.Exchange(ref _value, value ? 1 : 0);
    }

    public bool CompareExchange(ref bool comparand, bool value)
    {
        int expected = comparand ? 1 : 0;
        int desired = value ? 1 : 0;
        int original = Interlocked.CompareExchange(ref _value, desired, expected);
        comparand = original != 0;
        return original == expected;
    }
}