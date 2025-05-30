using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

[SupportedOSPlatform("windows")]
public sealed partial class WindowsLatencyTester : LatencyTesterBase
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThread")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32.dll", EntryPoint = "SetThreadAffinityMask")]
    private static partial UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    [LibraryImport("kernel32.dll", EntryPoint = "SetThreadPriority")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadPriority(IntPtr hThread, int nPriority);

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
        var handle = GetCurrentThread();
        if (!SetThreadPriority(handle, 15)) // THREAD_PRIORITY_TIME_CRITICAL
        {
            throw new Exception($"Failed to set thread priority: {Marshal.GetLastWin32Error()}");
        }
    }

    protected override void SetThreadQoS()
    {
        // Windows doesn't have a direct equivalent to QoS classes
        // We use the highest thread priority instead
        SetThreadPriority();
    }
}