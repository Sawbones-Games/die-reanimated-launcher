using Avalonia;
using Avalonia.Controls;

namespace DieReanimated.Launcher.Ui.Views;

public sealed partial class SectionTitle : UserControl
{
    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<SectionTitle, string>(nameof(Text), "");
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public SectionTitle() => InitializeComponent();
}
