using System.Windows;
using ForgeDesk.Presentation.ViewModels;

namespace ForgeDesk.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
