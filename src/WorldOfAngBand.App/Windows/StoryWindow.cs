using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App.Windows;

/// <summary>A story level's altar: what lies there, and the choice. Choosing takes the hero's turn; closing chooses nothing.</summary>
public sealed class StoryWindow : Window
{
    public StoryWindow(StoryChoiceEvent story, Func<GameCommand, bool> perform)
    {
        Title = story.Title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(24, 19, 16));
        Story = story;
        var buttons = new StackPanel { Spacing = 8 };
        for (var i = 0; i < story.Choices.Count; i++)
        {
            var index = i;
            var button = new Button { Content = story.Choices[i].Text, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => Choose(index, perform);
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
                new TextBlock { Text = story.Title, FontSize = 20, Foreground = Brushes.BurlyWood },
                new TextBlock { Text = story.Prompt, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Wheat },
                buttons,
                later,
            },
        };
    }

    public StoryChoiceEvent Story { get; }

    /// <summary>Makes the choice (tests call this).</summary>
    public void Choose(int index, Func<GameCommand, bool> perform)
    {
        perform(new ChooseStory(Story.StoryId, index));
        Close();
    }
}
