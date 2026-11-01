using WorldOfAngBand.Engine.Geometry;

namespace WorldOfAngBand.Game;

/// <summary>What the player asks the hero to do; <see cref="DungeonSession.Perform"/> carries it out.</summary>
public abstract record GameCommand;

/// <summary>Step one square (opening a closed door in the way, as Angband's easy_open does).</summary>
public sealed record Walk(Direction Direction) : GameCommand;

/// <summary>Stay where you are for a turn.</summary>
public sealed record Hold : GameCommand;

/// <summary>Take the staircase underfoot: down with <see cref="Down"/>, else up.</summary>
public sealed record TakeStairs(bool Down) : GameCommand;

/// <summary>Rest a turn, healing (a full rest takes <see cref="DungeonSession.TurnsToRestFully"/> turns); not with monsters in view.</summary>
public sealed record Rest : GameCommand;

/// <summary>Drink from a healing flask.</summary>
public sealed record QuaffFlask : GameCommand;

/// <summary>Pick up what lies here.</summary>
public sealed record PickUp : GameCommand;

/// <summary>Put down some of a carried item.</summary>
public sealed record DropItem(WorldOfAngBand.Engine.Items.Item Item, int Count = 1) : GameCommand;

/// <summary>Wield or wear an item from the pack (or the floor).</summary>
public sealed record WieldItem(WorldOfAngBand.Engine.Items.Item Item) : GameCommand;

/// <summary>Take off a worn item.</summary>
public sealed record TakeOffItem(WorldOfAngBand.Engine.Items.Item Item) : GameCommand;

/// <summary>Drink a potion or read a scroll; <see cref="Target"/> is what an enchantment or identification is for.</summary>
public sealed record UseItem(WorldOfAngBand.Engine.Items.Item Item, WorldOfAngBand.Engine.Items.Item? Target = null) : GameCommand;

/// <summary>Choose at a story level's altar (one of <see cref="Story.Choices"/>).</summary>
public sealed record ChooseStory(string StoryId, int Choice) : GameCommand;

/// <summary>Use a class ability (by id) on the monster at <see cref="Target"/>, or the nearest one in sight.</summary>
public sealed record UsePower(string Id, Loc? Target = null) : GameCommand;

/// <summary>Make a level's picks (Pathfinder's feats, a skill increase, boosts). Takes no time.</summary>
public sealed record ChooseLevel(int Level, WorldOfAngBand.Rules.Characters.LevelChoice Choice) : GameCommand;

/// <summary>Activate a worn artifact, at the monster at <see cref="Target"/> when it attacks (else the nearest in sight).</summary>
public sealed record ActivateItem(WorldOfAngBand.Engine.Items.Item Item, Loc? Target = null) : GameCommand;
