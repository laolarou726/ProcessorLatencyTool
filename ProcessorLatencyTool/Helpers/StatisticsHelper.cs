using System;
using System.Collections.Generic;
using System.Linq;
using ProcessorLatencyTool.Models;

namespace ProcessorLatencyTool.Helpers;

public static class StatisticsHelper
{
    public static Statistics CalculateStatistics(List<double> measurements)
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
}