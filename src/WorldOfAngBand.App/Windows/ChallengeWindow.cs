using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.App.Services;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Content;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// Today's dungeon and this week's challenge (the spec's section 4.4): who goes down (the same hero for everyone), the
/// week's twist, the best tries so far, and a button to go down.
/// </summary>
public sealed class ChallengeWindow : Window
{
    public ChallengeWindow(ContentDatabase content, ChallengeBoard board, string name, DateOnly day, Action<ulong, Birth> begin)
    {
        Title = "Challenges";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 18, 15));
        Daily = Challenges.DailyFor(day, content, name);
        Weekly = Challenges.WeeklyFor(day, content, name);
        var twist = Challenges.Twists.First(t => t.Id == Weekly.Birth.Twist);
        StackPanel Section(string heading, string about, (ulong Seed, Birth Birth) run)
        {
            var c = run.Birth.Character!;
            var go = new Button { Content = "Go down", MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Right };
            go.Click += (_, _) =>
            {
                begin(run.Seed, run.Birth);
                Close();
            };
            var tries = board.For(run.Birth.Challenge!.Kind, run.Birth.Challenge.Stamp).Take(5)
                .Select(t => new TextBlock { Text = t.ShareLine, Foreground = Brushes.Tan, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var panel = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = heading, FontSize = 18, Foreground = Brushes.BurlyWood },
                    new TextBlock { Text = about, Foreground = Brushes.Wheat, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = $"Everyone goes down as {Article(content.Ancestry(c.AncestryId).Name.ToLowerInvariant())} {content.Class(c.ClassId).Name.ToLowerInvariant()}"
                               + (c.Subclass is { } s && content.Feats.TryGetValue(s, out var sub) ? $" ({sub.Name})" : "")
                               + $", from the {content.Background(c.BackgroundId).Name.ToLowerInvariant()} background.",
                        Foreground = Brushes.Wheat, TextWrapping = TextWrapping.Wrap,
                    },
                },
            };
            foreach (var line in tries)
                panel.Children.Add(line);
            panel.Children.Add(go);
            return panel;
        }
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 18,
            Children =
            {
                Section($"Today's dungeon, {Daily.Birth.Challenge!.Stamp}", "The same levels for everyone today.", Daily),
                Section($"This week's challenge, {Weekly.Birth.Challenge!.Stamp}: {twist.Name}", twist.Description, Weekly),
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

    private static string Article(string word) => ("aeiou".Contains(word[0]) ? "an " : "a ") + word;

    public (ulong Seed, Birth Birth) Daily { get; }

    public (ulong Seed, Birth Birth) Weekly { get; }
}
