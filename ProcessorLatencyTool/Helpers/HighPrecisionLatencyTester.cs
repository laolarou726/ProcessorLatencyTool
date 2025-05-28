using System.Threading;
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace ProcessorLatencyTool.Helpers;

public static unsafe partial class HighPrecisionLatencyTester
{
    private const int CacheLineSize = 64;
    private const int NumSamples = 1000;
    private const int NumRoundTrips = 1000;

    [StructLayout(LayoutKind.Explicit, Size = CacheLineSize)]
    private struct CacheLineAlignedBool
    {
        [FieldOffset(0)]
        public bool Value;
    }

    // macOS specific constants
    private const int QOS_CLASS_USER_INTERACTIVE = 0x21;
    private const int THREAD_AFFINITY_POLICY = 4;
    private const int THREAD_AFFINITY_POLICY_COUNT = 1;

    // macOS thread affinity policy structure
    [StructLayout(LayoutKind.Sequential)]
    private struct thread_affinity_policy_data_t
    {
        public int affinity_tag;
    }

    // macOS processor set APIs
    [LibraryImport("libSystem.dylib", EntryPoint = "processor_set_default")]
    private static partial int processor_set_default(
        int host,
        ref int pset);

    [LibraryImport("libSystem.dylib", EntryPoint = "host_processor_set_priv")]
    private static partial int host_processor_set_priv(
        int host,
        int pset,
        ref int pset_priv);

    [LibraryImport("libSystem.dylib", EntryPoint = "host_self")]
    private static partial int host_self();

    [LibraryImport("libSystem.dylib", EntryPoint = "thread_assign")]
    private static partial int thread_assign(
        int thread,
        int pset);

    // macOS specific P/Invoke declarations
    [LibraryImport("libSystem.dylib", EntryPoint = "pthread_set_qos_class_self_np")]
    private static partial int pthread_set_qos_class_self_np(int qos_class, int relative_priority);

    [LibraryImport("libSystem.dylib", EntryPoint = "thread_policy_set")]
    private static partial int thread_policy_set(
        int thread,
        int policy,
        thread_affinity_policy_data_t* policy_info,
        int count);

    [LibraryImport("libSystem.dylib", EntryPoint = "mach_thread_self")]
    private static partial int MachThreadSelf();

    // Native ARM64 register access
    [LibraryImport("arm64_registers", EntryPoint = "read_tpidr_el0")]
    private static partial ulong ReadTpidrEl0();

    [LibraryImport("arm64_registers", EntryPoint = "read_cntvct_el0")]
    private static partial ulong ReadCntvctEl0();

    [LibraryImport("arm64_registers", EntryPoint = "read_cntfrq_el0")]
    private static partial ulong ReadCntfrqEl0();

    private static ulong GetCurrentCore()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && 
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return ReadTpidrEl0();
        }
        return 0;
    }

    private static ulong GetCurrentTimer()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && 
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return ReadCntvctEl0();
        }
        return (ulong)Stopwatch.GetTimestamp();
    }

    private static double GetTimerPeriodNs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && 
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            var freq = ReadCntfrqEl0();
            return 1.0 / freq * 1_000_000_000.0;
        }
        return 1_000_000_000.0 / Stopwatch.Frequency;
    }

    public static LatencyResult MeasureLatencyBetweenCores(int coreA, int coreB)
    {
        var barrier = new Barrier(2);
        var ownedByPing = new CacheLineAlignedBool();
        var ownedByPong = new CacheLineAlignedBool();
        var results = new List<double>(NumSamples);

        var pongTask = Task.Run(() =>
        {
            SetThreadAffinity(coreB);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                pthread_set_qos_class_self_np(QOS_CLASS_USER_INTERACTIVE, 0);
            }
            else
            {
                Thread.CurrentThread.Priority = ThreadPriority.Highest;
            }
            barrier.SignalAndWait();

            var value = false;
            for (var i = 0; i < NumRoundTrips * NumSamples; i++)
            {
                while (Volatile.Read(ref ownedByPing.Value) != value) { }
                Volatile.Write(ref ownedByPong.Value, !value);
                value = !value;
            }
        });

        SetThreadAffinity(coreA);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            pthread_set_qos_class_self_np(QOS_CLASS_USER_INTERACTIVE, 0);
        }
        else
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        barrier.SignalAndWait();

        var value = true;
        for (var sample = 0; sample < NumSamples; sample++)
        {
            var start = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 
                GetCurrentTimer() : (ulong)Stopwatch.GetTimestamp();
            
            for (var trip = 0; trip < NumRoundTrips; trip++)
            {
                while (Volatile.Read(ref ownedByPong.Value) != value) { }
                Volatile.Write(ref ownedByPing.Value, value);
                value = !value;
            }
            
            var end = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 
                GetCurrentTimer() : (ulong)Stopwatch.GetTimestamp();
                
            var duration = (end - start) * GetTimerPeriodNs();
            results.Add(duration / NumRoundTrips / 2.0); // Divide by 2 for one-way latency
        }

        pongTask.Wait();

        var stats = CalculateStatistics(results);
        return new LatencyResult
        {
            MeanLatency = stats.Mean,
            StandardDeviation = stats.StandardDeviation,
            MinLatency = stats.Min,
            MaxLatency = stats.Max,
            SampleCount = stats.SampleCount,
            CoreA = coreA,
            CoreB = coreB
        };
    }

    private static Statistics CalculateStatistics(List<double> measurements)
    {
        var mean = measurements.Average();
        var stdDev = Math.Sqrt(measurements.Average(x => Math.Pow(x - mean, 2)));

        var filteredMeasurements = measurements
            .Where(x => Math.Abs(x - mean) <= 1.5 * stdDev)
            .ToList();

        return new Statistics
        {
            Mean = filteredMeasurements.Average(),
            StandardDeviation = Math.Sqrt(filteredMeasurements.Average(x => Math.Pow(x - mean, 2))),
            Min = filteredMeasurements.Min(),
            Max = filteredMeasurements.Max(),
            SampleCount = filteredMeasurements.Count
        };
    }

    private struct Statistics
    {
        public double Mean;
        public double StandardDeviation;
        public double Min;
        public double Max;
        public int SampleCount;
    }

    public struct LatencyResult
    {
        public double MeanLatency;
        public double StandardDeviation;
        public double MinLatency;
        public double MaxLatency;
        public int SampleCount;
        public int CoreA;
        public int CoreB;
    }

    private static void SetThreadAffinity(int core)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetThreadAffinityWindows(core);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            SetThreadAffinityLinux(core);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetThreadAffinityMacOs(core);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetThreadAffinityWindows(int core)
    {
        var mask = new UIntPtr(1UL << core);
        var handle = GetCurrentThread();
        var result = SetThreadAffinityMask(handle, mask);
        if (result == UIntPtr.Zero)
        {
            throw new Exception($"Failed to set thread affinity: {Marshal.GetLastWin32Error()}");
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetThreadAffinityLinux(int core)
    {
        var mask = new UIntPtr(1UL << core);
        var result = SchedSetAffinity(0, sizeof(ulong), &mask);
        if (result != 0)
        {
            throw new Exception($"Failed to set thread affinity: {Marshal.GetLastWin32Error()}");
        }
    }

    [SupportedOSPlatform("osx")]
    private static void SetThreadAffinityMacOs(int core)
    {
        try
        {
            var thread = MachThreadSelf();
            var host = host_self();
            var pset = 0;
            var pset_priv = 0;

            // Get the default processor set
            var result = processor_set_default(host, ref pset);
            if (result != 0)
            {
                // If we can't get processor set access, try a simpler approach
                var policy = new thread_affinity_policy_data_t { affinity_tag = core };
                result = thread_policy_set(
                    thread,
                    THREAD_AFFINITY_POLICY,
                    &policy,
                    THREAD_AFFINITY_POLICY_COUNT);
                if (result != 0)
                {
                    Console.WriteLine($"Warning: Could not set thread affinity. The application may need to be run with sudo for accurate measurements.");
                    return; // Continue without affinity rather than throwing
                }
                return;
            }

            // Get privileged access to the processor set
            result = host_processor_set_priv(host, pset, ref pset_priv);
            if (result != 0)
            {
                // Fall back to simple affinity policy
                var policy = new thread_affinity_policy_data_t { affinity_tag = core };
                result = thread_policy_set(
                    thread,
                    THREAD_AFFINITY_POLICY,
                    &policy,
                    THREAD_AFFINITY_POLICY_COUNT);
                if (result != 0)
                {
                    Console.WriteLine($"Warning: Could not set thread affinity. The application may need to be run with sudo for accurate measurements.");
                    return; // Continue without affinity rather than throwing
                }
                return;
            }

            // Assign thread to processor set
            result = thread_assign(thread, pset_priv);
            if (result != 0)
            {
                // Fall back to simple affinity policy
                var policy = new thread_affinity_policy_data_t { affinity_tag = core };
                result = thread_policy_set(
                    thread,
                    THREAD_AFFINITY_POLICY,
                    &policy,
                    THREAD_AFFINITY_POLICY_COUNT);
                if (result != 0)
                {
                    Console.WriteLine($"Warning: Could not set thread affinity. The application may need to be run with sudo for accurate measurements.");
                    return; // Continue without affinity rather than throwing
                }
                return;
            }

            // Set thread affinity policy
            var finalPolicy = new thread_affinity_policy_data_t { affinity_tag = core };
            result = thread_policy_set(
                thread,
                THREAD_AFFINITY_POLICY,
                &finalPolicy,
                THREAD_AFFINITY_POLICY_COUNT);
            if (result != 0)
            {
                Console.WriteLine($"Warning: Could not set thread affinity. The application may need to be run with sudo for accurate measurements.");
                return; // Continue without affinity rather than throwing
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error setting thread affinity: {ex.Message}");
            // Continue without affinity rather than throwing
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThread")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32.dll", EntryPoint = "SetThreadAffinityMask")]
    private static partial UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    [LibraryImport("libc", EntryPoint = "sched_setaffinity", SetLastError = true)]
    private static partial int SchedSetAffinity(int pid, int cpusetsize, UIntPtr* mask);
}