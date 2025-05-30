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

    [LibraryImport("libc", EntryPoint = "sched_setscheduler", SetLastError = true)]
    private static partial int SchedSetScheduler(int pid, int policy, ref SchedParam param);

    [StructLayout(LayoutKind.Sequential)]
    private struct SchedParam
    {
        public int sched_priority;
    }

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
        var param = new SchedParam { sched_priority = 99 }; // SCHED_FIFO with highest priority
        var result = SchedSetScheduler(0, 1, ref param); // 1 = SCHED_FIFO
        if (result != 0)
        {
            throw new Exception($"Failed to set thread priority: {Marshal.GetLastWin32Error()}");
        }
    }

    protected override void SetThreadQoS()
    {
        // Linux uses SCHED_FIFO for real-time scheduling
        SetThreadPriority();
    }
}