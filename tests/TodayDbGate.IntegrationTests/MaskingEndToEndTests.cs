using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;
using TodayDbGate.Domain.Enums;
using TodayDbGate.Infrastructure.Adapters;

namespace TodayDbGate.IntegrationTests;

/// <summary>Masking configured in the dashboard, applied to real PostgreSQL results served over MCP.</summary>
public sealed class MaskingEndToEndTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private const string PgVar = "TDG_IT_POSTGRES";

    private async Task<HttpClient> SignInAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var af = Antiforgery(await client.GetStringAsync("/account/login"));
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin", ["Password"] = GatewayFactory.AdminPassword, ["__RequestVerificationToken"] = af
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static string Antiforgery(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)]);
    }

    private static string Text(CallToolResult r) => string.Concat(r.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [IntegrationFact(PgVar)]
    public async Task Rules_saved_in_the_dashboard_mask_mcp_results()
    {
        var pg = Environment.GetEnvironmentVariable(PgVar)!;
        var adapter = new PostgresAdapter();
        var table = "it_mask_people_" + Guid.NewGuid().ToString("N")[..6];
        var rw = new QueryLimits(0, 15);
        await adapter.ExecuteQueryAsync(pg, $"CREATE TABLE {table} (id int PRIMARY KEY, email text, full_name text, note text)", AccessLevel.ReadWrite, rw, default);
        await adapter.ExecuteQueryAsync(pg,
            $"INSERT INTO {table} VALUES (1, 'ayse@example.com', 'Ayşe Kaya', 'tel 0532 123 45 67'), (2, 'ayse@example.com', 'Ayşe Kaya', '-')",
            AccessLevel.ReadWrite, rw, default);
        try
        {
            // Connection and read-only token for the test.
            int connectionId;
            string token;
            using (var scope = factory.Services.CreateScope())
            {
                var d = adapter.ParseConnectionString(pg);
                var connection = await scope.ServiceProvider.GetRequiredService<ConnectionService>().CreateAsync(new ConnectionInput
                {
                    AliasName = "mask-" + table[^6..], Host = d.Host, Port = d.Port, Database = d.Database, Username = d.Username, Password = d.Password
                });
                connectionId = connection.Value;
                token = (await scope.ServiceProvider.GetRequiredService<TokenService>().CreateAsync(new TokenInput
                {
                    Name = "mask-test", Grants = [new TokenGrantInput { ConnectionId = connectionId }]
                })).Value!.PlainToken;
            }
            var alias = "mask-" + table[^6..];

            // The masking page lists the live schema and suggests a rule for the e-mail column.
            var admin = await SignInAsync();
            var page = await admin.GetStringAsync($"/connections/{connectionId}/masking");
            Assert.Contains(table, page);
            Assert.Contains("Suggested:", page);

            var saved = await admin.PostAsync($"/connections/{connectionId}/masking", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = Antiforgery(page),
                ["DetectPersonalData"] = "true",
                ["Tables[0].Schema"] = "public",
                ["Tables[0].Name"] = table,
                ["Tables[0].Mode"] = "Normal",
                ["Tables[0].Columns[0].Name"] = "email",
                ["Tables[0].Columns[0].Mode"] = "Partial",
                ["Tables[0].Columns[1].Name"] = "full_name",
                ["Tables[0].Columns[1].Mode"] = "Pseudonym"
            }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

            // Query over MCP with an alias: rules still apply, the free-text note is caught by detection.
            var http = factory.CreateClient();
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
            }, http, NullLoggerFactory.Instance, ownsHttpClient: true));

            var result = await client.CallToolAsync("execute_query", new Dictionary<string, object?>
            {
                ["alias"] = alias,
                ["sql"] = $"SELECT p.email AS contact, p.full_name, p.note FROM {table} p ORDER BY p.id"
            });
            Assert.NotEqual(true, result.IsError);
            using var json = JsonDocument.Parse(Text(result));
            var rows = json.RootElement.GetProperty("rows");
            Assert.Equal("a***@example.com", rows[0][0].GetString());
            Assert.StartsWith("full_name_", rows[0][1].GetString());
            Assert.Equal(rows[0][1].GetString(), rows[1][1].GetString());
            Assert.Equal("tel *** *** ** 67", rows[0][2].GetString());
            Assert.Equal(["contact", "full_name", "note"],
                json.RootElement.GetProperty("maskedColumns").EnumerateArray().Select(e => e.GetString()));
            Assert.DoesNotContain("ayse@example.com", Text(result));
            Assert.DoesNotContain("Kaya", Text(result));

            var schema = Text(await client.CallToolAsync("describe_schema", new Dictionary<string, object?>
            {
                ["alias"] = alias, ["tableNameContains"] = table
            }));
            Assert.Contains("email text NULL [masked: partial]", schema);
            Assert.Contains("full_name text NULL [masked: pseudonym]", schema);
        }
        finally
        {
            await adapter.ExecuteQueryAsync(pg, $"DROP TABLE {table}", AccessLevel.ReadWrite, rw, default);
        }
    }
}
