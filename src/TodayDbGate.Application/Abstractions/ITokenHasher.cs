namespace TodayDbGate.Application.Abstractions;

/// <summary>One-way hashing for access tokens. The same token always yields the same hash.</summary>
public interface ITokenHasher
{
    string Hash(string token);
}
