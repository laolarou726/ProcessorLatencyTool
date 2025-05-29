using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

[SupportedOSPlatform("osx")]
public sealed unsafe partial class MacOsLatencyTester : LatencyTesterBase
{
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_self")]
    private static partial IntPtr pthread_self();

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_getname_np")]
    private static partial int pthread_getname_np(IntPtr thread, byte* name, nuint len);

    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_setname_np")]
    private static partial int pthread_setname_np(byte* name);

    [LibraryImport("arm64_registers", EntryPoint = "set_realtime_policy")]
    private static partial int set_realtime_policy();

    [LibraryImport("arm64_registers", EntryPoint = "get_thread_policy")]
    private static partial int get_thread_policy(out int is_realtime, out int importance);

    [LibraryImport("arm64_registers", EntryPoint = "read_tpidr_el0")]
    private static partial ulong ReadTpidrEl0();

    [LibraryImport("arm64_registers", EntryPoint = "read_cntvct_el0")]
    private static partial ulong ReadCntvctEl0();

    [LibraryImport("arm64_registers", EntryPoint = "read_cntfrq_el0")]
    private static partial ulong ReadCntfrqEl0();

    private void SetThreadName(string name)
    {
        // Max thread name length in MacOS is 64 bytes
        var threadNameBytes = new byte[64];
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        Array.Copy(nameBytes, threadNameBytes, Math.Min(nameBytes.Length, 63));
        
        fixed (byte* namePtr = threadNameBytes)
        {
            pthread_setname_np(namePtr);
        }
    }

    protected override void SetThreadAffinity(int core)
    {
        try
        {
            // Set thread name for debugging
            SetThreadName($"LatencyTest_Core{core}");

            // Get current thread policy
            int isRealtime, importance;
            var result = get_thread_policy(out isRealtime, out importance);
            if (result == 0)
            {
                Console.WriteLine($"Current thread policy - Realtime: {isRealtime}, Importance: {importance}");
            }

            // Try to set realtime policy
            result = set_realtime_policy();
            if (result != 0)
            {
                switch (result)
                {
                    case -1:
                        Console.WriteLine("Warning: Could not get thread port");
                        break;
                    case -2:
                        Console.WriteLine("Warning: Could not set extended policy");
                        break;
                    case -3:
                        Console.WriteLine("Warning: Could not set precedence policy");
                        break;
                    default:
                        Console.WriteLine($"Warning: Unknown error setting thread policy ({result})");
                        break;
                }
                Console.WriteLine("Warning: Failed to set realtime policy. Performance may be affected.");
            }
            else
            {
                // Verify the change
                result = get_thread_policy(out isRealtime, out importance);
                if (result == 0)
                {
                    Console.WriteLine($"Thread policy set - Realtime: {isRealtime}, Importance: {importance}");
                }
            }

            // Get current thread info for logging
            var threadId = pthread_self();
            var threadName = new byte[64];
            fixed (byte* namePtr = threadName)
            {
                pthread_getname_np(threadId, namePtr, 64);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error setting thread parameters: {ex.Message}");
        }
    }

    protected override void SetThreadPriority()
    {
        // This is handled in SetThreadAffinity
    }

    protected override ulong GetCurrentTimer()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return ReadCntvctEl0();
        }
        return (ulong)Stopwatch.GetTimestamp();
    }

    protected override double GetTimerPeriodNs()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            var freq = ReadCntfrqEl0();
            return 1.0 / freq * 1_000_000_000.0;
        }
        return 1_000_000_000.0 / Stopwatch.Frequency;
    }
}