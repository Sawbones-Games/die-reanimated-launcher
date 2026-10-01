using Avalonia.Controls;
using Avalonia.Interactivity;
using DieReanimated.Launcher.Ui.ViewModels;

namespace DieReanimated.Launcher.Ui.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
    private LauncherViewModel? Vm => DataContext as LauncherViewModel;

    private void OnBorderless(object? sender, RoutedEventArgs e) => Vm?.SetWindowMode(GameProcess.WindowMode.Borderless);
    private void OnFullscreen(object? sender, RoutedEventArgs e) => Vm?.SetWindowMode(GameProcess.WindowMode.Fullscreen);
    private void OnWindowed(object? sender, RoutedEventArgs e) => Vm?.SetWindowMode(GameProcess.WindowMode.Windowed);
    private async void OnReapply(object? sender, RoutedEventArgs e) { if (Vm != null) await Vm.ReapplyAsync(); }
    private async void OnRestore(object? sender, RoutedEventArgs e) { if (Vm != null) await Vm.RestoreAsync(); }
    private void OnOpenLog(object? sender, RoutedEventArgs e) => Vm?.OpenLog();
    private void OnSource(object? sender, RoutedEventArgs e) => Vm?.OpenSource();
    private void OnDiscord(object? sender, RoutedEventArgs e) => Vm?.OpenDiscord();
}
