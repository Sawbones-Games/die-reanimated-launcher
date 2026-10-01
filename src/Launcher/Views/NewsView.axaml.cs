using Avalonia.Controls;
using Avalonia.Interactivity;
using DieReanimated.Launcher.Ui.ViewModels;

namespace DieReanimated.Launcher.Ui.Views;

public sealed partial class NewsView : UserControl
{
    public NewsView() => InitializeComponent();
    private LauncherViewModel? Vm => DataContext as LauncherViewModel;

    private void OnPick(object? sender, RoutedEventArgs e)
    {
        if (Vm != null && (sender as Button)?.DataContext is LauncherViewModel.NewsVm item) Vm.SelectedNews = item;
    }
    private void OnNewer(object? sender, RoutedEventArgs e) => Vm?.Newer();
    private void OnOlder(object? sender, RoutedEventArgs e) => Vm?.Older();
}
