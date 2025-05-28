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

    public static LatencyResult MeasureLatencyBetweenCores(int coreA, int coreB)
    {
        var barrier = new Barrier(2);
        var ownedByPing = new CacheLineAlignedBool();
        var ownedByPong = new CacheLineAlignedBool();
        var results = new List<double>(NumSamples);

        var pongTask = Task.Run(() =>
        {
            SetThreadAffinity(coreB);
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
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
        Thread.CurrentThread.Priority = ThreadPriority.Highest;
        barrier.SignalAndWait();

        var value = true;
        for (var sample = 0; sample < NumSamples; sample++)
        {
            var start = Stopwatch.GetTimestamp();
            for (var trip = 0; trip < NumRoundTrips; trip++)
            {
                while (Volatile.Read(ref ownedByPong.Value) != value) { }
                Volatile.Write(ref ownedByPing.Value, value);
                value = !value;
            }
            var end = Stopwatch.GetTimestamp();
            var duration = (end - start) * (1_000_000_000.0 / Stopwatch.Frequency);
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
        var result = thread_policy_set(
            MachThreadSelf(),
            ThreadAffinityPolicy,
            &core,
            ThreadAffinityPolicyCount);
        if (result != 0)
        {
            throw new Exception($"Failed to set thread affinity: {result}");
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThread")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32.dll", EntryPoint = "SetThreadAffinityMask")]
    private static partial UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    [LibraryImport("libc", EntryPoint = "sched_setaffinity", SetLastError = true)]
    private static partial int SchedSetAffinity(int pid, int cpusetsize, UIntPtr* mask);

    [LibraryImport("libSystem.dylib", EntryPoint = "thread_policy_set")]
    private static partial int thread_policy_set(
        int thread,
        int policy,
        int* policy_info,
        int count);

    private const int ThreadAffinityPolicy = 4;
    private const int ThreadAffinityPolicyCount = 1;

    [LibraryImport("libSystem.dylib", EntryPoint = "mach_thread_self")]
    private static partial int MachThreadSelf();
}