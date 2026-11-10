namespace TodayDbGate.Application;

/// <summary>Bound from the "Gateway" section of appsettings.json.</summary>
public sealed class GatewayOptions
{
    /// <summary>Rows returned per query at most; the result is marked truncated beyond this.</summary>
    public int MaxRows { get; set; } = 500;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int MaxSqlLength { get; set; } = 100_000;
}
