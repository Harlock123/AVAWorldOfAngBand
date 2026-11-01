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

/// <summary>A Strike or a monster's blow, from one square to another, and how it went.</summary>
public sealed record StrikeEvent(Loc From, Loc To, bool ByHero, WorldOfAngBand.Rules.Rules.DegreeOfSuccess Degree, int Damage) : IGameEvent;

public sealed record MonsterMovedEvent(int Id, Loc From, Loc To) : IGameEvent;

/// <summary>A monster came into the hero's sight (which stops a journey or a rest).</summary>
public sealed record MonsterAppearedEvent(int Id, string Name) : IGameEvent;

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

/// <summary>A monster's spell or innate attack, from where it stands toward the hero (the view draws its flash).</summary>
public sealed record SpellEvent(Loc From, Loc To, WorldOfAngBand.Engine.Definitions.MonsterSpellKind Kind, string? Element, int Radius) : IGameEvent;

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
