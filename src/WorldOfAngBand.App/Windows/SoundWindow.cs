using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.App.Services;
using WorldOfAngBand.Audio;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// The Sound window (Ctrl+O, the spec's section 8.1): the master, music, effects and ambience volumes, each part on or
/// off, and OpenAL's mixing period. Changes are heard at once and kept in settings.json; the mixing period takes effect
/// when the game starts again.
/// </summary>
public sealed class SoundWindow : Window
{
    private readonly SoundDirector _sound;
    private readonly AppSettings _settings;
    private readonly string? _settingsPath;
    private readonly Dictionary<string, Slider> _sliders = [];
    private readonly Dictionary<string, CheckBox> _switches = [];
    private readonly ComboBox _buffer = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    public SoundWindow(SoundDirector sound, AppSettings settings, string? settingsPath = null)
    {
        _sound = sound;
        _settings = settings;
        _settingsPath = settingsPath;
        Title = "Sound";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 18, 15));

        var s = settings.Sound;
        var rows = new StackPanel { Spacing = 10 };
        rows.Children.Add(Row("Master", s.Master, null, null));
        rows.Children.Add(Row("Music", s.Music, "Music", s.MusicOn));
        rows.Children.Add(Row("Effects", s.Effects, "Effects", s.EffectsOn));
        rows.Children.Add(Row("Ambience", s.Ambience, "Ambience", s.AmbienceOn));
        var mute = new CheckBox { Content = "All sound on (Ctrl+M)", IsChecked = !s.Muted, Foreground = Brushes.Wheat };
        mute.IsCheckedChanged += (_, _) => Change(x => x with { Muted = mute.IsChecked != true });
        _switches["All"] = mute;

        _buffer.ItemsSource = AudioBuffer.Choices.Select(c => c.Label).ToList();
        _buffer.SelectedIndex = Math.Max(0, AudioBuffer.Choices.ToList().FindIndex(c => c.Id == s.Buffer));
        _buffer.SelectionChanged += (_, _) => Change(x => x with { Buffer = AudioBuffer.Choices[Math.Max(0, _buffer.SelectedIndex)].Id });

        var status = sound.Engine is NullAudioEngine { Reason: { } reason } ? reason
            : sound.Engine.IsAvailable ? "" : "Sound is off: the game was started with --mute.";
        var close = new Button { Content = "Close", MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        close.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Sound", FontSize = 22, Foreground = Brushes.BurlyWood },
                new TextBlock { Text = status, IsVisible = status.Length > 0, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap },
                mute,
                rows,
                new TextBlock { Text = "Audio buffer", FontWeight = FontWeight.Bold, Foreground = Brushes.BurlyWood, Margin = new Thickness(0, 6, 0, 0) },
                _buffer,
                new TextBlock
                {
                    Text = "A bigger buffer keeps sound smooth on a slow or virtual sound card, for a little delay. It takes effect when the game starts again.",
                    Foreground = Brushes.Tan, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                },
                close,
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

    private Control Row(string name, float volume, string? part, bool? on)
    {
        var label = new TextBlock { Text = name, Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Wheat };
        var percent = new TextBlock { Width = 44, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Tan, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(volume * 100), Width = 340, VerticalAlignment = VerticalAlignment.Center };
        percent.Text = $"{slider.Value:0}%";
        slider.ValueChanged += (_, e) =>
        {
            percent.Text = $"{e.NewValue:0}%";
            var v = (float)(e.NewValue / 100);
            Change(x => name switch
            {
                "Master" => x with { Master = v },
                "Music" => x with { Music = v },
                "Effects" => x with { Effects = v },
                _ => x with { Ambience = v },
            });
        };
        _sliders[name] = slider;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { label, slider, percent } };
        if (part is not null)
        {
            var box = new CheckBox { IsChecked = on == true, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Width = 32 };
            ToolTip.SetTip(box, $"{part} on");
            box.IsCheckedChanged += (_, _) => Change(x => part switch
            {
                "Music" => x with { MusicOn = box.IsChecked == true },
                "Effects" => x with { EffectsOn = box.IsChecked == true },
                _ => x with { AmbienceOn = box.IsChecked == true },
            });
            _switches[part] = box;
            row.Children.Insert(0, box);
        }
        else
        {
            row.Children.Insert(0, new Border { Width = 32 });
        }
        return row;
    }

    private void Change(Func<SoundSettings, SoundSettings> change)
    {
        _settings.Sound = change(_settings.Sound);
        _sound.Apply(_settings.Sound);
        _settings.Save(_settingsPath);
    }

    /// <summary>Sets a volume by name (Master, Music, Effects, Ambience), from 0 to 100 (tests).</summary>
    public void SetVolume(string name, double percent) => _sliders[name].Value = percent;

    /// <summary>Turns a part (Music, Effects, Ambience, or All) on or off (tests).</summary>
    public void Switch(string part, bool on) => _switches[part].IsChecked = on;

    /// <summary>Picks the mixing period by its id (tests).</summary>
    public void PickBuffer(string id) => _buffer.SelectedIndex = AudioBuffer.Choices.ToList().FindIndex(c => c.Id == id);
}
