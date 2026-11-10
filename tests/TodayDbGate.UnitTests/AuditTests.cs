using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Infrastructure.Audit;

namespace TodayDbGate.UnitTests;

public class AuditOutputCodecTests
{
    private static AuditOutputCodec Codec(AuditOptions? options = null) =>
        new(new EphemeralDataProtectionProvider(), options ?? new AuditOptions());

    private static QueryResult Result(int rows, Func<int, object?[]> row) =>
        new(["id", "name", "note"], Enumerable.Range(1, rows).Select(row).ToList(), false, -1, 5);

    [Fact]
    public void Keeps_only_max_rows_but_records_how_many_were_returned()
    {
        var codec = Codec(new AuditOptions { MaxStoredRows = 50 });
        var encoded = codec.Encode(Result(200, i => [i, $"customer {i}", null]));

        Assert.True(encoded.Truncated);
        using var json = JsonDocument.Parse(codec.Decode(encoded.Blob));
        Assert.Equal(50, json.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(50, json.RootElement.GetProperty("storedRows").GetInt32());
        Assert.Equal(200, json.RootElement.GetProperty("returnedRows").GetInt32());
        Assert.Equal("customer 1", json.RootElement.GetProperty("rows")[0][1].GetString());
    }

    [Fact]
    public void Small_results_are_stored_whole()
    {
        var codec = Codec();
        var encoded = codec.Encode(Result(3, i => [i, "x", "y"]));
        Assert.False(encoded.Truncated);
        Assert.Contains("\"storedRows\":3", codec.Decode(encoded.Blob));
    }

    [Fact]
    public void Wide_rows_are_cut_to_the_byte_limit()
    {
        var codec = Codec(new AuditOptions { MaxStoredRows = 50, MaxStoredOutputKb = 16 });
        var wide = new string('x', 4000);
        var encoded = codec.Encode(Result(50, i => [i, wide, wide]));

        Assert.True(encoded.Truncated);
        Assert.True(encoded.JsonBytes <= 16 * 1024);
        using var json = JsonDocument.Parse(codec.Decode(encoded.Blob)); // still valid JSON: rows were dropped, not cut
        Assert.True(json.RootElement.GetProperty("storedRows").GetInt32() < 50);
    }

    [Fact]
    public void Stored_blob_is_compressed_and_encrypted()
    {
        var codec = Codec(new AuditOptions { MaxStoredRows = 500, MaxStoredOutputKb = 1024 });
        var encoded = codec.Encode(Result(500, i => [i, "Istanbul Kadikoy customer", "secret-value-123"]));

        Assert.DoesNotContain("secret-value-123", Encoding.UTF8.GetString(encoded.Blob));
        // Repetitive table data compresses well; expect at least 5x.
        Assert.True(encoded.Blob.Length * 5 < encoded.JsonBytes, $"{encoded.Blob.Length} vs {encoded.JsonBytes}");
    }

    [Fact]
    public void Other_outputs_are_stored_as_json()
    {
        var codec = Codec();
        var json = codec.Decode(codec.Encode(new[] { new { Table = "orders" } }).Blob);
        Assert.Equal("""[{"table":"orders"}]""", json);
    }
}

public class ChannelAuditSinkTests
{
    private static AuditEvent Event() =>
        new(DateTime.UtcNow, 1, "t", "execute_query", "db", "SELECT 1", AuditStatus.Success, null, 1);

    [Fact]
    public void Full_queue_drops_and_counts_instead_of_blocking()
    {
        var sink = new ChannelAuditSink(new AuditOptions { QueueCapacity = 2 });
        for (var i = 0; i < 5; i++) sink.Record(Event());

        Assert.Equal(2, sink.Reader.Count);
        Assert.Equal(3, sink.DroppedEvents);
    }

    [Fact]
    public void Disabled_audit_records_nothing()
    {
        var sink = new ChannelAuditSink(new AuditOptions { Enabled = false });
        sink.Record(Event());
        Assert.Equal(0, sink.Reader.Count);
    }
}

public sealed class AuditMaintenanceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"tdg-audit-{Guid.NewGuid():N}.db");
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class Factory(string path) : IDbContextFactory<AuditDbContext>
    {
        public AuditDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AuditDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private Factory CreateDatabase()
    {
        var factory = new Factory(_path);
        using var db = factory.CreateDbContext();
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA auto_vacuum = INCREMENTAL;");
        db.Database.EnsureCreated();
        return factory;
    }

    private static AuditLogEntry Entry(DateTime at, int outputBytes = 0) => new()
    {
        Timestamp = at, TokenId = 1, TokenName = "t", Tool = "execute_query", Status = "Success",
        Output = outputBytes > 0 ? RandomNumberGenerator.GetBytes(outputBytes) : null
    };

    private AuditMaintenance Maintenance(Factory factory, AuditOptions options) =>
        new(factory, options, new FixedTime(_now), NullLogger<AuditMaintenance>.Instance);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public async Task Deletes_entries_older_than_retention()
    {
        var factory = CreateDatabase();
        await using (var db = factory.CreateDbContext())
        {
            db.AuditLog.AddRange(Entry(_now.UtcDateTime.AddDays(-40)), Entry(_now.UtcDateTime.AddDays(-31)), Entry(_now.UtcDateTime.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var deleted = await Maintenance(factory, new AuditOptions { RetentionDays = 30 }).RunAsync(default);

        Assert.Equal(2, deleted);
        await using var check = factory.CreateDbContext();
        Assert.Equal(1, await check.AuditLog.CountAsync());
    }

    [Fact]
    public async Task Size_cap_removes_oldest_first_and_shrinks_the_file()
    {
        var factory = CreateDatabase();
        await using (var db = factory.CreateDbContext())
        {
            // ~3 MB of incompressible output across 300 recent entries.
            for (var i = 0; i < 300; i++) db.AuditLog.Add(Entry(_now.UtcDateTime.AddMinutes(-300 + i), 10_000));
            await db.SaveChangesAsync();
        }
        long fileBefore;
        await using (var db = factory.CreateDbContext()) fileBefore = await AuditMaintenance.GetFileBytesAsync(db, default);

        await Maintenance(factory, new AuditOptions { MaxDatabaseMb = 1 }).RunAsync(default);

        await using var check = factory.CreateDbContext();
        Assert.True(await AuditMaintenance.GetUsedBytesAsync(check, default) <= 1024 * 1024);
        Assert.True(await AuditMaintenance.GetFileBytesAsync(check, default) < fileBefore / 2, "file did not shrink");
        // The newest entry survived; the oldest did not.
        var ids = await check.AuditLog.Select(e => e.Id).ToListAsync();
        Assert.Contains(300L, ids);
        Assert.DoesNotContain(1L, ids);
    }
}
