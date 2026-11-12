using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using TodayDbGate.Application.Security;
using TodayDbGate.Infrastructure.Security;

namespace TodayDbGate.UnitTests;

public class SecurityTests
{
    [Fact]
    public void Generated_tokens_have_prefix_and_are_unique()
    {
        var a = AccessTokenGenerator.Generate();
        var b = AccessTokenGenerator.Generate();

        Assert.StartsWith(AccessTokenGenerator.Prefix, a);
        Assert.Equal(47, a.Length); // "tdg_" + 43 base64url chars
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Hmac_hash_is_deterministic_and_depends_on_key()
    {
        var token = AccessTokenGenerator.Generate();
        var hasher1 = new HmacTokenHasher(RandomNumberGenerator.GetBytes(32));
        var hasher2 = new HmacTokenHasher(RandomNumberGenerator.GetBytes(32));

        Assert.Equal(hasher1.Hash(token), hasher1.Hash(token));
        Assert.NotEqual(hasher1.Hash(token), hasher2.Hash(token));
        Assert.Equal(64, hasher1.Hash(token).Length);
    }

    [Fact]
    public void Hmac_hasher_rejects_short_key()
    {
        Assert.Throws<ArgumentException>(() => new HmacTokenHasher(new byte[16]));
    }

    [Fact]
    public void Secret_protector_round_trips_and_hides_plaintext()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        const string cs = "Host=db;Database=app;Username=reader;Password=s3cret";

        var encrypted = protector.Protect(cs);

        Assert.DoesNotContain("s3cret", encrypted);
        Assert.Equal(cs, protector.Unprotect(encrypted));
    }

    [Fact]
    public void Token_hash_key_is_created_once_and_reloaded()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tdg-test-" + Guid.NewGuid());
        var path = Path.Combine(dir, "token-hash.key");
        var provider = new EphemeralDataProtectionProvider();
        try
        {
            var first = new ProtectedKeyFile(provider, path, "TodayDbGate.TokenHashKey.v1").GetKey();
            var second = new ProtectedKeyFile(provider, path, "TodayDbGate.TokenHashKey.v1").GetKey();

            Assert.Equal(32, first.Length);
            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
