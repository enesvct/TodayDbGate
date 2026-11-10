using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;

namespace TodayDbGate.IntegrationTests;

/// <summary>The admin dashboard through real HTTP: auth, CSRF, forms, and the token it hands out.</summary>
public sealed partial class DashboardTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="([^"]+)"|value="([^"]+)" name="__RequestVerificationToken" """)]
    private static partial Regex AntiforgeryField();

    [GeneratedRegex("""tdg_[A-Za-z0-9_-]{43}""")]
    private static partial Regex TokenPattern();

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<string> AntiforgeryAsync(HttpClient client, string pageUrl)
    {
        var html = await client.GetStringAsync(pageUrl);
        var match = AntiforgeryField().Match(html);
        Assert.True(match.Success, $"No antiforgery field on {pageUrl}");
        return WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string formPageUrl, string postUrl, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await AntiforgeryAsync(client, formPageUrl);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = NewClient();
        var response = await PostFormAsync(client, "/account/login", "/account/login", new()
        {
            ["Username"] = "admin",
            ["Password"] = GatewayFactory.AdminPassword
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/connections")]
    [InlineData("/tokens/create")]
    public async Task Pages_require_sign_in(string url)
    {
        var response = await NewClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location!.PathAndQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_password_is_refused_and_right_one_signs_in()
    {
        var client = NewClient();
        var wrong = await PostFormAsync(client, "/account/login", "/account/login", new()
        {
            ["Username"] = "admin", ["Password"] = "not-the-password"
        });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Invalid username or password.", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/connections")).StatusCode);

        var signedIn = await SignedInClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/connections")).StatusCode);
    }

    [Fact]
    public async Task Posts_without_antiforgery_token_are_rejected()
    {
        var client = await SignedInClientAsync();
        var response = await client.PostAsync("/tokens/delete/1", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pages_send_security_headers()
    {
        var response = await NewClient().GetAsync("/account/login");
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Create_connection_and_token_in_the_ui_then_use_the_token_on_mcp()
    {
        var client = await SignedInClientAsync();
        var alias = "ui-db-" + Guid.NewGuid().ToString("N")[..6];

        var created = await PostFormAsync(client, "/connections/create", "/connections/create", new()
        {
            ["Input.AliasName"] = alias,
            ["Input.DatabaseType"] = "PostgreSql",
            ["Input.Host"] = "127.0.0.1",
            ["Input.Port"] = "1",
            ["Input.Database"] = "app",
            ["Input.Username"] = "reader",
            ["Input.Password"] = "ui-secret-pw"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Contains(alias, await client.GetStringAsync("/connections"));

        int connectionId;
        using (var scope = factory.Services.CreateScope())
            connectionId = (await scope.ServiceProvider.GetRequiredService<ConnectionService>().ListAsync())
                .Single(c => c.AliasName == alias).Id;

        var tokenPage = await PostFormAsync(client, "/tokens/create", "/tokens/create", new()
        {
            ["Name"] = "ui-token",
            ["Rows[0].ConnectionId"] = connectionId.ToString(),
            ["Rows[0].Level"] = "ReadOnly"
        });
        Assert.Equal(HttpStatusCode.OK, tokenPage.StatusCode);
        Assert.Contains("no-store", tokenPage.Headers.CacheControl!.ToString());

        var plainToken = TokenPattern().Match(await tokenPage.Content.ReadAsStringAsync()).Value;
        Assert.NotEmpty(plainToken);

        // The token list shows the grant but never the full token.
        var list = await client.GetStringAsync("/tokens");
        Assert.Contains(alias, list);
        Assert.DoesNotContain(plainToken, list);

        // The new token works on the MCP endpoint and sees exactly the granted alias.
        var mcp = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_connections","arguments":{}}}""",
                Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Authorization = new("Bearer", plainToken);
        var mcpResponse = await mcp.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, mcpResponse.StatusCode);
        Assert.Contains(alias, await mcpResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Edit_form_never_contains_the_stored_password_and_test_reports_failure()
    {
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ConnectionService>().CreateAsync(new ConnectionInput
            {
                AliasName = "edit-" + Guid.NewGuid().ToString("N")[..6], Host = "127.0.0.1", Port = 1,
                Database = "app", Username = "reader", Password = "very-secret-stored-pw"
            });
            id = result.Value;
        }

        var client = await SignedInClientAsync();
        var html = await client.GetStringAsync($"/connections/edit/{id}");
        Assert.Contains("127.0.0.1", html);
        Assert.DoesNotContain("very-secret-stored-pw", html);

        var test = await PostFormAsync(client, $"/connections/edit/{id}", $"/connections/test/{id}", []);
        using var json = JsonDocument.Parse(await test.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
    }
}
