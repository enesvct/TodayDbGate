using TodayDbGate.Application.Masking;
using TodayDbGate.Domain.Entities;

namespace TodayDbGate.Application.Abstractions;

/// <summary>Replaces a value with a stable alias: the same input always gives the same output, without revealing it.</summary>
public interface IPseudonymizer
{
    string Pseudonymize(string columnName, object value);
}

public interface IMaskingRepository
{
    Task<MaskingPolicy> GetPolicyAsync(int connectionId, CancellationToken ct);
    Task<List<MaskingRule>> GetRulesAsync(int connectionId, CancellationToken ct);

    /// <summary>Replaces all rules of the connection and its detection setting.</summary>
    Task SaveAsync(int connectionId, bool detectPersonalData, IReadOnlyList<MaskingRule> rules, CancellationToken ct);
}
