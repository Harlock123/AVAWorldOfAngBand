using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.App.Services;
using WorldOfAngBand.Game;
using WorldOfAngBand.Rules.Characters;
using WorldOfAngBand.Rules.Content;
using WorldOfAngBand.Rules.Rules;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// A new Lantern-sworn (the spec's section 5.1): a name, ancestry and heritage, background, class and subclass (with
/// Pathfinder's recommended boosts), then how they heard Ashen Reliquary ended: a rumour at random, a rumour chosen, or
/// a Legacy, the ending a hero reached in an AVAAshenReliquary save. Then the birth options.
/// </summary>
public sealed class BirthWindow : Window
{
    private readonly ContentDatabase _content;
    private readonly IReadOnlyList<LegacyHero> _legacy;
    private readonly TextBox _name = new() { Text = "Lantern-sworn", MaxLength = 24 };
    private readonly ComboBox _ancestry = Wide();
    private readonly ComboBox _heritage = Wide();
    private readonly ComboBox _background = Wide();
    private readonly ComboBox _class = Wide();
    private readonly ComboBox _subclass = Wide();
    private readonly ComboBox _rumour = Wide();
    private readonly CheckBox _randarts = new() { Content = "Random artifacts (a new set, made for this hero)" };
    private readonly TextBlock _about = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Tan, FontSize = 12 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Wheat };
    private readonly List<AncestryDefinition> _ancestries;
    private readonly List<BackgroundDefinition> _backgrounds;
    private readonly List<ClassDefinition> _classes;
    private List<FeatDefinition> _subclasses = [];
    private readonly List<(string? Ending, string Label)> _rumours = [];

    public BirthWindow(ContentDatabase content, IReadOnlyList<LegacyHero> legacy, Birth? last = null)
    {
        _content = content;
        _legacy = legacy;
        Title = "A new Lantern-sworn";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 18, 15));

        _ancestries = content.Ancestries.Values.OrderBy(a => a.Id == "human" ? 0 : 1).ThenBy(a => a.Name).ToList();
        _backgrounds = content.Backgrounds.Values.OrderBy(b => b.Name).ToList();
        _classes = content.Classes.Values.OrderBy(c => Powers.IsPlayable(content, c.Id) ? 0 : 1).ThenBy(c => c.Name).ToList();
        _ancestry.ItemsSource = _ancestries.Select(a => a.Name).ToList();
        _background.ItemsSource = _backgrounds.Select(b => b.Name).ToList();
        _class.ItemsSource = _classes.Select(c => Powers.IsPlayable(content, c.Id) ? c.Name + (c.Also is { } also ? $" ({also})" : "") : $"{c.Name} (not yet)").ToList();
        _rumours.Add((null, "A rumour, heard at random"));
        foreach (var (ending, _, _) in Story.Rumours)
            _rumours.Add((ending, $"The rumour: {Story.EndingTitle(ending)}"));
        foreach (var hero in legacy)
            _rumours.Add((hero.Ending, $"Legacy: {hero.Name}, {Story.EndingTitle(hero.Ending)}"));
        _rumour.ItemsSource = _rumours.Select(r => r.Label).ToList();

        _ancestry.SelectionChanged += (_, _) => { FillHeritages(); Refresh(); };
        _class.SelectionChanged += (_, _) => { FillSubclasses(); Refresh(); };
        _heritage.SelectionChanged += (_, _) => Refresh();
        _background.SelectionChanged += (_, _) => Refresh();
        _subclass.SelectionChanged += (_, _) => Refresh();
        _name.TextChanged += (_, _) => Refresh();

        var begin = new Button { Content = "Go down", MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        begin.Click += (_, _) => Begin();
        Content = new ScrollViewer
        {
            MaxHeight = 820,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "A new Lantern-sworn", FontSize = 22, Foreground = Brushes.BurlyWood },
                    Label("Name"), _name,
                    Label("Ancestry"), _ancestry, _heritage,
                    Label("Background"), _background,
                    Label("Class"), _class, _subclass, _about,
                    Label("How you heard it ended"), _rumour,
                    Label("Birth options"), _randarts,
                    _summary,
                    begin,
                },
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

        // Start from the last hero's choices, or a human deserter fighter.
        var c = last?.Character;
        _name.Text = c?.Name ?? "Lantern-sworn";
        _ancestry.SelectedIndex = Math.Max(0, _ancestries.FindIndex(a => a.Id == (c?.AncestryId ?? "human")));
        _background.SelectedIndex = Math.Max(0, _backgrounds.FindIndex(b => b.Id == (c?.BackgroundId ?? "deserter")));
        _class.SelectedIndex = Math.Max(0, _classes.FindIndex(k => k.Id == (c?.ClassId ?? "fighter")));
        FillHeritages();
        FillSubclasses();
        if (c?.HeritageId is { } heritage && Ancestry.Heritages is { } heritages)
            _heritage.SelectedIndex = Math.Max(0, heritages.ToList().FindIndex(h => h.Id == heritage));
        if (c?.Subclass is { } sub)
            _subclass.SelectedIndex = Math.Max(0, _subclasses.FindIndex(f => f.Id == sub));
        _rumour.SelectedIndex = last?.Rumour is { } r ? Math.Max(0, _rumours.FindIndex(x => x.Ending == r)) : 0;
        _randarts.IsChecked = last?.Randarts == true;
        Refresh();
    }

    /// <summary>The birth made when the player goes down; null until then.</summary>
    public Birth? Result { get; private set; }

    /// <summary>Raised when the player goes down.</summary>
    public event Action<Birth>? Chosen;

    private static ComboBox Wide() => new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    private static TextBlock Label(string text) =>
        new() { Text = text, FontWeight = FontWeight.Bold, Foreground = Brushes.BurlyWood, Margin = new Thickness(0, 8, 0, 0) };

    private AncestryDefinition Ancestry => _ancestries[Math.Max(0, _ancestry.SelectedIndex)];

    private ClassDefinition Class => _classes[Math.Max(0, _class.SelectedIndex)];

    private void FillHeritages()
    {
        var heritages = Ancestry.Heritages ?? [];
        _heritage.ItemsSource = heritages.Select(h => h.Name).ToList();
        _heritage.IsVisible = heritages.Count > 0;
        _heritage.SelectedIndex = heritages.Count > 0 ? 0 : -1;
    }

    private void FillSubclasses()
    {
        _subclasses = _content.Feats.Values.Where(f => f.Type == FeatType.Subclass && f.Class == Class.Id).OrderBy(f => f.Name).ToList();
        _subclass.ItemsSource = _subclasses.Select(f => f.Name).ToList();
        _subclass.SelectedIndex = _subclasses.Count > 0 ? 0 : -1;
    }

    /// <summary>The hero as chosen so far, with Pathfinder's recommended boosts.</summary>
    public CharacterChoices Choices =>
        CharacterBuilder.Recommend(_content, string.IsNullOrWhiteSpace(_name.Text) ? "Lantern-sworn" : _name.Text.Trim(), Ancestry.Id,
            _backgrounds[Math.Max(0, _background.SelectedIndex)].Id, Class.Id, null,
            _heritage.SelectedIndex >= 0 ? Ancestry.Heritages?[_heritage.SelectedIndex].Id : null) with
        {
            Subclass = _subclass.SelectedIndex >= 0 ? _subclasses[_subclass.SelectedIndex].Id : null,
        };

    private void Refresh()
    {
        if (_ancestry.SelectedIndex < 0 || _class.SelectedIndex < 0 || _background.SelectedIndex < 0)
            return;
        var playable = Powers.IsPlayable(_content, Class.Id);
        var sub = _subclass.SelectedIndex >= 0 ? _subclasses[_subclass.SelectedIndex] : null;
        _about.Text = playable
            ? sub?.Description ?? ""
            : $"The {Class.Name} needs what this game doesn't have yet (a companion, raised dead, a bow or a gun).";
        try
        {
            var character = CharacterBuilder.Create(_content, Choices);
            var key = character.KeyAttribute;
            _summary.Text = $"At level 1: {character.MaxHitPoints} HP, {key} +{character.Attributes[key]}, {Class.Resource} for abilities. "
                            + $"Background skill: {_backgrounds[_background.SelectedIndex].Skill}.";
        }
        catch (RulesException e)
        {
            _summary.Text = e.Message;
        }
    }

    /// <summary>Go down with the hero chosen (tests call this).</summary>
    public void Begin()
    {
        if (!Powers.IsPlayable(_content, Class.Id))
            return;
        try
        {
            CharacterBuilder.Create(_content, Choices);
        }
        catch (RulesException e)
        {
            _summary.Text = e.Message;
            return;
        }
        Result = new Birth(Choices, _rumours[Math.Max(0, _rumour.SelectedIndex)].Ending, _randarts.IsChecked == true);
        Chosen?.Invoke(Result);
        Close();
    }

    /// <summary>Picks a class and subclass by id (tests).</summary>
    public void Pick(string classId, string? subclass = null)
    {
        _class.SelectedIndex = _classes.FindIndex(c => c.Id == classId);
        if (subclass is not null)
            _subclass.SelectedIndex = _subclasses.FindIndex(f => f.Id == subclass);
    }

    /// <summary>Picks how the hero heard it ended: an ending, or null for a rumour at random (tests).</summary>
    public void PickRumour(string? ending, bool legacy = false) =>
        _rumour.SelectedIndex = _rumours.FindIndex(r => r.Ending == ending && (!legacy || r.Label.StartsWith("Legacy", StringComparison.Ordinal)));

    public IReadOnlyList<string> RumourLabels => _rumours.Select(r => r.Label).ToList();
}
