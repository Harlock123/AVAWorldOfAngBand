using WorldOfAngBand.Engine.Geometry;
using WorldOfAngBand.Engine.Time;

namespace WorldOfAngBand.Game;

/// <summary>The Lantern-sworn the player plays: where they stand and how they take their turns.</summary>
public sealed class Hero : IActor
{
    /// <summary>A wooden torch's light, as in Angband 4.2.</summary>
    public const int TorchRadius = 2;

    public int ActorId => 0;

    /// <summary>Speed relative to normal: +10 is twice as many actions.</summary>
    public int Speed { get; set; }

    public int Energy { get; set; }

    public bool IsActive => true;

    public Loc Position { get; set; }

    /// <summary>The way the hero last moved, for which way their sprite faces.</summary>
    public Direction Facing { get; set; } = Direction.South;

    public int MaxDepth { get; set; }

    public int LightRadius { get; set; } = TorchRadius;
}
