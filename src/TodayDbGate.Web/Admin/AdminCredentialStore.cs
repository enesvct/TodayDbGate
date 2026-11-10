using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using TodayDbGate.Application.Models;
using TodayDbGate.Infrastructure;

namespace TodayDbGate.Web.Admin;

/// <summary>
/// The single dashboard account, stored as a PBKDF2 hash in App_Data/admin.json.
/// On first start the password comes from Admin:InitialPassword, or is generated and written to the log once.
/// </summary>
public sealed class AdminCredentialStore(StoragePaths paths)
{
    public const string Username = "admin";
    public const int MinPasswordLength = 12;

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private readonly string _path = Path.Combine(paths.DataDirectory, "admin.json");
    private readonly PasswordHasher<string> _hasher = new();
    private readonly Lock _lock = new();

    private sealed record AdminRecord(string Username, string PasswordHash);

    public void EnsureInitialized(string? initialPassword, ILogger logger)
    {
        lock (_lock)
        {
            if (File.Exists(_path)) return;

            var generated = string.IsNullOrEmpty(initialPassword);
            var password = generated ? RandomNumberGenerator.GetString(Alphabet, 20) : initialPassword!;
            Save(new AdminRecord(Username, _hasher.HashPassword(Username, password)));

            if (generated)
                logger.LogWarning(
                    "Created dashboard account '{User}' with password {Password} . It is shown only this once; change it after signing in.",
                    Username, password);
            else
                logger.LogInformation("Created dashboard account '{User}' from Admin:InitialPassword.", Username);
        }
    }

    public bool Verify(string? username, string? password)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;

        lock (_lock)
        {
            var record = Load();
            var userMatches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(username.Trim().ToLowerInvariant()), Encoding.UTF8.GetBytes(record.Username));
            var result = _hasher.VerifyHashedPassword(record.Username, record.PasswordHash, password);

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
                Save(record with { PasswordHash = _hasher.HashPassword(record.Username, password) });

            return userMatches && result != PasswordVerificationResult.Failed;
        }
    }

    public OperationResult ChangePassword(string? currentPassword, string? newPassword)
    {
        if (!Verify(Username, currentPassword))
            return OperationResult.Failure("Current password is incorrect.");
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < MinPasswordLength)
            return OperationResult.Failure($"New password must be at least {MinPasswordLength} characters.");

        lock (_lock)
        {
            Save(new AdminRecord(Username, _hasher.HashPassword(Username, newPassword)));
        }
        return OperationResult.Success();
    }

    private AdminRecord Load() =>
        JsonSerializer.Deserialize<AdminRecord>(File.ReadAllText(_path))
        ?? throw new InvalidOperationException($"{_path} is empty or invalid.");

    private void Save(AdminRecord record)
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(record));
        File.Move(tmp, _path, overwrite: true);
    }
}
