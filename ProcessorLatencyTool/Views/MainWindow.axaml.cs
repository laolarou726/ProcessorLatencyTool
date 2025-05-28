using Avalonia.Controls;
using ProcessorLatencyTool.ViewModels;

namespace ProcessorLatencyTool.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public override void Show()
        {
            base.Show();

            var viewModel = (DataContext as MainWindowViewModel)!;
            var panel = this.FindControl<StackPanel>("MainPanel")!;
            viewModel.SetMainPanel(panel);
        }
    }
}