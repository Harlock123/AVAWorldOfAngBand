using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WorldOfAngBand.App.Rendering;
using WorldOfAngBand.Data;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Game;
using WorldOfAngBand.App.Services;

namespace WorldOfAngBand.App.Tests;

public class WindowTests
{
    private static readonly Lazy<GameData> Data = new(() => DataLoader.LoadDefault(includeUserMods: false));

    /// <summary>A game window, shown headless, with the clock stopped so pictures repeat.</summary>
    private static MainWindow Open(ulong seed = 1234) => Open(seed, out _);

    private static MainWindow Open(ulong seed, out Graveyard graveyard)
    {
        var book = Path.Combine(Path.GetTempPath(), "worldofangband-tests", Guid.NewGuid().ToString("N"), "graveyard.json");
        graveyard = new Graveyard(book);
        var window = new MainWindow(new DungeonSession(Data.Value, seed), graveyard) { Width = 1280, Height = 800 };
        window.State.Frozen = true;
        window.ShowDialogs = false;
        window.Challenges = new ChallengeBoard(Path.Combine(Path.GetDirectoryName(book)!, "challenges.json"));
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets a journey finish: each frame takes the next step.</summary>
    private static void Walk(MainWindow window)
    {
        for (var i = 0; i < 2000 && window.StepsLeft > 0; i++)
            window.Tick();
    }

    /// <summary>
    /// The frame as drawn, saved as <paramref name="name"/>.png in the folder WORLDOFANGBAND_SHOTS names, when it is set
    /// (<c>WORLDOFANGBAND_SHOTS=docs/images dotnet test</c> redraws the README's pictures).
    /// </summary>
    private static WriteableBitmap Capture(MainWindow window, string name)
    {
        Pump();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was drawn.");
        if (Environment.GetEnvironmentVariable("WORLDOFANGBAND_SHOTS") is { Length: > 0 } folder)
        {
            var root = FindRepoRoot();
            var path = Path.Combine(Path.IsPathRooted(folder) ? folder : Path.Combine(root, folder), name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }
        return frame;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AVAWorldOfAngBand.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Explores until a lit room is in view: each time, travel to the nearest known floor beside the unknown (most
    /// levels start in a dark corridor, which makes a dull picture).
    /// </summary>
    private static void ExploreToALitRoom(MainWindow window)
    {
        var game = window.Session;
        for (var trip = 0; trip < 60; trip++)
        {
            if (game.Level.AllLocs().Count(p => game.Level[p].Has(Engine.World.SquareFlags.Seen) && game.Level[p].Has(Engine.World.SquareFlags.Glow)) > 30)
                return;
            var frontier = game.Level.AllLocs()
                .Where(p => game.Known.IsKnown(p) && game.Level.IsPassable(p) && p != game.Hero.Position
                    && DirectionExtensions.Compass.Any(d => game.Level.InBounds(p.Step(d)) && !game.Known.IsKnown(p.Step(d))))
                .OrderBy(p => p.DistanceTo(game.Hero.Position))
                .FirstOrDefault(p => game.PathTo(p) is not null);
            if (frontier == default || !window.TravelTo(frontier))
                return;
            Walk(window);
        }
    }

    /// <summary>How many of the frame's pixels are not the empty background: how much art was drawn.</summary>
    private static double DrawnShare(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var pixels = new int[buffer.Size.Width * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        var background = pixels[0];
        return pixels.Count(p => Math.Abs((p & 0xFF) - (background & 0xFF)) + Math.Abs(((p >> 8) & 0xFF) - ((background >> 8) & 0xFF)) > 24)
            / (double)pixels.Length;
    }

    [AvaloniaFact]
    public void The_town_is_drawn_round_the_hero()
    {
        var window = Open();
        var frame = Capture(window, "WorldOfAngBand-town");
        Assert.True(DrawnShare(frame) > 0.5, $"Only {DrawnShare(frame):P0} of the town was drawn.");
        window.Close();
    }

    [AvaloniaFact]
    public void Keys_move_the_hero_the_way_they_point_on_screen()
    {
        Assert.Equal(Direction.NorthWest, MainWindow.DirectionFor(Key.Up));
        Assert.Equal(Direction.SouthEast, MainWindow.DirectionFor(Key.Down));
        Assert.Equal(Direction.NorthEast, MainWindow.DirectionFor(Key.Right));
        Assert.Equal(Direction.SouthWest, MainWindow.DirectionFor(Key.Left));
        Assert.Equal(Direction.North, MainWindow.DirectionFor(Key.NumPad9));

        var window = Open();
        var start = window.Session.Hero.Position;
        var key = new[] { Key.Up, Key.Down, Key.Left, Key.Right }
            .First(k => window.Session.Level.IsPassable(start.Step(MainWindow.DirectionFor(k)!.Value)));
        Assert.True(window.Press(key));
        Assert.Equal(start.Step(MainWindow.DirectionFor(key)!.Value), window.Session.Hero.Position);

        // The point on screen where the hero stands is the hero's square.
        Assert.Equal(window.Session.Hero.Position, window.View.ToMap(new Avalonia.Point(window.View.Bounds.Width / 2, window.View.Bounds.Height / 2 - 4)));
        window.Close();
    }

    [AvaloniaFact]
    public void Click_to_travel_to_the_stairs_then_go_down_into_the_lid()
    {
        var window = Open();
        var stairs = window.Session.Level.FindFeature(TerrainFlags.DownStair).First();
        Assert.True(window.TravelTo(stairs));
        Walk(window);
        Assert.Equal(stairs, window.Session.Hero.Position);
        Assert.True(window.Type(">"));
        Assert.Equal(1, window.Session.Depth);
        Assert.Contains(window.State.Messages, m => m.Text == "You enter a maze of down staircases.");
        Assert.Equal("The Lid", IsoView.Stratum(window.Session.Depth));

        ExploreToALitRoom(window);
        var frame = Capture(window, "WorldOfAngBand-lid");
        Assert.True(DrawnShare(frame) > 0.12, $"Only {DrawnShare(frame):P0} of the level was drawn.");
        window.Close();
    }

    /// <summary>Puts monsters of these races round the hero, awake, and lets a turn pass so they are seen.</summary>
    private static void Surround(MainWindow window, params string[] races)
    {
        var game = window.Session;
        var spots = DirectionExtensions.Compass.Select(d => game.Hero.Position.Step(d).Step(d))
            .Concat(DirectionExtensions.Compass.Select(d => game.Hero.Position.Step(d)))
            .Where(p => game.Level.InBounds(p) && game.Level.IsEmptyFloor(p) && game.PathTo(p) is not null).ToList();
        for (var i = 0; i < races.Length && i < spots.Count; i++)
            game.Spawn(Data.Value.Monster(races[i])!, spots[i], asleep: true);
        window.Press(Key.NumPad5);
    }

    private static MainWindow OnFirstLevel(ulong seed)
    {
        var window = Open(seed);
        var stairs = window.Session.Level.FindFeature(TerrainFlags.DownStair).First();
        window.TravelTo(stairs);
        Walk(window);
        window.Type(">");
        return window;
    }

    [AvaloniaFact]
    public void Monsters_are_drawn_from_their_bridge_entry_and_named_under_the_pointer()
    {
        var window = OnFirstLevel(1234);
        ExploreToALitRoom(window);
        Surround(window, "kobold", "jackal", "giant_yellow_centipede", "rock_lizard", "cutpurse", "skeleton_kobold");
        var game = window.Session;
        Assert.True(game.VisibleFoes().Count() >= 3, "The monsters put round the hero should be in sight.");
        var frame = Capture(window, "WorldOfAngBand-fight");
        Assert.True(DrawnShare(frame) > 0.12);
        var foe = game.VisibleFoes().First();
        var at = window.View.ToScreen(foe.Monster.Position.X + 0.5, foe.Monster.Position.Y + 0.5);
        window.MouseMove(new Avalonia.Point(at.X, at.Y - 4));
        Pump();
        Assert.NotNull(window.Hud.Hover);
        Assert.StartsWith(foe.Name, window.Hud.Hover!.Value.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_T_turns_to_the_classic_letters_then_tiles_where_up_is_north()
    {
        var window = OnFirstLevel(1234);
        ExploreToALitRoom(window);
        Surround(window, "kobold", "jackal");
        Assert.True(window.Press(Key.T, KeyModifiers.Control));
        Assert.Equal(ViewMode.Letters, window.Mode);
        Assert.IsType<ClassicView>(window.Content);
        Capture(window, "WorldOfAngBand-letters");
        Assert.True(window.Press(Key.T, KeyModifiers.Control));
        Assert.Equal(ViewMode.Tiles, window.Mode);
        var frame = Capture(window, "WorldOfAngBand-tiles");
        Assert.True(DrawnShare(frame) > 0.02);
        Assert.Equal(Direction.North, MainWindow.ClassicDirectionFor(Key.Up));
        Assert.True(window.Press(Key.T, KeyModifiers.Control));
        Assert.Equal(ViewMode.Isometric, window.Mode);
        window.Close();
    }

    [AvaloniaFact]
    public void Death_writes_the_hero_into_the_book_and_enter_sends_another_down()
    {
        var window = OnFirstLevel(77);
        var game = window.Session;
        game.HurtHero(10_000, "a kobold");
        game.HurtHero(10_000, "a kobold");
        Assert.True(game.IsOver);
        Pump();
        Capture(window, "WorldOfAngBand-death");
        var record = Assert.Single(window.Hud.Book);
        Assert.Equal("a kobold", record.Killer);
        Assert.False(window.Type(">"));
        Assert.True(window.Press(Key.Enter));
        Assert.NotSame(game, window.Session);
        Assert.False(window.Session.IsOver);
        Assert.Equal(0, window.Session.Depth);
        window.Close();
    }

    /// <summary>Shows a dialog window headless, draws it, and saves the picture like the main window's.</summary>
    private static WriteableBitmap CaptureDialog(Avalonia.Controls.Window dialog, string name)
    {
        dialog.Show();
        Pump();
        var frame = dialog.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was drawn.");
        if (Environment.GetEnvironmentVariable("WORLDOFANGBAND_SHOTS") is { Length: > 0 } folder)
        {
            var path = Path.Combine(Path.IsPathRooted(folder) ? folder : Path.Combine(FindRepoRoot(), folder), name + ".png");
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }
        return frame;
    }

    [AvaloniaFact]
    public void An_altar_offers_its_choice_and_the_cinders_end_shows_the_epilogue()
    {
        var window = Open(1301, out var graveyard);
        var game = window.Session;
        game.JumpTo(15);
        var font = Story.Levels[0];
        var altar = window.OpenStory(new StoryChoiceEvent(font.Id, font.Title, font.Prompt, Story.Choices(font.Id, game.Flags, true)));
        CaptureDialog(altar, "WorldOfAngBand-altar");
        altar.Choose(0, game.Perform);
        Assert.Equal("sealed", game.Flags["font"]);

        // A winner's numbers for the picture.
        while (game.Hero.Level < 20)
            game.Hero.Character.LevelUp();
        game.Hero.MythicTier = Hero.MaxMythicTier;
        game.Belongings.Refit();
        game.Hero.Character.Heal(game.Hero.MaxHitPoints);
        game.JumpTo(Story.CinderDepth);
        var cinder = game.Foes.Single(f => f.Monster.Race.Id == Story.Cinder);
        game.Fight.Hurt(cinder, 1_000_000);
        Assert.True(game.Won);
        Pump();
        Assert.True(window.State.ShowEpilogue);
        Capture(window, "WorldOfAngBand-epilogue");
        Assert.True(window.Press(Key.Enter));
        var record = Assert.Single(graveyard.Read());
        Assert.True(record.Won);
        Assert.NotSame(game, window.Session);
        window.Close();
    }

    [AvaloniaFact]
    public void The_birth_window_makes_a_priest_who_remembers_a_legacy()
    {
        var folder = Path.Combine(Path.GetTempPath(), "worldofangband-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "the-ashbound-abc123.json"),
            """{ "Version": 1, "SavedAt": "2026-10-01T12:00:00+00:00", "Choices": { "Name": "Vessa" }, "Flags": { "act5.ending": "bind" } }""");
        File.WriteAllText(Path.Combine(folder, "unfinished.json"), """{ "Choices": { "Name": "Nobody" }, "Flags": {} }""");
        var window = Open();
        window.LegacyFolder = folder;
        var birth = window.OpenBirth(4242);
        Assert.Contains("Legacy: Vessa, The Lantern Rekindled", birth.RumourLabels);
        Assert.DoesNotContain(birth.RumourLabels, l => l.Contains("Nobody"));
        birth.Pick("priest", "confessor");
        birth.PickRumour("bind", legacy: true);
        CaptureDialog(birth, "WorldOfAngBand-birth");
        birth.Begin();
        var game = window.Session;
        Assert.Equal("priest", game.Hero.Character.Class.Id);
        Assert.Equal("confessor", game.Hero.Character.Subclass?.Id);
        Assert.Equal("bind", game.Rumour);
        window.Close();
    }

    [AvaloniaFact]
    public void A_new_level_asks_for_its_feats_and_the_bar_shows_what_they_give()
    {
        var window = Open();
        var game = window.Session;
        window.NewGame(77, new Birth(Rules.Characters.CharacterBuilder.Recommend(game.Content, "Ilse", "elf", "hedge-scholar", "wizard")
            with { Subclass = "pyromancer" }));
        game = window.Session;
        game.Hero.Character.LevelUp();
        var level = window.OpenLevelUp()!;
        Assert.Equal(2, level.Level);
        level.Pick("Wizard feat", "ray_of_frost");
        CaptureDialog(level, "WorldOfAngBand-levelup");
        level.Choose();
        Assert.Empty(game.Hero.Character.PendingLevels);
        Assert.Contains(game.Powers.Bar, a => a.Id == "ray_of_frost");
        // The bar's keys: 1 is Ignition, a spell needing a monster in sight.
        Assert.True(window.UseAbility(0) || game.VisibleFoes().Any() == false);
        window.Close();
    }

    [AvaloniaFact]
    public void A_weekly_challenge_goes_down_with_its_twist_and_a_death_goes_on_the_board()
    {
        var window = Open();
        var challenges = window.OpenChallenges(new DateOnly(2026, 11, 1));
        Assert.Equal("2026-W44", challenges.Weekly.Birth.Challenge!.Stamp);
        CaptureDialog(challenges, "WorldOfAngBand-challenges");
        window.NewGame(challenges.Weekly.Seed, challenges.Weekly.Birth);
        var game = window.Session;
        Assert.Equal(challenges.Weekly.Birth.Twist, game.Birth.Twist);
        game.HurtHero(10_000, "a test");
        game.HurtHero(10_000, "a kobold");
        Pump();
        var tried = Assert.Single(window.Challenges.For(Challenges.Weekly, "2026-W44"));
        Assert.Equal(1, tried.Attempt);
        Assert.Contains("killed by a kobold", tried.ShareLine);
        window.Close();
    }

    [AvaloniaFact]
    public void The_mods_window_makes_an_example_and_switches_it_off_for_next_time()
    {
        var root = Path.Combine(Path.GetTempPath(), "worldofangband-tests", Guid.NewGuid().ToString("N"));
        var window = Open();
        window.ModsRoot = Path.Combine(root, "mods");
        window.SettingsPath = Path.Combine(root, "settings.json");
        var mods = window.OpenMods();
        Assert.Empty(mods.Mods);
        mods.MakeExample();
        Assert.Equal([WorldOfAngBand.Data.ModCatalog.ExampleName], mods.Mods);
        CaptureDialog(mods, "WorldOfAngBand-mods");
        mods.Switch(WorldOfAngBand.Data.ModCatalog.ExampleName, on: false);
        Assert.Contains(WorldOfAngBand.Data.ModCatalog.ExampleName, AppSettings.Read(window.SettingsPath).DisabledMods);
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_q_asks_before_leaving_and_staying_keeps_the_game()
    {
        var window = Open();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        Assert.True(window.Press(Key.Q, KeyModifiers.Control));
        var ask = window.OpenQuit();
        ask.Show();
        ask.Close();
        Pump();
        Assert.False(closed);
        window.OpenQuit().Confirm(window.Close);
        Pump();
        Assert.True(closed);
    }

    [AvaloniaFact]
    public void Things_lie_on_the_floor_and_are_named_under_the_pointer()
    {
        var window = OnFirstLevel(1234);
        ExploreToALitRoom(window);
        var game = window.Session;
        var spots = DirectionExtensions.Compass.Select(game.Hero.Position.Step).Where(game.Level.IsEmptyFloor).Take(4).ToList();
        string[] kinds = ["long_sword", "chain_mail", "cure_light_wounds", "phase_door"];
        for (var i = 0; i < spots.Count; i++)
            game.Level.Objects.Add(spots[i], game.Factory.Create(kinds[i]));
        game.Level.Objects.Add(spots[0], game.Factory.MakeGold(game.Rng, 5));
        window.Press(Key.NumPad5);
        Assert.NotNull(game.Known.RememberedObject(spots[0]));
        var frame = Capture(window, "WorldOfAngBand-loot");
        Assert.True(DrawnShare(frame) > 0.12);
        var at = window.View.ToScreen(spots[1].X + 0.5, spots[1].Y + 0.5);
        window.MouseMove(new Avalonia.Point(at.X, at.Y));
        Pump();
        Assert.Contains("Chain Mail", window.Hud.Hover!.Value.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void The_pack_window_wears_and_uses_things()
    {
        var window = Open();
        var game = window.Session;
        var cap = game.Hero.Pack.Add(game.Factory.Create("hard_leather_cap"))!;
        var pack = window.OpenInventory();
        Assert.Contains(pack.WornLines, l => l.StartsWith("a Long Sword", StringComparison.Ordinal));
        Assert.Contains(pack.PackLines, l => l.Contains("Cure Light Wounds"));
        CaptureDialog(pack, "WorldOfAngBand-pack");
        pack.SelectCarried(cap);
        pack.Default();
        Assert.Same(cap, game.Hero.Pack.InSlot(EquipSlot.Head));
        pack.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_shop_window_buys_and_sells_and_the_smith_offers_runework()
    {
        var window = Open();
        var game = window.Session;
        var shop = window.OpenStore("alchemist");
        Assert.Contains(shop.StockLines, l => l.Contains("Cure Light Wounds") && l.EndsWith("gold", StringComparison.Ordinal));
        var potion = shop.Store.Stock.First(i => i.Kind.Id == "cure_light_wounds");
        var gold = game.Hero.Gold;
        shop.SelectStock(potion);
        shop.Buy();
        Assert.True(game.Hero.Gold < gold);
        Assert.StartsWith("You bought", shop.Said);
        shop.Close();

        game.Hero.Character.LevelUp();
        game.Hero.Gold = 2000;
        var smith = window.OpenStore("weaponsmith");
        CaptureDialog(smith, "WorldOfAngBand-smith");
        smith.Etch(game.Hero.Pack.Weapon!, Etching.Potency);
        Assert.Equal(1, game.Hero.Character.Weapon.Potency);
        smith.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void Walking_into_a_shop_door_opens_its_window()
    {
        var window = Open();
        var game = window.Session;
        var door = game.Level.FindFeature(TerrainFlags.Shop).First(p => game.Level.FeatureAt(p).Shop == "alchemist");
        var beside = game.Level.Neighbors(door).First(p => game.Level.IsPassable(p) && !game.Level.Has(p, TerrainFlags.Shop) && game.PathTo(p) is not null);
        Assert.True(window.TravelTo(beside));
        Walk(window);
        Assert.Equal(beside, game.Hero.Position);
        var entered = new List<string>();
        game.Published += e => { if (e is ShopEnteredEvent s) entered.Add(s.StoreId); };
        window.Press(window.Mode == ViewMode.Isometric ? IsoKey(DirectionExtensions.FromOffset(door.X - beside.X, door.Y - beside.Y)) : Key.None);
        Assert.Equal(["alchemist"], entered);
        window.Close();
    }

    /// <summary>The isometric key that walks a map direction.</summary>
    private static Key IsoKey(Direction direction) =>
        new[] { Key.Up, Key.PageUp, Key.Right, Key.PageDown, Key.Down, Key.End, Key.Left, Key.Home }.First(k => MainWindow.DirectionFor(k) == direction);

    [AvaloniaFact]
    public void Deep_monsters_come_in_their_families_and_their_spells_flash()
    {
        var window = Open(4243);
        var game = window.Session;
        game.JumpTo(40);
        foreach (var f in game.Foes.ToList())
            game.Banish(f);
        while (game.Hero.Level < 12)
            game.Hero.Character.LevelUp();
        // Deep rooms are seldom lit: walk to the nearest room and light it, as a Scroll of Light would.
        foreach (var p in game.Level.AllLocs())
            game.Known.Remember(game.Level, p);
        var room = game.Level.AllLocs().Where(p => game.Level[p].Has(Engine.World.SquareFlags.Room) && game.Level.IsEmptyFloor(p)
                && game.Level.Neighbors(p).All(n => game.Level.IsPassable(n)))
            .OrderBy(p => p.DistanceTo(game.Hero.Position)).First(p => game.PathTo(p) is not null);
        window.TravelTo(room);
        Walk(window);
        game.LightArea(game.Hero.Position, 6);
        window.Press(Key.NumPad5);
        Surround(window, "mature_white_dragon", "cave_orc", "white_wraith", "stone_golem", "vrock", "hill_giant", "moaning_spirit", "quylthulg");
        var dragon = game.Foes.FirstOrDefault(f => f.Monster.Race.Id == "mature_white_dragon");
        if (dragon is not null)
            game.Publish(new SpellEvent(dragon.Monster.Position, game.Hero.Position, MonsterSpellKind.Breath, "cold", 0));
        var frame = Capture(window, "WorldOfAngBand-deep");
        Assert.True(DrawnShare(frame) > 0.12);
        Assert.True(game.VisibleFoes().Count() >= 4);
        window.Close();
    }
}
