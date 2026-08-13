using System.Windows;
using System.Windows.Controls;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

public partial class BranchesView : UserControl
{
    public BranchesView()
    {
        InitializeComponent();
    }

    private async void OnAddBranchClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new AddBranchDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var error = await viewModel.CreateBranchAsync(
            dialog.Code, dialog.BranchName, dialog.TimeZoneId, dialog.LegalEntityName, dialog.Address);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo crear la sucursal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
