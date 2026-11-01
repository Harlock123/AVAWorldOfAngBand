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
