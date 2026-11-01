using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Progression;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// A new level's picks (the spec's section 5.2): a class feat, a skill feat, a general or ancestry feat, a skill
/// increase, as Pathfinder's level table grants them. Each list starts on a suggestion; its description shows beneath.
/// Levels waiting for their picks are taken lowest first, one page each.
/// </summary>
public sealed class LevelUpWindow : Window
{
    private readonly DungeonSession _session;
    private readonly Func<GameCommand, bool> _perform;
    private readonly StackPanel _page = new() { Spacing = 10 };
    private readonly Dictionary<string, ComboBox> _lists = [];

    public LevelUpWindow(DungeonSession session, Func<GameCommand, bool> perform)
    {
        _session = session;
        _perform = perform;
        Title = "A new level";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 18, 15));
        Content = new ScrollViewer { MaxHeight = 760, Content = new Border { Padding = new Thickness(20), Child = _page } };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Build();
    }

    /// <summary>The level whose picks are being made.</summary>
    public int Level { get; private set; }

    private Character Character => _session.Hero.Character;

    private void Build()
    {
        _page.Children.Clear();
        _lists.Clear();
        if (Character.PendingLevels is not [var level, ..])
        {
            Close();
            return;
        }
        Level = level;
        var suggested = LevelPicks.Suggest(Character, level);
        _page.Children.Add(new TextBlock { Text = $"Level {level}", FontSize = 22, Foreground = Brushes.BurlyWood });
        var gains = LevelProgression.GainsAt(level);
        void Feats(LevelGain gain, FeatType type, string heading, string? pick)
        {
            if ((gains & gain) == 0)
                return;
            var options = Character.FeatOptions(type, level);
            AddList(heading, options.Select(f => (f.Id, f.Name, f.Description)).ToList(), pick);
        }
        Feats(LevelGain.ClassFeat, FeatType.Class, $"{Character.Class.Name} feat", suggested.ClassFeat);
        Feats(LevelGain.AncestryFeat, FeatType.Ancestry, $"{Character.Ancestry.Name} feat", suggested.AncestryFeat);
        Feats(LevelGain.SkillFeat, FeatType.Skill, "Skill feat", suggested.SkillFeat);
        Feats(LevelGain.GeneralFeat, FeatType.General, "General feat", suggested.GeneralFeat);
        if ((gains & LevelGain.SkillIncrease) != 0)
        {
            var skills = Skills.Attribute.Keys.Where(s => Character.CanIncrease(s, level))
                .Select(s => (s, s, $"{s}: from {Character.SkillRank(s)} to {Character.SkillRank(s) + 1}.")).ToList();
            AddList("Skill increase", skills, suggested.SkillIncrease);
        }
        if (suggested.Boosts is { } boosts)
            _page.Children.Add(new TextBlock { Text = $"Attribute boosts: {string.Join(", ", boosts)}.", Foreground = Brushes.Wheat });
        var choose = new Button { Content = "Choose", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 120 };
        choose.Click += (_, _) => Choose();
        _page.Children.Add(choose);
    }

    private void AddList(string heading, IReadOnlyList<(string Id, string Name, string Description)> options, string? pick)
    {
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Tan, FontSize = 12 };
        var list = new ComboBox { ItemsSource = options.Select(o => o.Name).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch, Tag = options };
        list.SelectionChanged += (_, _) =>
            description.Text = list.SelectedIndex >= 0 ? options[list.SelectedIndex].Description : "";
        list.SelectedIndex = Math.Max(0, options.ToList().FindIndex(o => o.Id == pick));
        _lists[heading] = list;
        _page.Children.Add(new TextBlock { Text = heading, FontWeight = FontWeight.Bold, Foreground = Brushes.BurlyWood });
        _page.Children.Add(list);
        _page.Children.Add(description);
    }

    /// <summary>What each list is on: the heading's pick (an id).</summary>
    public IReadOnlyDictionary<string, string?> Picks => _lists.ToDictionary(kv => kv.Key, kv =>
        kv.Value.Tag is IReadOnlyList<(string Id, string Name, string Description)> options && kv.Value.SelectedIndex >= 0
            ? options[kv.Value.SelectedIndex].Id
            : null);

    /// <summary>Picks a list's option by id (tests).</summary>
    public void Pick(string heading, string id)
    {
        var list = _lists[heading];
        if (list.Tag is IReadOnlyList<(string Id, string Name, string Description)> options)
            list.SelectedIndex = options.ToList().FindIndex(o => o.Id == id);
    }

    /// <summary>Makes this level's picks, then turns to the next level waiting, or closes.</summary>
    public void Choose()
    {
        var suggested = LevelPicks.Suggest(Character, Level);
        string? Of(string prefix) => _lists.FirstOrDefault(kv => kv.Key.EndsWith(prefix, StringComparison.Ordinal)) is { Value: not null } kv
            ? Picks[kv.Key]
            : null;
        var choice = suggested with
        {
            ClassFeat = suggested.ClassFeat is null ? null : Of($"{Character.Class.Name} feat"),
            AncestryFeat = suggested.AncestryFeat is null ? null : Of($"{Character.Ancestry.Name} feat"),
            SkillFeat = suggested.SkillFeat is null ? null : Of("Skill feat"),
            GeneralFeat = suggested.GeneralFeat is null ? null : Of("General feat"),
            SkillIncrease = suggested.SkillIncrease is null ? null : Of("Skill increase"),
        };
        _perform(new ChooseLevel(Level, choice));
        Build();
    }
}
