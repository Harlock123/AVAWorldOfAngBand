using System.Text.RegularExpressions;
using WorldOfAngBand.Engine.Definitions;
using WorldOfAngBand.Game;
using WorldOfAngBand.Game.Bridge;
using Xunit.Sdk;

namespace WorldOfAngBand.Tests;

/// <summary>
/// The bridge check (the spec's section 10): every monster, spell and element of Angband 4.2.5 is brought in; no
/// Tolkien name reaches the screen; and how much of the bestiary has Flare art is reported.
/// </summary>
public partial class BridgeCheckTests(ITestOutputHelper output)
{
    /// <summary>
    /// Tolkien's names and coinages that would give the game away, matched as whole words in what the game shows (the
    /// names it shows; the 4.2.5 data underneath keeps its own).
    /// </summary>
    [GeneratedRegex(@"\b(Morgoth|Sauron|Saruman|Gandalf|Galadriel|Elbereth|Gondolin|N[uú]men[oó]r\w*|Mordor|Moria|Durin|Nazg[uû]l|Balrog|Smaug|Glaurung|Shelob|Ungoliant|Beorn|Bilbo|Frodo|Gollum|Sm[eé]agol|Maggot|Bullroarer|Wormtongue|Grishn[aá]kh|Golfimbul|Shagrat|Gorbag|Bolg|Azog|Lugdush|Ugl[uú]k|Ufthak|Lagduf|M[iî]m|Ulfang|Ulfast|Ulwarth|Uldor|Lorgan|Castamir|Gorlim|Barahir|T[uú]rin|E[oö]l|Maeglin|Thuringwethil|Tevildo|Carcharoth|Draugluin|Huan|Ar-Pharaz[oô]n|Umbar|Harad\w*|Easterling|Dr[uú]adan|D[uú]nadan|Angmar|Mirkwood|Gorgoroth|Durthang|Cirith|Ungol|Barrow-?wight|hobbit|Huorn|Ent|M[uû]mak|Uruk|Olog|Snaga|mithril|Westernesse|Lothl[oó]rien|Valar|Maia|Ainu\w*|Ulmo|Manw[eë]|Varda|Aul[eë]|Yavanna|Mandos|Nienna|Orom[eë]|Oss[eë]|Gothmog|Ancalagon|Scatha|Itangast|Lungorthin|Khamûl|Khamul|Witch-King|Vecna|Craban|Crebain|Miruvor|Lembas|Ringil|And[uú]ril|Glamdring|Orcrist|Narthanc|Nimthanc|Dethanc|Aiglos|Gurthang|Mormegil|Anguirel|Aranr[uú]th|Elendil|Isildur|F[eë]anor|Thingol|L[uú]thien|Gil-galad|Celeborn|Celegorm|Thranduil|Th[eé]oden|[EÉ]owyn|Rohirrim|Eorlingas|Gondor|Erebor|Thr[aá]in|Thr[oó]r|Thorin|Ingw[eë]|Elessar|Evenstar|Narya|Nenya|Vilya|Grond|Arkenstone|Elros|E[aä]rendil|Fingolfin|Celebrimbor|Melkor|Tulkas|Radagast|Ol[oó]rin|Maedhros|Amrod|Amras|Thengel|Dor-L[oó]min|Hithlomir|Belthronding|Ber[uú]thiel|Thorongil|Tuor|Aragorn|Strider|Narvi|Haldir|Hornburg|Dol Amroth|Arvedui|Anárion|Anarion|Himring|Eönwë|E[oö]nw[eë]|H[uú]rin|Azagh[aâ]l|Fundin|Balli|N[aá]in|Gil-Galad)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Tolkien();

    private static readonly CreatureBridge Bridge = CreatureBridge.Load(TestData.Data);

    [Fact]
    public void Every_monster_but_the_seasonal_ones_is_bridged()
    {
        var missing = TestData.Data.Monsters.Where(m => !Bridge.Has(m)).Select(m => m.Id).ToList();
        Assert.Empty(missing);
        Assert.All(TestData.Data.SeasonalMonsters, m => Assert.False(Bridge.Has(m)));
    }

    [Fact]
    public void Every_monster_spell_is_cast_or_waits_for_what_it_needs()
    {
        var used = TestData.Data.Monsters.SelectMany(m => m.Spells).Distinct().ToList();
        Assert.All(used, id => Assert.NotNull(TestData.Data.MonsterSpell(id)));
        var deferred = TestData.Data.MonsterSpells.Where(s => MonsterSpells.Deferred.Contains(s.Kind)).Select(s => s.Id).ToList();
        output.WriteLine($"{TestData.Data.MonsterSpells.Count} spells; waiting for traps, webs and shapes: {string.Join(", ", deferred)}");
        Assert.Equal(["SHAPECHANGE", "TRAPS", "WEAVE"], deferred.Order());
    }

    [Fact]
    public void Every_element_has_a_pathfinder_damage_type()
    {
        var elements = TestData.Data.Elements.Select(e => e.Id).ToList();
        Assert.Equal(25, elements.Count);
        Assert.All(elements, e => Assert.Contains(e, Elements.Known));
        var spellElements = TestData.Data.MonsterSpells.Select(s => s.Element).OfType<string>().Distinct();
        Assert.All(spellElements, e => Assert.Contains(e, Elements.Known));
    }

    [Fact]
    public void No_tolkien_name_reaches_the_screen()
    {
        var shown = TestData.Data.Monsters.Select(m => (Kind: "monster", Id: m.Id, Name: Bridge.NameOf(m)))
            .Concat(TestData.Data.Egos.Select(e => (Kind: "ego", e.Id, Name: Setting.EgoName(e.Id) ?? e.Name)))
            .Concat(TestData.Data.Objects.Where(k => ItemBridge.Load(TestData.Data).Has(k))
                .Select(k => (Kind: "object", k.Id, Name: Setting.ObjectName(k.Id) ?? k.Name)))
            .Concat(TestData.Data.Artifacts.Select(a => (Kind: "artifact", a.Id, Name: Setting.ArtifactName(a.Id) ?? a.Name)))
            // Random artifacts are named by Angband's name generator: a few sets of them.
            .Concat(new ulong[] { 1, 2, 3, 4, 5 }.SelectMany(seed => Engine.Items.RandartGenerator.Generate(TestData.Data, seed, Setting.NameWords))
                .Select(a => (Kind: "randart", a.Id, Name: Setting.ArtifactName(a.Id) ?? a.Name)))
            .ToList();
        var offending = shown.Where(s => Tolkien().IsMatch(s.Name)).Select(s => $"{s.Kind} {s.Id}: {s.Name}").ToList();
        Assert.True(offending.Count == 0, "Tolkien names on screen:\n" + string.Join("\n", offending));
    }

    [Fact]
    public void Sprite_coverage_is_reported()
    {
        var families = Bridge.Monsters.GroupBy(m => m.Sprite ?? "(paper)").OrderByDescending(g => g.Count()).ToList();
        foreach (var family in families)
            output.WriteLine($"{family.Key}: {family.Count()}");
        var flare = Bridge.Monsters.Count(m => m.Sprite is not null);
        output.WriteLine($"Flare art for {flare} of {Bridge.Monsters.Count} monsters; the rest stand as their DCSS tiles.");
        Assert.True(flare * 2 > Bridge.Monsters.Count);
    }
}
