using WorldOfAngBand.Engine.Geometry;

namespace WorldOfAngBand.Game;

/// <summary>Something that happened in the game, for the view, the sound and the message log to follow.</summary>
public interface IGameEvent;

/// <summary>A line for the message log.</summary>
public sealed record MessageEvent(string Text) : IGameEvent;

public sealed record HeroMovedEvent(Loc From, Loc To) : IGameEvent;

public sealed record DoorOpenedEvent(Loc At) : IGameEvent;

/// <summary>The hero arrived on a new level (0 is the town).</summary>
public sealed record LevelEnteredEvent(int Depth) : IGameEvent;

/// <summary>
/// A Strike or a monster's blow, from one square to another, and how it went: the blow's method if a monster struck
/// (Angband's: bite, claw, sting...; Ashen's creatures hit, shoot or cast), and how much a shield or ward took.
/// </summary>
public sealed record StrikeEvent(Loc From, Loc To, bool ByHero, WorldOfAngBand.Rules.Rules.DegreeOfSuccess Degree, int Damage,
    string? Method = null, int Blocked = 0) : IGameEvent;

public sealed record MonsterMovedEvent(int Id, Loc From, Loc To) : IGameEvent;

/// <summary>A monster came into the hero's sight (which stops a journey or a rest).</summary>
public sealed record MonsterAppearedEvent(int Id, string Name, string? RaceId = null) : IGameEvent;

public sealed record MonsterDiedEvent(int Id, Loc At, string Name, string RaceId) : IGameEvent;

public sealed record LevelUpEvent(int Level) : IGameEvent;

/// <summary>A blow that would have killed the hero left them at 1 HP: Second Breath is spent.</summary>
public sealed record SecondBreathEvent : IGameEvent;

/// <summary>The hero is dead; the record is their line in the Order's book.</summary>
public sealed record HeroDiedEvent(DeathRecord Record) : IGameEvent;

/// <summary>The hero's things, or those on the floor, changed.</summary>
public sealed record ItemsChangedEvent : IGameEvent;

/// <summary>The hero walked into a shop (or their room at the inn).</summary>
public sealed record ShopEnteredEvent(string StoreId) : IGameEvent;

/// <summary>
/// A monster's spell or innate attack, from where it stands toward the hero (the view draws its flash), with the
/// spell's id (Angband's: BR_FIRE, BOLT, S_UNDEAD...) when a monster cast it.
/// </summary>
public sealed record SpellEvent(Loc From, Loc To, WorldOfAngBand.Engine.Definitions.MonsterSpellKind Kind, string? Element, int Radius,
    string? SpellId = null) : IGameEvent;

/// <summary>The hero stands at a story level's altar: what it is, and what they can choose.</summary>
public sealed record StoryChoiceEvent(string StoryId, string Title, string Prompt, IReadOnlyList<StoryChoice> Choices) : IGameEvent;

/// <summary>The Cinder of Ashur is out: the game is won, and the epilogue is these lines.</summary>
public sealed record HeroWonEvent(IReadOnlyList<string> Epilogue) : IGameEvent;

/// <summary>The hero has gone past level 20 into a mythic tier.</summary>
public sealed record MythicTierEvent(int Tier) : IGameEvent;

/// <summary>The hero came to a shrine: the three blessings Ashur's whim offers (ids from <see cref="Province.Shrines"/>).</summary>
public sealed record ShrineEvent(string ShrineId, string Name, IReadOnlyList<string> Blessings) : IGameEvent;

/// <summary>The hero read something in the province (it is in the journal now).</summary>
public sealed record ReadingEvent(string Title, string Where, string Text) : IGameEvent;

/// <summary>The hero touched a Lantern Rift: where they can step through to (zone ids, with their names; "fort_gallowmere" is the town).</summary>
public sealed record RiftEvent(IReadOnlyList<(string Id, string Name)> Destinations) : IGameEvent;

/// <summary>Something worth hearing that no other event says (the spec's section 8.1). The sound decides what it sounds like.</summary>
public enum Cue
{
    DoorOpened,
    DoorBroken,
    StairsDown,
    StairsUp,
    /// <summary>Word of Recall or Deep Descent takes the hero.</summary>
    Recalled,
    /// <summary>Teleport Level, or a monster's spell, sends the hero up or down.</summary>
    LevelTeleported,
    /// <summary>The hero is blinked or teleported across the level.</summary>
    Teleported,
    GoldTaken,
    /// <summary>An item picked up; the detail is its base (sword, soft_armour, ring, potion...).</summary>
    ItemTaken,
    ItemDropped,
    /// <summary>An item worn or wielded; the detail is its base.</summary>
    Wielded,
    Quaffed,
    Read,
    Activated,
    /// <summary>A healing flask drunk.</summary>
    FlaskDrunk,
    /// <summary>An artifact or an ego item falls where the hero can see it.</summary>
    RareDrop,
    Bought,
    Sold,
    RuneEtched,
    CacheOpened,
    BlessingTaken,
    RiftStepped,
    /// <summary>The last of a zone's packs is slain.</summary>
    ZoneCleared,
}

/// <summary>A cue for the sound, at a square if it has one.</summary>
public sealed record CueEvent(Cue Cue, Loc? At = null, string? Detail = null) : IGameEvent;

/// <summary>
/// The hero used an ability (one of the bar's): its id, its kind (Strike, Spell, Blast, Ward, Heal...) and its damage
/// type, if any. A Strike's swing and hit come as <see cref="StrikeEvent"/>s too.
/// </summary>
public sealed record AbilityUsedEvent(string Id, string Kind, string? DamageType) : IGameEvent;
