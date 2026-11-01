namespace WorldOfAngBand.Rules.Rules;

/// <summary>Thrown when a choice breaks the game's rules (an illegal boost, an unknown definition, and so on).</summary>
public sealed class RulesException(string message) : Exception(message);
