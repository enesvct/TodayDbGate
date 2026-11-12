using Microsoft.AspNetCore.Mvc;
using TodayDbGate.Application.Services;
using TodayDbGate.Web.Models;

namespace TodayDbGate.Web.Controllers;

[Route("connections/{id:int}/masking")]
public sealed class MaskingController(MaskingAdminService masking) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int id, CancellationToken ct)
    {
        var editor = await masking.GetEditorAsync(id, ct);
        return editor is null ? NotFound() : View(MaskingFormModel.From(editor));
    }

    // Large schemas post a few fields per column; lift the default limit of 1024 form values.
    [HttpPost("")]
    [RequestFormLimits(ValueCountLimit = 200_000)]
    public async Task<IActionResult> Index(int id, MaskingFormModel form, CancellationToken ct)
    {
        var result = await masking.SaveAsync(id, form.ToInput(), ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? $"Masking saved: {form.Tables.Sum(t => t.RuleCount)} rule(s). It applies from the next query."
            : string.Join(" ", result.Errors);
        return RedirectToAction(nameof(Index), new { id });
    }
}
