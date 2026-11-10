namespace TodayDbGate.Infrastructure;

/// <summary>Where the gateway keeps its files (SQLite database, keys, admin account).</summary>
public sealed record StoragePaths(string DataDirectory);
