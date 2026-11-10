namespace TodayDbGate.Application.Models;

/// <summary>Outcome of an admin operation; <see cref="Errors"/> are meant to be shown in the dashboard.</summary>
public record OperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static OperationResult Success() => new(true, []);
    public static OperationResult Failure(params string[] errors) => new(false, errors);
    public static OperationResult Failure(IReadOnlyList<string> errors) => new(false, errors);
}

public sealed record OperationResult<T>(bool Succeeded, T? Value, IReadOnlyList<string> Errors)
    : OperationResult(Succeeded, Errors)
{
    public static OperationResult<T> Success(T value) => new(true, value, []);
    public static new OperationResult<T> Failure(params string[] errors) => new(false, default, errors);
    public static new OperationResult<T> Failure(IReadOnlyList<string> errors) => new(false, default, errors);
}
