using System.Windows;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel branchesViewModel, DevicesViewModel devicesViewModel)
    {
        InitializeComponent();

        BranchesViewControl.DataContext = branchesViewModel;
        DevicesViewControl.DataContext = devicesViewModel;

        Loaded += async (_, _) =>
        {
            await branchesViewModel.InitializeAsync();
            await devicesViewModel.InitializeAsync();
        };
    }
}
