using System;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace ProcessorLatencyTool.Helpers;

public static class ColorHelper
{
    private static readonly Color[] ColorPresets =
    [
        Color.FromRgb(68, 41, 90), // Dark purple
        Color.FromRgb(65, 62, 133), // Purple
        Color.FromRgb(48, 104, 141), // Blue
        Color.FromRgb(31, 146, 139), // Teal
        Color.FromRgb(53, 183, 119), // Green
        Color.FromRgb(145, 213, 66), // Light green
        Color.FromRgb(248, 230, 32) // Yellow
    ];

    public static ImmutableSolidColorBrush GetLatencyColorBrush(double latency, double maxLatency)
    {
        if (latency < 0) return new ImmutableSolidColorBrush(Colors.Gray);

        var normalized = Math.Clamp(Math.Log10(latency + 1) / Math.Log10(maxLatency + 1), 0, 1);
        var scaled = normalized * (ColorPresets.Length - 1);
        var index = (int)scaled;
        var t = scaled - index;

        if (index >= ColorPresets.Length - 1)
            return new ImmutableSolidColorBrush(ColorPresets[^1]);

        var c1 = ColorPresets[index];
        var c2 = ColorPresets[index + 1];

        var r = (byte)(c1.R + (c2.R - c1.R) * t);
        var g = (byte)(c1.G + (c2.G - c1.G) * t);
        var b = (byte)(c1.B + (c2.B - c1.B) * t);

        return new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
    }
}