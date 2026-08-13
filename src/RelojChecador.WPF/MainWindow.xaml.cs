using System.Windows;
using RelojChecador.WPF.ViewModels;
using RelojChecador.WPF.Views;

namespace RelojChecador.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnAddBranchClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddBranchDialog { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var error = await ViewModel.CreateBranchAsync(
            dialog.Code, dialog.BranchName, dialog.TimeZoneId, dialog.LegalEntityName, dialog.Address);

        if (error is not null)
        {
            MessageBox.Show(this, error, "No se pudo crear la sucursal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
