using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// The hero's pack and what they wear: wield or wear, take off, drink or read, or drop. Each of those takes the hero's
/// turn, so the window closes after one, as Angband's item commands do. Enter does what suits the item; D drops it, T
/// takes it off, Escape closes.
/// </summary>
public sealed class InventoryWindow : Window
{
    private readonly DungeonSession _session;
    private readonly Func<GameCommand, bool> _do;
    private readonly ListBox _worn = new() { MinHeight = 120 };
    private readonly ListBox _pack = new() { MinHeight = 200 };
    private readonly TextBlock _summary = new() { Foreground = Brushes.Wheat, TextWrapping = TextWrapping.Wrap };
    private List<Item> _wornItems = [];
    private List<Item> _packItems = [];

    public InventoryWindow(DungeonSession session, Func<GameCommand, bool> perform)
    {
        _session = session;
        _do = perform;
        Title = "Pack";
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(18, 15, 13));
        Button Make(string text, Action act)
        {
            var b = new Button { Content = text, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => act();
            return b;
        }
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                Make("Use / wear", Default), Make("Activate", ActivateSelected), Make("Take off", TakeOff), Make("Drop", Drop), Make("Close", Close),
            },
        };
        _worn.SelectionChanged += (_, _) =>
        {
            if (_worn.SelectedIndex >= 0)
                _pack.SelectedIndex = -1;
        };
        _pack.SelectionChanged += (_, _) =>
        {
            if (_pack.SelectedIndex >= 0)
                _worn.SelectedIndex = -1;
        };
        _worn.DoubleTapped += (_, _) => Default();
        _pack.DoubleTapped += (_, _) => Default();
        Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                Docked(_summary, Dock.Top),
                Docked(buttons, Dock.Bottom),
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        Heading("Worn"), _worn, Heading("Carried"), _pack,
                    },
                },
            },
        };
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Enter: Default(); break;
                case Key.D: Drop(); break;
                case Key.T: TakeOff(); break;
                default: return;
            }
            e.Handled = true;
        };
        Refresh();
        if (_packItems.Count > 0)
            _pack.SelectedIndex = 0;
    }

    private static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        control.Margin = dock == Dock.Top ? new Thickness(0, 0, 0, 10) : new Thickness(0, 10, 0, 0);
        return control;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.Bold, Foreground = Brushes.BurlyWood };

    /// <summary>The item picked in either list, or null.</summary>
    public Item? Selected => _worn.SelectedIndex >= 0 ? _wornItems[_worn.SelectedIndex]
        : _pack.SelectedIndex >= 0 ? _packItems[_pack.SelectedIndex] : null;

    public IReadOnlyList<string> WornLines => _wornItems.Select(_session.NameOf).ToList();

    public IReadOnlyList<string> PackLines => _packItems.Select(_session.NameOf).ToList();

    /// <summary>Picks a carried item (tests, and keys).</summary>
    public void SelectCarried(Item item) => _pack.SelectedIndex = _packItems.IndexOf(item);

    public void SelectWorn(Item item) => _worn.SelectedIndex = _wornItems.IndexOf(item);

    public void Refresh()
    {
        var hero = _session.Hero;
        _wornItems = hero.Pack.Equipped.ToList();
        _packItems = hero.Pack.Pack.ToList();
        _worn.ItemsSource = _wornItems.Select(_session.NameOf).ToList();
        _pack.ItemsSource = _packItems.Select(_session.NameOf).ToList();
        var c = hero.Character;
        _summary.Text = $"{hero.Gold} gold  ·  AC {c.ArmorClass().Total}  ·  attack +{c.AttackModifier().Total}, "
                        + $"d{c.Weapon.Definition.DamageDie}{(c.Weapon.Striking > 0 ? $" ×{c.Weapon.DiceCount}" : "")} {c.DamageBonus().Total:+0;-0}"
                        + $"  ·  speed {hero.Speed:+0;-0}  ·  light {hero.LightRadius}  ·  stealth {hero.Stealth}";
    }

    /// <summary>What suits the item: wear what can be worn, drink or read what can be used.</summary>
    public void Default()
    {
        if (Selected is not { } item)
            return;
        if (_wornItems.Contains(item))
            Act(new TakeOffItem(item));
        else if (item.IsWearable)
            Act(new WieldItem(item));
        else
            Act(new UseItem(item));
    }

    /// <summary>Activates the worn artifact selected (at the nearest monster in sight, if it attacks).</summary>
    public void ActivateSelected()
    {
        if (Selected is { } item && _wornItems.Contains(item))
            Act(new ActivateItem(item));
    }

    public void TakeOff()
    {
        if (Selected is { } item && _wornItems.Contains(item))
            Act(new TakeOffItem(item));
    }

    public void Drop()
    {
        if (Selected is { } item)
            Act(new DropItem(item, 1));
    }

    private void Act(GameCommand command)
    {
        if (_do(command))
            Close();
        else
            Refresh();
    }
}
