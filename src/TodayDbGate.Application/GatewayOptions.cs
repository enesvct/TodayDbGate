namespace TodayDbGate.Application;

/// <summary>Bound from the "Gateway" section of appsettings.json.</summary>
public sealed class GatewayOptions
{
    /// <summary>
    /// Rows returned per query at most; the result is marked truncated beyond this.
    /// 0 (the default) means no limit: every row is read into memory and returned, so a very large
    /// result can use a lot of memory on the gateway.
    /// </summary>
    public int MaxRows { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int MaxSqlLength { get; set; } = 100_000;
}
