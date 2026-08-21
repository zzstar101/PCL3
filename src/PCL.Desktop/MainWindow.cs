using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PCL3.Platform;

namespace PCL3.Desktop;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        var platform = PlatformTarget.Current;

        Title = "PCL3";
        Width = 960;
        Height = 640;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = new StackPanel
        {
            Margin = new Thickness(48),
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new TextBlock
                {
                    Text = "PCL3",
                    FontSize = 36,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = "Cross-platform launcher core bootstrap",
                    FontSize = 18
                },
                new Border
                {
                    Margin = new Thickness(0, 16, 0, 0),
                    Padding = new Thickness(16),
                    CornerRadius = new CornerRadius(12),
                    Child = new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Runtime target",
                                FontWeight = FontWeight.SemiBold
                            },
                            new TextBlock
                            {
                                Text = platform.ToString()
                            }
                        }
                    }
                },
                new TextBlock
                {
                    Margin = new Thickness(0, 16, 0, 0),
                    Text = "Phase 1 keeps Minecraft and platform logic outside the UI. Native acceleration will remain optional.",
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }
}
