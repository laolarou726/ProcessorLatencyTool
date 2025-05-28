using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ProcessorLatencyTool.Models;

namespace ProcessorLatencyTool.Helpers.LatencyTester;

public abstract class LatencyTesterBase
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

    protected abstract void SetThreadAffinity(int core);
    protected abstract void SetThreadPriority();
    protected abstract ulong GetCurrentTimer();
    protected abstract double GetTimerPeriodNs();

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