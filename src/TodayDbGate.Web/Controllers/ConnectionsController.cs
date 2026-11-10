using Microsoft.AspNetCore.Mvc;
using TodayDbGate.Application.Models;
using TodayDbGate.Application.Services;
using TodayDbGate.Web.Models;

namespace TodayDbGate.Web.Controllers;

public sealed class ConnectionsController(ConnectionService connections) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await connections.ListAsync(ct));

    [HttpGet]
    public IActionResult Create() => View("Form", new ConnectionFormModel());

    [HttpPost]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] ConnectionInput input, CancellationToken ct)
    {
        var result = await connections.CreateAsync(input, ct);
        if (!result.Succeeded)
            return View("Form", new ConnectionFormModel { Input = WithoutPassword(input), Errors = result.Errors });

        TempData["Success"] = $"Connection '{input.AliasName.Trim().ToLowerInvariant()}' created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var input = await connections.GetForEditAsync(id, ct);
        return input is null ? NotFound() : View("Form", new ConnectionFormModel { Id = id, Input = input });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, [Bind(Prefix = "Input")] ConnectionInput input, CancellationToken ct)
    {
        var result = await connections.UpdateAsync(id, input, ct);
        if (!result.Succeeded)
            return View("Form", new ConnectionFormModel { Id = id, Input = WithoutPassword(input), Errors = result.Errors });

        TempData["Success"] = "Connection updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var result = await connections.DeleteAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? "Connection deleted. Tokens lost their access to it." : string.Join(" ", result.Errors);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Tests a saved connection (AJAX from the list).</summary>
    [HttpPost]
    public async Task<IActionResult> Test(int id, CancellationToken ct) => Json(await connections.TestAsync(id, ct));

    /// <summary>Tests the values currently in the form (AJAX). On edit, an empty password means "use the saved one".</summary>
    [HttpPost]
    public async Task<IActionResult> TestForm(int? id, [Bind(Prefix = "Input")] ConnectionInput input, CancellationToken ct) =>
        Json(await connections.TestAsync(input, id, ct));

    // Never echo a password back into the page.
    private static ConnectionInput WithoutPassword(ConnectionInput input)
    {
        input.Password = null;
        return input;
    }
}
