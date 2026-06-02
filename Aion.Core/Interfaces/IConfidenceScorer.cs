namespace Aion.Core.Interfaces;

public record ConfidenceScore(double Score, List<string> Signals);

public interface IConfidenceScorer
{
    ConfidenceScore Score(string rawInput, string parsedJson, string? toolName);
}
