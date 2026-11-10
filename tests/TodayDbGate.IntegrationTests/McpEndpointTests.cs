using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;
using TodayDbGate.Domain.Enums;
using TodayDbGate.Infrastructure.Adapters;

namespace TodayDbGate.IntegrationTests;

/// <summary>Starts the real app in memory with its own data directory.</summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "integration-test-admin-pw";

    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "tdg-it-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:DataDirectory", DataDirectory);
        builder.UseSetting("Admin:InitialPassword", AdminPassword);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDirectory, recursive: true); } catch (IOException) { }
    }
}

public sealed class McpEndpointTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private const string LivePgVar = "TDG_IT_POSTGRES";

    private async Task<(CreatedToken Token, int GrantedId)> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<ConnectionService>();
        var tokens = scope.ServiceProvider.GetRequiredService<TokenService>();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        // Nothing listens on port 1, so this one always fails to connect.
        var unreachable = await connections.CreateAsync(new ConnectionInput
        {
            AliasName = "unreachable-" + suffix, Host = "127.0.0.1", Port = 1,
            Database = "app", Username = "reader", Password = "pw"
        });
        var other = await connections.CreateAsync(new ConnectionInput
        {
            AliasName = "other-" + suffix, Host = "127.0.0.1", Port = 1,
            Database = "app", Username = "reader", Password = "pw"
        });

        var grants = new List<TokenGrantInput> { new() { ConnectionId = unreachable.Value } };

        var livePg = Environment.GetEnvironmentVariable(LivePgVar);
        if (!string.IsNullOrWhiteSpace(livePg))
        {
            var d = new PostgresAdapter().ParseConnectionString(livePg);
            var live = await connections.CreateAsync(new ConnectionInput
            {
                AliasName = "live-pg-" + suffix, Host = d.Host, Port = d.Port,
                Database = d.Database, Username = d.Username, Password = d.Password
            });
            grants.Add(new TokenGrantInput { ConnectionId = live.Value });
        }

        var token = await tokens.CreateAsync(new TokenInput { Name = "it-" + suffix, Grants = grants });
        Assert.True(unreachable.Succeeded && other.Succeeded && token.Succeeded);
        return (token.Value!, unreachable.Value);
    }

    private async Task<McpClient> ConnectAsync(string token)
    {
        var http = factory.CreateClient();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
        }, http, NullLoggerFactory.Instance, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private async Task<HttpResponseMessage> RawPostAsync(string? token)
    {
        var http = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    [Fact]
    public async Task Requests_without_a_valid_token_get_401()
    {
        var missing = await RawPostAsync(null);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Contains("Bearer", missing.Headers.WwwAuthenticate.ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await RawPostAsync("tdg_made-up-token")).StatusCode);
    }

    [Fact]
    public async Task Data_directory_override_is_used() =>
        Assert.True(File.Exists(Path.Combine(factory.DataDirectory, "gateway.db")));

    [Fact]
    public async Task Tools_are_listed_and_connections_show_only_granted_aliases()
    {
        var (token, _) = await SeedAsync();
        await using var client = await ConnectAsync(token.PlainToken);

        var tools = await client.ListToolsAsync();
        Assert.Equal(["describe_schema", "execute_query", "list_connections"], tools.Select(t => t.Name).Order());

        var text = Text(await client.CallToolAsync("list_connections"));
        using var json = JsonDocument.Parse(text);
        var aliases = json.RootElement.EnumerateArray().Select(e => e.GetProperty("alias").GetString()).ToList();

        Assert.Contains(aliases, a => a!.StartsWith("unreachable-"));
        Assert.DoesNotContain(aliases, a => a!.StartsWith("other-"));
        Assert.DoesNotContain("127.0.0.1", text);
        Assert.DoesNotContain("reader", text);
    }

    [Fact]
    public async Task Tool_errors_are_safe_and_readable()
    {
        var (token, _) = await SeedAsync();
        await using var client = await ConnectAsync(token.PlainToken);
        var alias = JsonDocument.Parse(Text(await client.CallToolAsync("list_connections")))
            .RootElement.EnumerateArray().Select(e => e.GetProperty("alias").GetString()!).Single(a => a.StartsWith("unreachable-"));

        var write = await client.CallToolAsync("execute_query", new Dictionary<string, object?> { ["alias"] = alias, ["sql"] = "DELETE FROM t" });
        Assert.True(write.IsError);
        Assert.Contains("read-only", Text(write));

        var notGranted = await client.CallToolAsync("execute_query", new Dictionary<string, object?> { ["alias"] = "other-x", ["sql"] = "SELECT 1" });
        Assert.True(notGranted.IsError);
        Assert.Contains("not available", Text(notGranted));

        var down = await client.CallToolAsync("describe_schema", new Dictionary<string, object?> { ["alias"] = alias });
        Assert.True(down.IsError);
        Assert.Contains("Could not connect to the database.", Text(down));
        Assert.DoesNotContain("127.0.0.1", Text(down));
    }

    [Fact]
    public async Task Deactivated_token_is_refused_on_the_next_request()
    {
        var (token, _) = await SeedAsync();
        await using var client = await ConnectAsync(token.PlainToken);
        await client.CallToolAsync("list_connections");

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TokenService>().SetActiveAsync(token.Id, false);

        await Assert.ThrowsAnyAsync<Exception>(() => client.CallToolAsync("list_connections").AsTask());
        Assert.Equal(HttpStatusCode.Unauthorized, (await RawPostAsync(token.PlainToken)).StatusCode);
    }

    [IntegrationFact(LivePgVar)]
    public async Task Live_postgres_query_and_schema_through_mcp()
    {
        var (token, _) = await SeedAsync();
        await using var client = await ConnectAsync(token.PlainToken);
        var alias = JsonDocument.Parse(Text(await client.CallToolAsync("list_connections")))
            .RootElement.EnumerateArray().Select(e => e.GetProperty("alias").GetString()!).Single(a => a.StartsWith("live-pg-"));

        var result = await client.CallToolAsync("execute_query", new Dictionary<string, object?>
        {
            ["alias"] = alias,
            ["sql"] = "SELECT 42 AS answer, 'hello' AS greeting, now() AS at, NULL AS nothing"
        });
        Assert.NotEqual(true, result.IsError);

        using var json = JsonDocument.Parse(Text(result));
        Assert.Equal(["answer", "greeting", "at", "nothing"], json.RootElement.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        var row = json.RootElement.GetProperty("rows")[0];
        Assert.Equal(42, row[0].GetInt32());
        Assert.Equal("hello", row[1].GetString());
        Assert.Equal(JsonValueKind.Null, row[3].ValueKind);

        var schema = await client.CallToolAsync("describe_schema", new Dictionary<string, object?> { ["alias"] = alias });
        Assert.NotEqual(true, schema.IsError);
    }
}
