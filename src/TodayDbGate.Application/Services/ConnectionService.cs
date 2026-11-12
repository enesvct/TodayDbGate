using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Exceptions;
using TodayDbGate.Application.Models;
using TodayDbGate.Domain.Entities;

namespace TodayDbGate.Application.Services;

/// <summary>Admin operations on the connection registry.</summary>
public sealed class ConnectionService(
    IConnectionRepository connections,
    IDbAdapterFactory adapters,
    ISecretProtector protector,
    TimeProvider time)
{
    private static readonly Regex AliasPattern = new("^[a-z0-9][a-z0-9_-]{1,99}$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<ConnectionSummary>> ListAsync(CancellationToken ct = default)
    {
        var entries = await connections.ListAsync(ct);
        return entries.Select(ToSummary).ToList();
    }

    /// <summary>Form data for editing. The password is left empty on purpose.</summary>
    public async Task<ConnectionInput?> GetForEditAsync(int id, CancellationToken ct = default)
    {
        var entry = await connections.GetByIdAsync(id, ct);
        if (entry is null) return null;

        var details = ReadDetails(entry);
        return new ConnectionInput
        {
            AliasName = entry.AliasName,
            DatabaseType = entry.DatabaseType,
            Host = details.Host,
            Port = details.Port,
            Database = details.Database,
            Username = details.Username,
            TrustServerCertificate = details.TrustServerCertificate,
            Description = entry.Description
        };
    }

    public async Task<OperationResult<int>> CreateAsync(ConnectionInput input, CancellationToken ct = default)
    {
        var alias = NormalizeAlias(input.AliasName);
        var errors = Validate(input, alias, passwordRequired: true);
        if (errors.Count == 0 && await connections.AliasExistsAsync(alias, null, ct))
            errors.Add($"The alias '{alias}' is already in use.");
        if (errors.Count > 0) return OperationResult<int>.Failure(errors);

        var adapter = adapters.Get(input.DatabaseType);
        var entry = new DbConnectionEntry
        {
            AliasName = alias,
            DatabaseType = input.DatabaseType,
            ConnectionStringEncrypted = protector.Protect(adapter.BuildConnectionString(ToDetails(input, adapter, input.Password!))),
            Description = Clean(input.Description),
            CreatedAt = time.GetUtcNow().UtcDateTime
        };

        await connections.AddAsync(entry, ct);
        return OperationResult<int>.Success(entry.Id);
    }

    public async Task<OperationResult> UpdateAsync(int id, ConnectionInput input, CancellationToken ct = default)
    {
        var entry = await connections.GetByIdAsync(id, ct);
        if (entry is null) return OperationResult.Failure("Connection not found.");

        var alias = NormalizeAlias(input.AliasName);
        var errors = Validate(input, alias, passwordRequired: false);
        if (errors.Count == 0 && await connections.AliasExistsAsync(alias, id, ct))
            errors.Add($"The alias '{alias}' is already in use.");
        if (errors.Count > 0) return OperationResult.Failure(errors);

        var password = string.IsNullOrEmpty(input.Password) ? ReadDetails(entry).Password : input.Password;
        var adapter = adapters.Get(input.DatabaseType);

        entry.AliasName = alias;
        entry.DatabaseType = input.DatabaseType;
        entry.ConnectionStringEncrypted = protector.Protect(adapter.BuildConnectionString(ToDetails(input, adapter, password)));
        entry.Description = Clean(input.Description);
        entry.UpdatedAt = time.GetUtcNow().UtcDateTime;

        await connections.SaveChangesAsync(ct);
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entry = await connections.GetByIdAsync(id, ct);
        if (entry is null) return OperationResult.Failure("Connection not found.");

        await connections.DeleteAsync(entry, ct);
        return OperationResult.Success();
    }

    /// <summary>Tests a stored connection.</summary>
    public async Task<ConnectionTestResult> TestAsync(int id, CancellationToken ct = default)
    {
        var entry = await connections.GetByIdAsync(id, ct);
        if (entry is null) return new ConnectionTestResult(false, "Connection not found.", 0);

        return await RunTestAsync(adapters.Get(entry.DatabaseType), protector.Unprotect(entry.ConnectionStringEncrypted), ct);
    }

    /// <summary>
    /// Tests form values before saving. When editing (<paramref name="existingId"/> set) and the password
    /// field is empty, the stored password is used.
    /// </summary>
    public async Task<ConnectionTestResult> TestAsync(ConnectionInput input, int? existingId, CancellationToken ct = default)
    {
        var password = input.Password;
        if (string.IsNullOrEmpty(password) && existingId is not null)
        {
            var entry = await connections.GetByIdAsync(existingId.Value, ct);
            if (entry is not null) password = ReadDetails(entry).Password;
        }

        var errors = Validate(input, NormalizeAlias(input.AliasName), passwordRequired: false, aliasRequired: false);
        if (errors.Count > 0) return new ConnectionTestResult(false, string.Join(" ", errors), 0);

        var adapter = adapters.Get(input.DatabaseType);
        return await RunTestAsync(adapter, adapter.BuildConnectionString(ToDetails(input, adapter, password ?? "")), ct);
    }

    private static async Task<ConnectionTestResult> RunTestAsync(IDbAdapter adapter, string connectionString, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await adapter.TestConnectionAsync(connectionString, ct);
            return new ConnectionTestResult(true, "Connection successful.", sw.ElapsedMilliseconds);
        }
        catch (GatewayException ex)
        {
            // The admin may see the real driver message (it helps fixing host/port/credentials).
            return new ConnectionTestResult(false, ex.InnerException?.Message ?? ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private ConnectionSummary ToSummary(DbConnectionEntry entry)
    {
        ConnectionDetails? details = null;
        try { details = ReadDetails(entry); }
        catch (CryptographicException) { /* keys lost or rotated away; still list the entry */ }

        return new ConnectionSummary(
            entry.Id, entry.AliasName, entry.DatabaseType,
            details?.Host ?? "(unreadable)", details?.Port ?? 0, details?.Database ?? "", details?.Username ?? "",
            entry.Description, entry.CreatedAt, entry.TokenConnections.Count,
            entry.MaskingRules.Count, entry.DetectPersonalData);
    }

    private ConnectionDetails ReadDetails(DbConnectionEntry entry) =>
        adapters.Get(entry.DatabaseType).ParseConnectionString(protector.Unprotect(entry.ConnectionStringEncrypted));

    private static ConnectionDetails ToDetails(ConnectionInput input, IDbAdapter adapter, string password) =>
        new(input.Host.Trim(), input.Port ?? adapter.DefaultPort, input.Database.Trim(), input.Username.Trim(),
            password, input.TrustServerCertificate);

    private static List<string> Validate(ConnectionInput input, string alias, bool passwordRequired, bool aliasRequired = true)
    {
        var errors = new List<string>();
        if (aliasRequired && !AliasPattern.IsMatch(alias))
            errors.Add("Alias must be 2-100 characters: lowercase letters, digits, '-' or '_', starting with a letter or digit.");
        if (!Enum.IsDefined(input.DatabaseType)) errors.Add("Unknown database type.");
        if (string.IsNullOrWhiteSpace(input.Host)) errors.Add("Host is required.");
        if (input.Port is < 1 or > 65535) errors.Add("Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(input.Database)) errors.Add("Database name is required.");
        if (string.IsNullOrWhiteSpace(input.Username)) errors.Add("Username is required.");
        if (passwordRequired && string.IsNullOrEmpty(input.Password)) errors.Add("Password is required.");
        if (input.Description?.Length > 500) errors.Add("Description must be at most 500 characters.");
        return errors;
    }

    private static string NormalizeAlias(string? alias) => (alias ?? "").Trim().ToLowerInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
