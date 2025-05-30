using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ProcessorLatencyTool.Models;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

public abstract partial class LatencyTesterBase
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

    [LibraryImport("arm64_registers", EntryPoint = "read_cntvct_el0", SetLastError = true)]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    private static partial ulong ReadCntvctEl0();

    [LibraryImport("arm64_registers", EntryPoint = "read_cntfrq_el0", SetLastError = true)]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("osx")]
    private static partial ulong ReadCntfrqEl0();

    protected abstract void SetThreadAffinity(int core);
    protected abstract void SetThreadPriority();
    protected abstract void SetThreadQoS();

    protected virtual ulong GetCurrentTimer()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            try
            {
                return ReadCntvctEl0();
            }
            catch
            {
                return (ulong)Stopwatch.GetTimestamp();
            }
        }
        return (ulong)Stopwatch.GetTimestamp();
    }

    protected virtual double GetTimerPeriodNs()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            try
            {
                var freq = ReadCntfrqEl0();
                if (freq == 0)
                {
                    return 1_000_000_000.0 / Stopwatch.Frequency;
                }
                return 1_000_000_000.0 / freq;
            }
            catch
            {
                return 1_000_000_000.0 / Stopwatch.Frequency;
            }
        }
        return 1_000_000_000.0 / Stopwatch.Frequency;
    }

    public LatencyResult MeasureLatencyBetweenCores(int coreA, int coreB)
    {
        var barrier = new Barrier(2);
        var ownedByPing = new CacheLineAlignedBool();
        var ownedByPong = new CacheLineAlignedBool();
        var results = new List<double>(NumSamples);

        var pongTask = Task.Run(() =>
        {
            SetThreadAffinity(coreB);
            SetThreadPriority();
            SetThreadQoS();
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
        SetThreadPriority();
        SetThreadQoS();
        barrier.SignalAndWait();

        var value = true;
        for (var sample = 0; sample < NumSamples; sample++)
        {
            var start = GetCurrentTimer();

            for (var trip = 0; trip < NumRoundTrips; trip++)
            {
                while (Volatile.Read(ref ownedByPong.Value) != value) { }
                Volatile.Write(ref ownedByPing.Value, value);
                value = !value;
            }

            var end = GetCurrentTimer();

            var duration = (end - start) * GetTimerPeriodNs();
            results.Add(duration / NumRoundTrips / 2.0); // Divide by 2 for one-way latency
        }

        pongTask.Wait();

        var stats = StatisticsHelper.CalculateStatistics(results);
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

    public static LatencyTesterBase Create()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsLatencyTester();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxLatencyTester();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacOsLatencyTester();
        }

        throw new PlatformNotSupportedException("Current platform is not supported.");
    }
}