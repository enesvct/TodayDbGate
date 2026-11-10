namespace TodayDbGate.Infrastructure.Audit;

/// <summary>Bound from the "Audit" section of appsettings.json.</summary>
public sealed class AuditOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Store query results (compressed and encrypted). When false, only inputs and metadata are kept.</summary>
    public bool StoreOutput { get; set; } = true;

    /// <summary>Entries older than this are deleted.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>Rows of a result kept per entry. The full row count is always recorded.</summary>
    public int MaxStoredRows { get; set; } = 50;

    /// <summary>Upper bound for one entry's output before compression.</summary>
    public int MaxStoredOutputKb { get; set; } = 64;

    /// <summary>Longer SQL is cut to this length.</summary>
    public int MaxInputChars { get; set; } = 20_000;

    /// <summary>When the audit database grows past this, the oldest entries are deleted.</summary>
    public int MaxDatabaseMb { get; set; } = 1024;

    /// <summary>Events waiting to be written. If the writer falls this far behind, new events are dropped and counted.</summary>
    public int QueueCapacity { get; set; } = 10_000;
}
