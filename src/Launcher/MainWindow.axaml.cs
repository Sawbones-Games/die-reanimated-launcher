using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia;
using DieReanimated.Launcher.Ui.ViewModels;

namespace DieReanimated.Launcher.Ui;

public sealed partial class MainWindow : Window
{
    private LauncherViewModel Vm => (LauncherViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            Vm.RequestClose += () => Avalonia.Threading.Dispatcher.UIThread.Post(Close);
            await Vm.RefreshAsync();
        };
        KeyDown += OnKey;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm.PrimaryEnabled) { _ = Vm.PrimaryAsync(); e.Handled = true; }
        else if (e.Key == Key.Escape && Vm.Tab != Tab.Home) { Vm.Tab = Tab.Home; e.Handled = true; }
    }

    // custom chrome: the header is the drag handle
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && (e.Source as Visual)?.FindAncestorOfType<Button>(true) is null) BeginMoveDrag(e);
    }
    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnTabHome(object? sender, RoutedEventArgs e) => Vm.Tab = Tab.Home;
    private void OnTabNews(object? sender, RoutedEventArgs e) => Vm.Tab = Tab.News;
    private void OnTabServers(object? sender, RoutedEventArgs e) => Vm.Tab = Tab.Servers;
    private void OnTabSettings(object? sender, RoutedEventArgs e) => Vm.Tab = Tab.Settings;

    private async void OnPrimary(object? sender, RoutedEventArgs e) => await Vm.PrimaryAsync();
    private void OnSource(object? sender, RoutedEventArgs e) => Vm.OpenSource();
    private void OnDiscord(object? sender, RoutedEventArgs e) => Vm.OpenDiscord();
}
