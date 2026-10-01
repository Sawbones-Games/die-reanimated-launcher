using Avalonia.Controls;
using Avalonia.Interactivity;
using DieReanimated.Launcher.Ui.ViewModels;

namespace DieReanimated.Launcher.Ui.Views;

public sealed partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();
    private LauncherViewModel? Vm => DataContext as LauncherViewModel;

    private void OnReadNotes(object? sender, RoutedEventArgs e) { if (Vm?.Headline is { } h) Vm.OpenNews(h); }
    private void OnDiscord(object? sender, RoutedEventArgs e) => Vm?.OpenDiscord();
}
