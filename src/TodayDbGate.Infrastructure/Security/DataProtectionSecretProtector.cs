using Microsoft.AspNetCore.DataProtection;
using TodayDbGate.Application.Abstractions;

namespace TodayDbGate.Infrastructure.Security;

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    // Changing this purpose string makes every stored connection string unreadable.
    private readonly IDataProtector _protector = provider.CreateProtector("TodayDbGate.ConnectionStrings.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
