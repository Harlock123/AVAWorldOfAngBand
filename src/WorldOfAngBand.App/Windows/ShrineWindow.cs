using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Province;

namespace WorldOfAngBand.App.Windows;

/// <summary>A shrine (one of Ashur's old waystones): the three blessings it offers; one is taken, and the shrine goes cold.</summary>
public sealed class ShrineWindow : Window
{
    public ShrineWindow(ShrineEvent shrine, Func<GameCommand, bool> perform)
    {
        Shrine = shrine;
        Title = shrine.Name;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(18, 20, 28));
        var buttons = new StackPanel { Spacing = 8 };
        foreach (var blessing in shrine.Blessings)
        {
            var b = blessing;
            var button = new Button { Content = Shrines.Blessings[b], HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => Take(b, perform);
            buttons.Children.Add(button);
        }
        var later = new Button { Content = "Not yet.", HorizontalAlignment = HorizontalAlignment.Right };
        later.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = shrine.Name, FontSize = 20, Foreground = Brushes.LightSteelBlue },
                new TextBlock
                {
                    Text = "One of Ashur's old waystones, warm under your hand. It will give one thing, once.",
                    TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Wheat,
                },
                buttons,
                later,
            },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    public ShrineEvent Shrine { get; }

    /// <summary>Takes a blessing (tests call this).</summary>
    public void Take(string blessing, Func<GameCommand, bool> perform)
    {
        perform(new TakeBlessing(Shrine.ShrineId, blessing));
        Close();
    }
}
