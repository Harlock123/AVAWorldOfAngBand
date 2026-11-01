using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.App.Services;
using WorldOfAngBand.Data;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// The mods (AVABand's, with this game's own layers): each folder in the mods folder, what it adds or replaces, whether
/// it loaded, and a switch to leave it out. Changes take effect when the game starts again. "Make an example mod"
/// writes a small working one to copy from.
/// </summary>
public sealed class ModsWindow : Window
{
    private readonly string _root;
    private readonly AppSettings _settings;
    private readonly string? _settingsPath;
    private readonly IReadOnlyList<ModLoadResult> _loaded;
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly TextBlock _note = new() { Foreground = Brushes.Tan, TextWrapping = TextWrapping.Wrap };

    public ModsWindow(string root, AppSettings settings, IReadOnlyList<ModLoadResult> loaded, string? settingsPath = null)
    {
        _root = root;
        _settings = settings;
        _settingsPath = settingsPath;
        _loaded = loaded;
        Title = "Mods";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 18, 15));
        var example = new Button { Content = "Make an example mod" };
        example.Click += (_, _) => MakeExample();
        var close = new Button { Content = "Close", MinWidth = 100 };
        close.Click += (_, _) => Close();
        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(close);
        buttons.Children.Add(example);
        Content = new ScrollViewer
        {
            MaxHeight = 760,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Mods", FontSize = 22, Foreground = Brushes.BurlyWood },
                    new TextBlock
                    {
                        Text = $"Each folder in {root} is a mod: data files named as the game's own, over them by id. A monster or item "
                               + "comes into the game through its bridge/monsters.json and bridge/items.json, and its names through ashen_names.json.",
                        Foreground = Brushes.Wheat, TextWrapping = TextWrapping.Wrap,
                    },
                    _list,
                    _note,
                    buttons,
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
        Fill();
    }

    /// <summary>The mods found, by folder name.</summary>
    public IReadOnlyList<string> Mods { get; private set; } = [];

    private void Fill()
    {
        _list.Children.Clear();
        var mods = ModCatalog.Discover(_root);
        Mods = mods.Select(m => m.Name).ToList();
        if (mods.Count == 0)
            _list.Children.Add(new TextBlock { Text = "No mods yet.", Foreground = Brushes.Tan });
        foreach (var mod in mods)
        {
            var state = _loaded.FirstOrDefault(r => string.Equals(r.Name, mod.Name, StringComparison.OrdinalIgnoreCase));
            var on = !_settings.DisabledMods.Contains(mod.Name, StringComparer.OrdinalIgnoreCase);
            var toggle = new CheckBox { Content = mod.Title, IsChecked = on, FontWeight = FontWeight.Bold };
            var name = mod.Name;
            toggle.IsCheckedChanged += (_, _) => Switch(name, toggle.IsChecked == true);
            var lines = new List<string>();
            if (mod.Manifest?.Description is { Length: > 0 } about)
                lines.Add(about);
            lines.AddRange(mod.Files.Select(f => f.Summary));
            lines.AddRange(mod.Errors.Select(e => "Problem: " + e));
            lines.AddRange(mod.Notes);
            lines.Add(state?.State switch
            {
                ModLoadState.Loaded => "Loaded.",
                ModLoadState.Failed => $"Not loaded: {state.Error}",
                ModLoadState.Disabled => "Switched off.",
                _ => "New: it loads when the game starts again.",
            });
            _list.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children = { toggle, new TextBlock { Text = string.Join("\n", lines), Foreground = Brushes.Tan, FontSize = 12, TextWrapping = TextWrapping.Wrap } },
            });
        }
    }

    /// <summary>Switches a mod on or off (by folder name); it takes effect when the game starts again.</summary>
    public void Switch(string mod, bool on)
    {
        _settings.DisabledMods.RemoveAll(m => string.Equals(m, mod, StringComparison.OrdinalIgnoreCase));
        if (!on)
            _settings.DisabledMods.Add(mod);
        _settings.Save(_settingsPath);
        _note.Text = "The change takes effect when the game starts again.";
    }

    public void MakeExample()
    {
        Directory.CreateDirectory(_root);
        _note.Text = ModCatalog.WriteExample(_root) is { } dir
            ? $"Wrote {dir}. It loads when the game starts again."
            : "There is an example mod already.";
        Fill();
    }
}
