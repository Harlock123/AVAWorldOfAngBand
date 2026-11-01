using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Items;
using WorldOfAngBand.Game;

namespace WorldOfAngBand.App.Windows;

/// <summary>
/// A shop in Gallowmere: its shelves on the left, the hero's pack on the right, buying and selling between them (no time
/// passes in a shop). At home the hero leaves and takes things for nothing. At the Smith, runework too: potency and
/// striking etched on the weapon and armour the hero wears.
/// </summary>
public sealed class StoreWindow : Window
{
    private readonly DungeonSession _session;
    private readonly Store _store;
    private readonly object _gate;
    private readonly ListBox _stock = new() { MinHeight = 320 };
    private readonly ListBox _pack = new() { MinHeight = 320 };
    private readonly TextBlock _gold = new() { Foreground = Brushes.Gold, FontWeight = FontWeight.Bold };
    private readonly TextBlock _said = new() { Foreground = Brushes.Wheat, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _runework = new() { Spacing = 6 };
    private List<Item> _stockItems = [];
    private List<Item> _packItems = [];

    public StoreWindow(DungeonSession session, Store store, object gate)
    {
        _session = session;
        _store = store;
        _gate = gate;
        Title = store.Name;
        Width = 980;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(18, 15, 13));
        session.Published += Heard;
        Closed += (_, _) => session.Published -= Heard;

        Button Make(string text, Action act)
        {
            var b = new Button { Content = text, MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
            b.Click += (_, _) => act();
            return b;
        }
        var buy = Make(store.IsHome ? "Take" : "Buy", Buy);
        var sell = Make(store.IsHome ? "Leave" : "Sell", Sell);
        _stock.DoubleTapped += (_, _) => Buy();
        _pack.DoubleTapped += (_, _) => Sell();
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var left = new StackPanel { Spacing = 6, Children = { Heading(store.IsHome ? "In your room" : "For sale"), _stock, buy } };
        var right = new StackPanel { Spacing = 6, Children = { Heading("Your pack"), _pack, sell } };
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);
        var owner = string.IsNullOrEmpty(store.Owner) ? "" : $"Kept by {store.Owner}.";
        // At the Archive, Wren says where the story goes next.
        if (store.Id == "bookseller")
            owner += "\n" + WorldOfAngBand.Game.Story.WrenSays(session.Flags, session.Hero.MaxDepth);
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = store.Name, FontSize = 22, Foreground = Brushes.BurlyWood },
                    new TextBlock { Text = owner, Foreground = Brushes.Tan },
                    _gold,
                    columns,
                    _runework,
                    _said,
                    Make("Leave the shop", Close),
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
        Refresh();
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.Bold, Foreground = Brushes.BurlyWood };

    private void Heard(IGameEvent e)
    {
        if (e is MessageEvent m)
            _said.Text = m.Text;
    }

    public Store Store => _store;

    public IReadOnlyList<string> StockLines => _stockItems.Select(Line).ToList();

    public string Said => _said.Text ?? "";

    private string Line(Item item) =>
        _store.IsHome ? _session.NameOf(item) : $"{_session.NameOf(item)}  —  {_session.Stores.PriceToBuy(_store, item)} gold";

    public void Refresh()
    {
        _stockItems = _store.Stock.ToList();
        _packItems = _session.Hero.Pack.Pack.ToList();
        _stock.ItemsSource = _stockItems.Select(Line).ToList();
        _pack.ItemsSource = _packItems.Select(i => _store.IsHome || !_session.Stores.Buys(_store, i)
            ? _session.NameOf(i)
            : $"{_session.NameOf(i)}  —  {_session.Stores.PriceToSell(_store, i)} gold").ToList();
        _gold.Text = $"You have {_session.Hero.Gold} gold.";
        BuildRunework();
    }

    /// <summary>The Smith's runework: for the weapon and body armour worn, the next potency and striking rune, its price and level.</summary>
    private void BuildRunework()
    {
        _runework.Children.Clear();
        if (_store.Id != "weaponsmith")
            return;
        _runework.Children.Add(Heading("Runework: Edda etches potency and striking on what you wear"));
        var worn = new[] { _session.Hero.Pack.Weapon, _session.Hero.Pack.InSlot(EquipSlot.Body) }.OfType<Item>();
        foreach (var item in worn)
        {
            foreach (var kind in new[] { Etching.Potency, Etching.Striking })
            {
                if (_session.Stores.Offer(item, kind) is not { } offer)
                    continue;
                var rune = kind == Etching.Potency ? $"+{offer.Rank} potency" : offer.Rank switch { 1 => "striking", 2 => "greater striking", _ => "major striking" };
                var text = offer.Price == 0 ? $"{_session.NameOf(item)}: {offer.Refusal}"
                    : $"{_session.NameOf(item)}: {rune} for {offer.Price} gold{(offer.Refusal is { } why ? $" ({why})" : "")}";
                var button = new Button { Content = text, IsEnabled = offer.Refusal is null && _session.Hero.Gold >= offer.Price };
                var (target, etching) = (item, kind);
                button.Click += (_, _) => Etch(target, etching);
                _runework.Children.Add(button);
            }
        }
    }

    public void Buy()
    {
        if (_stock.SelectedIndex < 0)
            return;
        lock (_gate)
            _session.Stores.Buy(_store, _stockItems[_stock.SelectedIndex]);
        Refresh();
    }

    public void Sell()
    {
        if (_pack.SelectedIndex < 0)
            return;
        lock (_gate)
            _session.Stores.Sell(_store, _packItems[_pack.SelectedIndex]);
        Refresh();
    }

    public void Etch(Item item, Etching kind)
    {
        lock (_gate)
            _session.Stores.Etch(item, kind);
        Refresh();
    }

    /// <summary>Picks a shelf item (tests).</summary>
    public void SelectStock(Item item) => _stock.SelectedIndex = _stockItems.IndexOf(item);

    /// <summary>Picks a pack item (tests).</summary>
    public void SelectCarried(Item item) => _pack.SelectedIndex = _packItems.IndexOf(item);
}
