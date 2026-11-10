using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using TodayDbGate.Application;
using TodayDbGate.Infrastructure;
using TodayDbGate.Infrastructure.Persistence;
using TodayDbGate.Web.Admin;
using TodayDbGate.Web.Mcp;

const string AdminPolicy = "Admin";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(o => builder.Configuration.GetSection("Gateway").Bind(o));
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddSingleton<AdminCredentialStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddRouting(o => o.LowercaseUrls = true);

// Dashboard: every controller needs the admin cookie (opt out with [AllowAnonymous]) and a CSRF token on POST.
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AuthorizeFilter(AdminPolicy));
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/account/login";
        o.LogoutPath = "/account/logout";
        o.AccessDeniedPath = "/account/login";
        o.Cookie.Name = "tdg_admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    })
    // MCP clients authenticate with "Authorization: Bearer tdg_...".
    .AddScheme<AuthenticationSchemeOptions, McpTokenAuthenticationHandler>(McpTokenAuthentication.SchemeName, null);

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(AdminPolicy, p => p
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
    o.AddPolicy(McpTokenAuthentication.PolicyName, p => p
        .AddAuthenticationSchemes(McpTokenAuthentication.SchemeName)
        .RequireAuthenticatedUser());
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("mcp", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
});

// Stateless HTTP transport (the SDK default): every request carries and re-validates its token,
// so deactivating a token takes effect on the next call.
builder.Services.AddMcpServer(o =>
    {
        o.ServerInfo = new Implementation { Name = "TodayDbGate", Version = "0.1.0" };
        o.ServerInstructions =
            "TodayDbGate gives access to databases by alias. Start with list_connections, " +
            "then describe_schema for the alias you need, then execute_query with one SQL statement at a time.";
    })
    .WithHttpTransport()
    .WithTools<GatewayTools>();

var app = builder.Build();

// MVP: apply pending migrations (gateway.db, audit.db) and create the admin account on startup.
app.Services.InitializeDatabases();
app.Services.GetRequiredService<AdminCredentialStore>()
    .EnsureInitialized(app.Configuration["Admin:InitialPassword"], app.Logger);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/home/error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapMcp("/mcp")
    .RequireAuthorization(McpTokenAuthentication.PolicyName)
    .RequireRateLimiting("mcp");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Lets integration tests start the app with WebApplicationFactory<Program>.
public partial class Program;
