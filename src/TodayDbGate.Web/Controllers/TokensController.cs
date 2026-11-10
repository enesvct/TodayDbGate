using Microsoft.AspNetCore.Mvc;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;
using TodayDbGate.Domain.Enums;
using TodayDbGate.Web.Models;

namespace TodayDbGate.Web.Controllers;

public sealed class TokensController(TokenService tokens, ConnectionService connections) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await tokens.ListAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        View("Form", new TokenFormModel { Rows = await BuildRowsAsync([], ct) });

    [HttpPost]
    public async Task<IActionResult> Create(TokenFormModel form, CancellationToken ct)
    {
        var result = await tokens.CreateAsync(form.ToInput(), ct);
        if (!result.Succeeded)
        {
            form.Errors = result.Errors;
            form.Rows = await BuildRowsAsync(form.Rows, ct);
            return View("Form", form);
        }

        // Rendered directly (not redirected) so the token never sits in a URL, cookie or TempData.
        Response.Headers.CacheControl = "no-store";
        return View("Created", new CreatedTokenModel(form.Name.Trim(), result.Value!.PlainToken, HomeController.McpUrl(Request)));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var token = await tokens.GetAsync(id, ct);
        if (token is null) return NotFound();

        var current = token.Grants.Select(g => new GrantRow
        {
            ConnectionId = g.ConnectionId,
            Level = g.AccessLevel == AccessLevel.ReadWrite ? GrantLevel.ReadWrite : GrantLevel.ReadOnly
        }).ToList();

        return View("Form", new TokenFormModel
        {
            Id = id,
            Name = token.Name,
            ExpiresOn = token.ExpiresAt is { } e ? DateOnly.FromDateTime(e.AddDays(-1)) : null,
            Rows = await BuildRowsAsync(current, ct)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, TokenFormModel form, CancellationToken ct)
    {
        var result = await tokens.UpdateAsync(id, form.ToInput(), ct);
        if (!result.Succeeded)
        {
            form.Id = id;
            form.Errors = result.Errors;
            form.Rows = await BuildRowsAsync(form.Rows, ct);
            return View("Form", form);
        }

        TempData["Success"] = "Token updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SetActive(int id, bool active, CancellationToken ct)
    {
        var result = await tokens.SetActiveAsync(id, active, ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? (active ? "Token activated." : "Token deactivated. It is refused from the next request.")
                             : string.Join(" ", result.Errors);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await tokens.DeleteAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Token deleted." : string.Join(" ", result.Errors);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>One row per existing connection, keeping the levels already chosen.</summary>
    private async Task<List<GrantRow>> BuildRowsAsync(IEnumerable<GrantRow> chosen, CancellationToken ct)
    {
        var levels = chosen.GroupBy(r => r.ConnectionId).ToDictionary(g => g.Key, g => g.Last().Level);
        return (await connections.ListAsync(ct)).Select(c => new GrantRow
        {
            ConnectionId = c.Id,
            AliasName = c.AliasName,
            DatabaseType = c.DatabaseType,
            Level = levels.GetValueOrDefault(c.Id, GrantLevel.None)
        }).ToList();
    }
}
