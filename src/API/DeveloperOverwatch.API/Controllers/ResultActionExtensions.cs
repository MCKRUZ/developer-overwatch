namespace DeveloperOverwatch.API.Controllers;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

/// <summary>Turns a failed <see cref="Result{T}"/> into the response the dashboard expects: validation errors as
/// RFC 7807 validation problem details, <see cref="ErrorCodes"/> as 404 and 409.</summary>
internal static class ResultActionExtensions
{
    public static IActionResult Failure<T>(this ControllerBase controller, Result<T> result)
    {
        if (result.ValidationErrors.Count > 0)
            return controller.ValidationProblem(ToModelState(result.ValidationErrors));

        return result.Error switch
        {
            ErrorCodes.NotFound => controller.NotFound(),
            ErrorCodes.AlreadyExists => controller.Conflict(),
            _ => throw new InvalidOperationException($"No response is mapped for the failure '{result.Error}'."),
        };
    }

    // ValidationBehavior writes each error as "Property: message".
    private static ModelStateDictionary ToModelState(IReadOnlyList<string> errors)
    {
        var state = new ModelStateDictionary();
        foreach (var error in errors)
        {
            var split = error.IndexOf(": ", StringComparison.Ordinal);
            if (split < 0)
                state.AddModelError(string.Empty, error);
            else
                state.AddModelError(error[..split], error[(split + 2)..]);
        }
        return state;
    }
}
