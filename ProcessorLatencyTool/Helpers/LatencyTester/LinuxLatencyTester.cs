using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

[SupportedOSPlatform("linux")]
public sealed unsafe partial class LinuxLatencyTester : LatencyTesterBase
{
    [LibraryImport("libc", EntryPoint = "sched_setaffinity", SetLastError = true)]
    private static partial int SchedSetAffinity(int pid, int cpusetsize, UIntPtr* mask);

    protected override void SetThreadAffinity(int core)
    {
        var mask = new UIntPtr(1UL << core);
        var result = SchedSetAffinity(0, sizeof(ulong), &mask);
        if (result != 0)
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