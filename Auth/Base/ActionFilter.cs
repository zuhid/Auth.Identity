using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace Zuhid.Auth.Base;

public class ActionFilter(ILogger<ActionFilter> logger) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        // Model binding runs before this filter, so a body that fails to deserialize (e.g. "" into an int?
        // property) already shows up here as an invalid ModelState with the bound parameter left null. Without
        // this check the action would run anyway and crash deep inside reflection instead of returning a clear 400.
        if (!context.ModelState.IsValid)
        {
            LogInvalidModelState(context.ModelState, context.ActionDescriptor.DisplayName);
            context.Result = new BadRequestObjectResult(context.ModelState);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // Runs after the action so it also catches ModelState errors added by validators, not just data-annotation errors.
        if (!context.ModelState.IsValid)
        {
            LogInvalidModelState(context.ModelState, context.ActionDescriptor.DisplayName);
            context.Result = new BadRequestObjectResult(context.ModelState);
        }
    }

    private void LogInvalidModelState(ModelStateDictionary modelState, string? actionName)
    {
        var errors = string.Join("; ", modelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error => $"{entry.Key}: {error.ErrorMessage}")));
        logger.LogWarning("Invalid ModelState in {ActionName}: {Errors}", actionName, errors);
    }
}
