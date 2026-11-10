using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TodayDbGate.Web.Admin;
using TodayDbGate.Web.Models;

namespace TodayDbGate.Web.Controllers;

public sealed class AccountController(AdminCredentialStore admin, ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginModel model)
    {
        if (!admin.Verify(model.Username, model.Password))
        {
            logger.LogWarning("Failed dashboard sign-in for '{User}' from {RemoteIp}.",
                model.Username, HttpContext.Connection.RemoteIpAddress);
            ModelState.AddModelError("", "Invalid username or password.");
            model.Password = null;
            return View(model);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, AdminCredentialStore.Username)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordModel());

    [HttpPost]
    public IActionResult ChangePassword(ChangePasswordModel model)
    {
        if (model.NewPassword != model.ConfirmPassword)
        {
            ModelState.AddModelError("", "The new passwords do not match.");
            return View(new ChangePasswordModel());
        }

        var result = admin.ChangePassword(model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("", error);
            return View(new ChangePasswordModel());
        }

        TempData["Success"] = "Password changed.";
        return RedirectToAction("Index", "Home");
    }
}
