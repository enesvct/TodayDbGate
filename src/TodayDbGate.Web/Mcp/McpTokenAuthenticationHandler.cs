using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;

namespace TodayDbGate.Web.Mcp;

public static class McpTokenAuthentication
{
    public const string SchemeName = "TodayDbGateToken";
    public const string PolicyName = "McpToken";
}

/// <summary>
/// Validates "Authorization: Bearer tdg_..." on MCP requests. On success the caller's grants are stored in
/// <see cref="HttpContext.Items"/> under <c>typeof(TokenPrincipal)</c> for the MCP tools to use.
/// </summary>
public sealed class McpTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var policy = Context.RequestServices.GetRequiredService<AccessPolicyService>();
        var principal = await policy.AuthenticateAsync(header[BearerPrefix.Length..].Trim(), Context.RequestAborted);
        if (principal is null)
        {
            Logger.LogWarning("Rejected MCP request with an invalid, inactive or expired token from {RemoteIp}.",
                Context.Connection.RemoteIpAddress);
            return AuthenticateResult.Fail("Invalid, inactive or expired token.");
        }

        Context.Items[typeof(TokenPrincipal)] = principal;

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, principal.TokenId.ToString()),
            new Claim(ClaimTypes.Name, principal.Name)
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
