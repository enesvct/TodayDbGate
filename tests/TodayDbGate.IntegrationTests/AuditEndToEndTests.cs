using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;
using TodayDbGate.Infrastructure.Adapters;

namespace TodayDbGate.IntegrationTests;

/// <summary>MCP calls end up in audit.db with input and output, and are visible in the dashboard.</summary>
public sealed class AuditEndToEndTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private async Task<(string Token, string TokenName, string Alias)> SeedAsync(string? livePg)
    {
        using var scope = factory.Services.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<ConnectionService>();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var input = new ConnectionInput
        {
            AliasName = "audit-" + suffix, Host = "127.0.0.1", Port = 1, Database = "app", Username = "u", Password = "p"
        };
        if (livePg is not null)
        {
            var d = new PostgresAdapter().ParseConnectionString(livePg);
            (input.Host, input.Port, input.Database, input.Username, input.Password) = (d.Host, d.Port, d.Database, d.Username, d.Password);
        }
        var connection = await connections.CreateAsync(input);
        var token = await scope.ServiceProvider.GetRequiredService<TokenService>().CreateAsync(new TokenInput
        {
            Name = "audit-token-" + suffix,
            Grants = [new TokenGrantInput { ConnectionId = connection.Value }]
        });
        return (token.Value!.PlainToken, "audit-token-" + suffix, input.AliasName);
    }

    private async Task<McpClient> ConnectAsync(string token)
    {
        var http = factory.CreateClient();
        return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/mcp"),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
        }, http, NullLoggerFactory.Instance, ownsHttpClient: true));
    }

    /// <summary>The writer runs in the background; wait until the expected number of entries is there.</summary>
    private async Task<AuditPage> WaitForEntriesAsync(string tokenName, int expected)
    {
        var reader = factory.Services.GetRequiredService<IAuditLogReader>();
        for (var i = 0; i < 100; i++)
        {
            var page = await reader.SearchAsync(new AuditFilter { Token = tokenName }, default);
            if (page.Total >= expected) return page;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Expected {expected} audit entries for {tokenName}.");
    }

    [Fact]
    public async Task Every_tool_call_is_recorded_with_status_and_input()
    {
        var (token, tokenName, alias) = await SeedAsync(null);
        await using (var client = await ConnectAsync(token))
        {
            await client.CallToolAsync("list_connections");
            await client.CallToolAsync("execute_query", new Dictionary<string, object?> { ["alias"] = alias, ["sql"] = "DROP TABLE users" });
            await client.CallToolAsync("execute_query", new Dictionary<string, object?> { ["alias"] = "not-mine", ["sql"] = "SELECT 1" });
            await client.CallToolAsync("execute_query", new Dictionary<string, object?> { ["alias"] = alias, ["sql"] = "SELECT 1" });
        }

        var page = await WaitForEntriesAsync(tokenName, 4);
        var byTime = page.Items.OrderBy(e => e.Id).ToList();
        Assert.Equal(
            [("list_connections", AuditStatus.Success), ("execute_query", AuditStatus.Rejected),
             ("execute_query", AuditStatus.Denied), ("execute_query", AuditStatus.Failed)],
            byTime.Select(e => (e.Tool, e.Status)));
        Assert.Equal("DROP TABLE users", byTime[1].InputPreview);

        var reader = factory.Services.GetRequiredService<IAuditLogReader>();
        var listed = (await reader.GetAsync(byTime[0].Id, default))!;
        Assert.Contains(alias, listed.OutputJson);

        var failed = (await reader.GetAsync(byTime[3].Id, default))!;
        Assert.StartsWith("Could not connect to the database.", failed.Error);

        // The dashboard shows the list and the details.
        var admin = await SignInAsync();
        var list = await admin.GetStringAsync($"/audit?token={tokenName}");
        Assert.Contains("DROP TABLE users", list);
        var details = await admin.GetAsync($"/audit/details/{byTime[1].Id}");
        Assert.Contains("no-store", details.Headers.CacheControl!.ToString());
        Assert.Contains("read-only", await details.Content.ReadAsStringAsync());
    }

    [IntegrationFact("TDG_IT_POSTGRES")]
    public async Task Query_output_is_stored_and_shown()
    {
        var (token, tokenName, alias) = await SeedAsync(Environment.GetEnvironmentVariable("TDG_IT_POSTGRES"));
        await using (var client = await ConnectAsync(token))
        {
            var result = await client.CallToolAsync("execute_query", new Dictionary<string, object?>
            {
                ["alias"] = alias,
                ["sql"] = "SELECT g AS n, 'row ' || g AS label FROM generate_series(1, 120) g"
            });
            Assert.NotEqual(true, result.IsError);
        }

        var entry = (await WaitForEntriesAsync(tokenName, 1)).Items.Single();
        Assert.Equal(120, entry.RowCount);

        var detail = (await factory.Services.GetRequiredService<IAuditLogReader>().GetAsync(entry.Id, default))!;
        Assert.True(detail.OutputTruncated); // 120 rows returned, 50 stored by default
        Assert.Contains("\"storedRows\":50", detail.OutputJson);
        Assert.Contains("row 1", detail.OutputJson);

        var admin = await SignInAsync();
        var html = await admin.GetStringAsync($"/audit/details/{entry.Id}");
        Assert.Contains("Stored 50 of 120 rows", html);
        Assert.Contains("row 50", html);
    }

    private async Task<HttpClient> SignInAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var html = await client.GetStringAsync("/account/login");
        var marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var af = WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]);
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin", ["Password"] = GatewayFactory.AdminPassword, ["__RequestVerificationToken"] = af
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }
}
