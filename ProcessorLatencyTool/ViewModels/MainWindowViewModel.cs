using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Runtime.InteropServices;
using System;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using ProcessorLatencyTool.Helpers;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace ProcessorLatencyTool.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public static string CpuModel
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    var processorName = key?.GetValue("ProcessorNameString")?.ToString()?.Trim();

                    return processorName ?? "Unknown CPU";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
                        // Ignore
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
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
                        // Ignore
                    }
                }

                return "Unknown CPU";
            }
        }

        public static string CoreThreadInfo => $"Cores: {Environment.ProcessorCount}";

        [ObservableProperty] private string _progressText = "Testing latency...";
        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private double _progressValue;
        [ObservableProperty] private string _progressPercentage = "0%";

        private StackPanel? _mainPanel;

        public void SetMainPanel(StackPanel panel)
        {
            _mainPanel = panel;
            DisplayInitialGrid();
        }

        private void DisplayInitialGrid()
        {
            if (_mainPanel == null) return;

            var coreCount = Environment.ProcessorCount;
            var matrix = new HighPrecisionLatencyTester.LatencyResult[coreCount][];

            for (var i = 0; i < coreCount; i++)
            {
                matrix[i] = new HighPrecisionLatencyTester.LatencyResult[coreCount];
                for (var j = 0; j < coreCount; j++)
                {
                    matrix[i][j] = new HighPrecisionLatencyTester.LatencyResult
                    {
                        MeanLatency = 0,
                        StandardDeviation = 0,
                        MinLatency = 0,
                        MaxLatency = 0,
                        SampleCount = 0,
                        CoreA = i,
                        CoreB = j
                    };
                }
            }

            BuildLatencyGrid(matrix, _mainPanel, true);
        }

        [RelayCommand]
        private async Task StartMeasurementAsync()
        {
            StatusText = "Starting measurements...";
            ProgressValue = 0;
            ProgressPercentage = "0%";

            var progress = new Progress<(int, int)>(onProgress =>
            {
                var (i, j) = onProgress;
                var coreCount = Environment.ProcessorCount;
                var totalTests = coreCount * coreCount - coreCount; // Exclude self-tests
                var currentTest = i * coreCount + j - i; // Adjust for skipped self-tests
                var percentage = currentTest * 100.0 / totalTests;

                Dispatcher.UIThread.Post(() =>
                {
                    ProgressText = $"Measuring latency: Core {i} → Core {j}...";
                    ProgressValue = percentage;
                    ProgressPercentage = $"{percentage:F1}%";
                });
            });

            var matrix = await MeasureLatencyMatrixAsync(progress);

            Dispatcher.UIThread.Post(() =>
            {
                if (_mainPanel != null)
                {
                    BuildLatencyGrid(matrix, _mainPanel);
                }

                StatusText = "Measurements completed";
            });
        }

        private static void BuildLatencyGrid(HighPrecisionLatencyTester.LatencyResult[][] matrix, StackPanel panel, bool isInit = false)
        {
            var grid = new Grid
            {
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var maxLatency = isInit
                ? 0
                : matrix
                    .SelectMany(row => row)
                    .Where(x => x.MeanLatency > 0)
                    .Max(x => x.MeanLatency);

            // Add column and row definitions
            for (var i = 0; i <= matrix.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            }

            // Add header row and column
            for (var i = 0; i < matrix.Length; i++)
            {
                var tb1 = new TextBlock
                {
                    Text = $"Core {i}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(5)
                };

                Grid.SetColumn(tb1, 0);
                Grid.SetRow(tb1, i + 1);

                grid.Children.Add(tb1);

                var tb2 = new TextBlock
                {
                    Text = $"Core {i}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.Bold,
                    Margin = new Thickness(5)
                };

                Grid.SetColumn(tb2, i + 1);
                Grid.SetRow(tb2, 0);

                grid.Children.Add(tb2);

                for (var j = 0; j < matrix.Length; j++)
                {
                    var result = matrix[i][j];
                    var border = new Border
                    {
                        Background = ColorHelper.GetLatencyColorBrush(result.MeanLatency, maxLatency),
                        Child = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(5),
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = $"{result.MeanLatency:0.0} ns",
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    Foreground = Brushes.White,
                                    FontWeight = FontWeight.Bold
                                },
                                new TextBlock
                                {
                                    Text = $"±{result.StandardDeviation:0.0}",
                                    HorizontalAlignment = HorizontalAlignment.Center,
                                    Foreground = Brushes.White,
                                    FontSize = 10
                                }
                            }
                        }
                    };

                    Grid.SetColumn(border, j + 1);
                    Grid.SetRow(border, i + 1);

                    grid.Children.Add(border);
                }
            }

            panel.Children.Clear();
            panel.Children.Add(grid);
        }

        private async Task<HighPrecisionLatencyTester.LatencyResult[][]> MeasureLatencyMatrixAsync(
            IProgress<(int i, int j)>? progress = null)
        {
            var coreCount = Environment.ProcessorCount;
            var matrix = new HighPrecisionLatencyTester.LatencyResult[coreCount][];

            for (var i = 0; i < coreCount; i++)
                matrix[i] = new HighPrecisionLatencyTester.LatencyResult[coreCount];

            for (var i = 0; i < coreCount; i++)
            {
                for (var j = 0; j < coreCount; j++)
                {
                    if (i == j)
                    {
                        matrix[i][j] = new HighPrecisionLatencyTester.LatencyResult
                        {
                            MeanLatency = 0,
                            StandardDeviation = 0,
                            MinLatency = 0,
                            MaxLatency = 0,
                            SampleCount = 0,
                            CoreA = i,
                            CoreB = j
                        };
                        continue;
                    }

                    progress?.Report((i, j));
                    try
                    {
                        matrix[i][j] = await Task.Run(() =>
                            HighPrecisionLatencyTester.MeasureLatencyBetweenCores(i, j)
                        );

                        // Update the grid after each measurement
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (_mainPanel != null)
                            {
                                BuildLatencyGrid(matrix, _mainPanel);
                            }
                        });
                    }
                    catch (Exception)
                    {
                        matrix[i][j] = new HighPrecisionLatencyTester.LatencyResult
                        {
                            MeanLatency = -1,
                            StandardDeviation = 0,
                            MinLatency = 0,
                            MaxLatency = 0,
                            SampleCount = 0,
                            CoreA = i,
                            CoreB = j
                        };
                    }
                }
            }

            return matrix;
        }
    }
}
