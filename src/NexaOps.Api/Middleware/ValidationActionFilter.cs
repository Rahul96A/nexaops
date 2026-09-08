using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NexaOps.Api.Middleware;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument before the action
/// executes.
/// <para>
/// This replaces the deprecated <c>FluentValidation.AspNetCore</c> auto-validation package. It
/// resolves a validator per argument type from the container, so adding a validator class is all
/// that is needed for an endpoint to start validating - there is no per-controller wiring to
/// forget, and no second place where validation rules could drift from the command they guard.
/// </para>
/// <para>
/// Failures are thrown as <see cref="ValidationException"/> and turned into a 400 with per-field
/// errors by <see cref="ExceptionHandlingMiddleware"/>, so validation failures look the same as
/// every other failure on the wire.
/// </para>
/// </summary>
public sealed class ValidationActionFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public ValidationActionFilter(IServiceProvider services) => _services = services;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var failures = new List<FluentValidation.Results.ValidationFailure>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (_services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);

            var result = await validator
                .ValidateAsync(validationContext, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (!result.IsValid)
            {
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        await next().ConfigureAwait(false);
    }
}
