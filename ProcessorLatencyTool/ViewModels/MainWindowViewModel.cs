using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using CommunityToolkit.Mvvm.Input;
using System.Linq;
using ProcessorLatencyTool.Helpers;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ProcessorLatencyTool.Helpers.LatencyTester;
using ProcessorLatencyTool.Models;
using System.IO;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace ProcessorLatencyTool.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public static string CpuModel => SystemInfoHelper.GetProcessorName();

        public static string CoreThreadInfo => $"Cores: {Environment.ProcessorCount}";

        [ObservableProperty] public partial string ProgressText { get; private set; } = "Testing latency...";
        [ObservableProperty] public partial string StatusText { get; private set; } = "Ready";
        [ObservableProperty] public partial double ProgressValue { get; private set; }
        [ObservableProperty] public partial string ProgressPercentage { get; private set; } = "0%";
        [ObservableProperty] public partial bool HasResults { get; private set; }

        private StackPanel? _mainPanel;
        private LatencyResult[][]? _currentMatrix;

        public void SetMainPanel(StackPanel panel)
        {
            _mainPanel = panel;
            DisplayInitialGrid();
        }

        private void DisplayInitialGrid()
        {
            if (_mainPanel == null) return;

            var coreCount = Environment.ProcessorCount;
            var matrix = new LatencyResult[coreCount][];

            for (var i = 0; i < coreCount; i++)
            {
                matrix[i] = new LatencyResult[coreCount];
                for (var j = 0; j < coreCount; j++)
                {
                    matrix[i][j] = new LatencyResult
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

            _currentMatrix = matrix;
            HasResults = false;
            BuildLatencyGrid(matrix, _mainPanel, true);
        }

        [RelayCommand]
        private async Task StartMeasurementAsync()
        {
            StatusText = "Starting measurements...";
            ProgressValue = 0;
            ProgressPercentage = "0%";
            HasResults = false;

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
                HasResults = true;
            });
        }

        [RelayCommand]
        private async Task ExportToCsvAsync()
        {
            if (_currentMatrix == null)
            {
                StatusText = "No data to export";
                return;
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                StatusText = "Cannot access application window";
                return;
            }

            var mainWindow = desktop.MainWindow;
            if (mainWindow == null)
            {
                StatusText = "Cannot access main window";
                return;
            }

            var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Latency Data",
                DefaultExtension = "csv",
                FileTypeChoices =
                [
                    new FilePickerFileType("CSV Files")
                    {
                        Patterns = ["*.csv"]
                    }
                ]
            });

            if (file == null)
                return;

            try
            {
                var csv = new StringBuilder();
                var coreCount = _currentMatrix.Length;

                // Add header row
                csv.AppendLine("Source Core,Target Core,Mean Latency (ns),Standard Deviation (ns),Min Latency (ns),Max Latency (ns),Sample Count");

                // Add data rows
                for (var i = 0; i < coreCount; i++)
                {
                    for (var j = 0; j < coreCount; j++)
                    {
                        var result = _currentMatrix[i][j];
                        csv.AppendLine($"{i},{j},{result.MeanLatency:F2},{result.StandardDeviation:F2},{result.MinLatency:F2},{result.MaxLatency:F2},{result.SampleCount}");
                    }
                }

                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(csv.ToString());

                StatusText = "Data exported successfully";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        private static void BuildLatencyGrid(LatencyResult[][] matrix, StackPanel panel, bool isInit = false)
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

        private async Task<LatencyResult[][]> MeasureLatencyMatrixAsync(
            IProgress<(int i, int j)>? progress = null)
        {
            var coreCount = Environment.ProcessorCount;
            var matrix = new LatencyResult[coreCount][];
            var latencyTester = LatencyTesterBase.Create();

            for (var i = 0; i < coreCount; i++)
                matrix[i] = new LatencyResult[coreCount];

            for (var i = 0; i < coreCount; i++)
            {
                for (var j = 0; j < coreCount; j++)
                {
                    if (i == j)
                    {
                        matrix[i][j] = new LatencyResult
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
                            latencyTester.MeasureLatencyBetweenCores(i, j)
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
                        matrix[i][j] = new LatencyResult
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

            _currentMatrix = matrix;
            return matrix;
        }
    }
}
