using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// Something read in the province, or the journal of all of them: a title, where, and the words, on old paper.
/// </summary>
public sealed class ReadingWindow : Window
{
    public ReadingWindow(string title, IReadOnlyList<(string Heading, string Where, string Text)> pages)
    {
        Title = title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(222, 206, 168));
        var ink = new SolidColorBrush(Color.FromRgb(52, 34, 24));
        var page = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        page.Children.Add(new TextBlock { Text = title, FontSize = 22, Foreground = ink });
        if (pages.Count == 0)
            page.Children.Add(new TextBlock { Text = "Nothing yet. What the province has to say, you will find out there.", Foreground = ink, TextWrapping = TextWrapping.Wrap });
        foreach (var (heading, where, text) in pages)
        {
            page.Children.Add(new TextBlock { Text = $"{heading}  ·  {where}", FontWeight = FontWeight.Bold, Foreground = ink, Margin = new Thickness(0, 6, 0, 0) });
            page.Children.Add(new TextBlock { Text = text, Foreground = ink, TextWrapping = TextWrapping.Wrap });
        }
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };
        close.Click += (_, _) => Close();
        page.Children.Add(close);
        Content = new ScrollViewer { MaxHeight = 720, Content = page };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter)
            {
                Close();
                e.Handled = true;
            }
        };
        Pages = pages;
    }

    public IReadOnlyList<(string Heading, string Where, string Text)> Pages { get; }
}
