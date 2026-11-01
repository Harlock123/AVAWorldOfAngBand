using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// Leaving the game (Ctrl+Q): asked first, since there is no saving yet and the hero below is lost. Enter or Y leaves,
/// Escape or N stays.
/// </summary>
public sealed class QuitWindow : Window
{
    public QuitWindow(bool heroAlive, Action quit)
    {
        Title = "Leave the game?";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 18, 15));
        var leave = new Button { Content = "Leave", MinWidth = 100, IsDefault = true };
        leave.Click += (_, _) => Confirm(quit);
        var stay = new Button { Content = "Stay", MinWidth = 100, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        stay.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Leave the game?", FontSize = 20, Foreground = Brushes.BurlyWood },
                new TextBlock
                {
                    Text = heroAlive
                        ? "There is no saving yet: the Lantern-sworn below is lost, and their line is not written in the book."
                        : "The book is written. Go?",
                    TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Wheat,
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { leave, stay } },
            },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Y or Key.Enter)
            {
                Confirm(quit);
                e.Handled = true;
            }
            else if (e.Key is Key.N or Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    /// <summary>Leaves the game (tests call this).</summary>
    public void Confirm(Action quit)
    {
        Close();
        quit();
    }
}
