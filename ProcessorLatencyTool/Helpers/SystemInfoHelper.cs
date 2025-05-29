using Microsoft.Win32;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace ProcessorLatencyTool.Helpers;

public static class SystemInfoHelper
{
    public static string GetProcessorName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var processorName = key?.GetValue("ProcessorNameString")?.ToString()?.Trim();

            return processorName ?? "Unknown CPU";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cat",
                    Arguments = "/proc/cpuinfo",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    var modelName = output.Split('\n')
                        .FirstOrDefault(line => line.StartsWith("model name"))
                        ?.Split(':')
                        .LastOrDefault()
                        ?.Trim();
                    return modelName ?? "Unknown CPU";
                }
            }
            catch
            {
                return "Unknown CPU";
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sysctl",
                    Arguments = "-n machdep.cpu.brand_string",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output.Trim();
                }
            }
            catch
            {
                return "Unknown CPU";
            }
        }

        return "Unknown CPU";
    }
}