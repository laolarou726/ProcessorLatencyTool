using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsLatencyTester : LatencyTesterBase
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThread")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32.dll", EntryPoint = "SetThreadAffinityMask")]
    private static partial UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    protected override void SetThreadAffinity(int core)
    {
        var mask = new UIntPtr(1UL << core);
        var handle = GetCurrentThread();
        var result = SetThreadAffinityMask(handle, mask);
        if (result == UIntPtr.Zero)
        {
            throw new Exception($"Failed to set thread affinity: {Marshal.GetLastWin32Error()}");
        }
    }

    protected override void SetThreadPriority()
    {
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
    }

    protected override ulong GetCurrentTimer()
    {
        return (ulong)Stopwatch.GetTimestamp();
    }

    protected override double GetTimerPeriodNs()
    {
        return 1_000_000_000.0 / Stopwatch.Frequency;
    }
} 