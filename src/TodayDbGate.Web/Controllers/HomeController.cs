using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TodayDbGate.Application.Abstractions;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;
using TodayDbGate.Web.Models;

namespace TodayDbGate.Web.Controllers;

public sealed class HomeController(ConnectionService connections, TokenService tokens, IAuditLogReader audit) : Controller
{
    private const int RecentEntries = 30;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var connectionList = await connections.ListAsync(ct);
        var tokenList = await tokens.ListAsync(ct);
        var recent = await audit.SearchAsync(new AuditFilter { PageSize = RecentEntries }, ct);

        return View(new OverviewModel(
            connectionList.Count,
            tokenList.Count,
            tokenList.Count(t => t.IsActive && !t.IsExpired),
            McpUrl(Request),
            LiveEntry.ToJson(recent.Items.Select(LiveEntry.From))));
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();

    internal static string McpUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}/mcp";
}
