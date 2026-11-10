using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Infrastructure.Adapters;

namespace TodayDbGate.UnitTests;

public class AdapterConnectionStringTests
{
    public static TheoryData<IDbAdapter> Adapters => [new PostgresAdapter(), new SqlServerAdapter(), new MySqlAdapter()];

    [Theory]
    [MemberData(nameof(Adapters))]
    public void Build_then_parse_round_trips(IDbAdapter adapter)
    {
        var details = new ConnectionDetails("db.example.local", 15000, "sales", "reader", "p@ss;word=\"x'", false);

        var parsed = adapter.ParseConnectionString(adapter.BuildConnectionString(details));

        Assert.Equal(details with { TrustServerCertificate = false }, parsed with { TrustServerCertificate = false });
    }

    [Fact]
    public void SqlServer_keeps_trust_server_certificate_and_default_port()
    {
        var adapter = new SqlServerAdapter();
        var details = new ConnectionDetails("sql01", 1433, "erp", "reader", "pw", TrustServerCertificate: true);

        var cs = adapter.BuildConnectionString(details);

        Assert.Contains("Data Source=sql01;", cs);
        Assert.Equal(details, adapter.ParseConnectionString(cs));
    }
}
