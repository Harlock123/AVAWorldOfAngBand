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
