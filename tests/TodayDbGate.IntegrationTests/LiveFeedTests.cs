using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;

namespace TodayDbGate.IntegrationTests;

/// <summary>The overview page's live feed: server-sent events and the details panel.</summary>
public sealed class LiveFeedTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private async Task<HttpClient> SignInAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var html = await client.GetStringAsync("/account/login");
        const string marker = "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "admin",
            ["Password"] = GatewayFactory.AdminPassword,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(html[start..html.IndexOf('"', start)])
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<(string Token, string Alias)> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var connection = await scope.ServiceProvider.GetRequiredService<ConnectionService>().CreateAsync(new ConnectionInput
        {
            AliasName = "live-" + suffix, Host = "127.0.0.1", Port = 1, Database = "d", Username = "u", Password = "p"
        });
        var token = await scope.ServiceProvider.GetRequiredService<TokenService>().CreateAsync(new TokenInput
        {
            Name = "live-token-" + suffix, Grants = [new TokenGrantInput { ConnectionId = connection.Value }]
        });
        return (token.Value!.PlainToken, "live-" + suffix);
    }

    private async Task CallToolAsync(string token, string sql, string alias)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0", id = 1, method = "tools/call",
                @params = new { name = "execute_query", arguments = new { alias, sql } }
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Authorization = new("Bearer", token);
        (await factory.CreateClient().SendAsync(request)).EnsureSuccessStatusCode();
    }

    /// <summary>Reads SSE "entry" events until one matches, or fails after a timeout.</summary>
    private static async Task<JsonElement> ReadEntryAsync(StreamReader reader, Func<JsonElement, bool> match)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var line = await reader.ReadLineAsync(timeout.Token) ?? throw new EndOfStreamException();
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var json = JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone();
            if (match(json)) return json;
        }
    }

    [Fact]
    public async Task Stream_requires_sign_in()
    {
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await anonymous.GetAsync("/audit/stream");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task New_calls_are_pushed_live_and_details_panel_shows_input()
    {
        var (token, alias) = await SeedAsync();
        var admin = await SignInAsync();

        // Start listening first (no backlog: begin after the newest existing entry).
        var after = (await factory.Services.GetRequiredService<TodayDbGate.Application.Abstractions.IAuditLogReader>()
            .SearchAsync(new AuditFilter { PageSize = 10 }, default)).Items.Select(i => i.Id).DefaultIfEmpty(0).Max();
        using var response = await admin.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/audit/stream?after={after}"), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        await CallToolAsync(token, "UPDATE accounts SET balance = 0", alias);

        var entry = await ReadEntryAsync(reader, e => e.GetProperty("alias").GetString() == alias);
        Assert.Equal("Rejected", entry.GetProperty("status").GetString());
        Assert.Equal("UPDATE accounts SET balance = 0", entry.GetProperty("input").GetString());
        Assert.EndsWith("Z", entry.GetProperty("timestamp").GetString()); // UTC, so browsers show the right local time

        var panel = await admin.GetAsync($"/audit/panel/{entry.GetProperty("id").GetInt64()}");
        var html = await panel.Content.ReadAsStringAsync();
        Assert.Contains("UPDATE accounts SET balance = 0", html);
        Assert.DoesNotContain("<html", html); // a fragment, not a full page
        Assert.Contains("no-store", panel.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Reconnect_replays_entries_missed_while_disconnected()
    {
        var (token, alias) = await SeedAsync();
        await CallToolAsync(token, "SELECT 'missed while offline'", alias);

        var admin = await SignInAsync();
        using var response = await admin.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/audit/stream?after=0"), HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var entry = await ReadEntryAsync(reader, e => e.GetProperty("alias").GetString() == alias);
        Assert.Equal("SELECT 'missed while offline'", entry.GetProperty("input").GetString());
    }

    [Fact]
    public async Task Overview_page_contains_the_live_feed_with_recent_entries()
    {
        var (token, alias) = await SeedAsync();
        await CallToolAsync(token, "SELECT 'on the overview'", alias);
        var admin = await SignInAsync();

        // The writer is asynchronous; give it a moment.
        for (var i = 0; i < 50; i++)
        {
            var html = await admin.GetStringAsync("/");
            if (html.Contains("on the overview"))
            {
                Assert.Contains("id=\"live-feed\"", html);
                Assert.Contains("id=\"live-initial\"", html);
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("Recent entry did not appear on the overview page.");
    }
}
