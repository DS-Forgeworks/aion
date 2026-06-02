namespace Aion.Core.Interfaces;

public record ParseResult(bool Success, string? Json, string? Error, int StepsUsed, bool IsHeavy);

public interface IJsonRepairer
{
    ParseResult Repair(string rawOutput, bool forceHeavy = false);
}
