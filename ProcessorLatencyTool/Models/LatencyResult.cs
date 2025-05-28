namespace ProcessorLatencyTool.Models;

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