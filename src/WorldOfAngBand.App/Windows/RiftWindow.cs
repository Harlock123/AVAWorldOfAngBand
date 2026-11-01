using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App.Windows;

/// <summary>A Lantern Rift: where the hero can step through to (Gallowmere, and the waystations whose rifts they have touched).</summary>
public sealed class RiftWindow : Window
{
    public RiftWindow(RiftEvent rift, Func<GameCommand, bool> perform)
    {
        Rift = rift;
        Title = "Lantern Rift";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(14, 18, 30));
        var buttons = new StackPanel { Spacing = 8 };
        foreach (var (id, name) in rift.Destinations)
        {
            var to = id;
            var button = new Button { Content = $"Step through to {name}", HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => Go(to, perform);
            buttons.Children.Add(button);
        }
        var stay = new Button { Content = "Stay.", HorizontalAlignment = HorizontalAlignment.Right };
        stay.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Lantern Rift", FontSize = 20, Foreground = Brushes.LightSkyBlue },
                new TextBlock { Text = "Blue light, cold as a winter well. Through it, the other rifts you have touched.", Foreground = Brushes.LightSteelBlue, TextWrapping = TextWrapping.Wrap },
                buttons,
                stay,
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

    public RiftEvent Rift { get; }

    /// <summary>Steps through to a destination (tests call this).</summary>
    public void Go(string destination, Func<GameCommand, bool> perform)
    {
        Close();
        perform(new StepThroughRift(destination));
    }
}
