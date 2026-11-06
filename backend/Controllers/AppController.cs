using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

// Every endpoint here is per signed-in user; services filter every read and write by this id.
[ApiController]
[Authorize]
public abstract class AppController : ControllerBase
{
    protected int UserId => AuthHelper.GetCurrentUserId(User);

    protected ActionResult Result<T>(ServiceResult<T> result) => result.Error == null
        ? Ok(result.Value)
        : result.Status switch
        {
            404 => NotFound(new { error = result.Error }),
            409 => Conflict(new { error = result.Error, current = result.Detail }),
            _ => BadRequest(new { error = result.Error })
        };

    protected ActionResult BadMonth() =>
        BadRequest(new { error = $"Month must look like 2026-11, between {InputRules.MinYear} and {InputRules.MaxYear}" });

    protected static bool IsValidYear(int year) => year is >= InputRules.MinYear and <= InputRules.MaxYear;

    protected ActionResult BadYear() => Error($"Year must be between {InputRules.MinYear} and {InputRules.MaxYear}");

    protected ActionResult Error(string message) => BadRequest(new { error = message });
}
