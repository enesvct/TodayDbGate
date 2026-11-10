namespace TodayDbGate.Application.Abstractions;

/// <summary>Reversible encryption for secrets we need to read back (connection strings).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
