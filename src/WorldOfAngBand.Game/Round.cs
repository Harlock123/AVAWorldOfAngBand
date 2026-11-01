namespace WorldOfAngBand.Game;

/// <summary>
/// Pathfinder's round, counted in actions (the spec's section 4.1): three actions make a round. The multiple attack
/// penalty counts the Strikes made in the current round, and the reaction comes back when a new round starts.
/// </summary>
public sealed class Round
{
    public const int ActionsPerRound = 3;

    /// <summary>Actions taken so far in this round (0 to 2).</summary>
    public int Actions { get; private set; }

    /// <summary>Strikes made so far in this round.</summary>
    public int Strikes { get; private set; }

    public bool ReactionReady { get; private set; } = true;

    /// <summary>The multiple attack penalty the next Strike takes: 0, then −5 and −10 (−4 and −8 when agile).</summary>
    public int AttackPenalty(bool agile) => Strikes switch
    {
        0 => 0,
        1 => agile ? -4 : -5,
        _ => agile ? -8 : -10,
    };

    public void Strike() => Strikes++;

    public void UseReaction() => ReactionReady = false;

    /// <summary>One action is done; after the third a new round begins.</summary>
    public void EndAction(int actions = 1)
    {
        Actions += actions;
        if (Actions < ActionsPerRound)
            return;
        Actions = 0;
        Strikes = 0;
        ReactionReady = true;
    }
}
