using Avalonia.Controls;
using Avalonia.Interactivity;
using DieReanimated.Launcher.Ui.ViewModels;

namespace DieReanimated.Launcher.Ui.Views;

public sealed partial class HomeTiles : UserControl
{
    public HomeTiles() => InitializeComponent();
    private LauncherViewModel? Vm => DataContext as LauncherViewModel;

    private void OnTile(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && (sender as Button)?.DataContext is LauncherViewModel.NewsVm item) Vm.OpenNews(item);
    }
    private void OnLive(object? sender, RoutedEventArgs e) { if (Vm != null) Vm.Tab = Tab.Servers; }
}
