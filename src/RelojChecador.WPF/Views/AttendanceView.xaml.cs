using System.Windows;
using System.Windows.Controls;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

public partial class AttendanceView : UserControl
{
    public AttendanceView()
    {
        InitializeComponent();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AttendanceViewModel viewModel)
        {
            return;
        }

        await viewModel.LoadAsync();
    }
}
