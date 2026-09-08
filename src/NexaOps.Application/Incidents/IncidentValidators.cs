using FluentValidation;
using NexaOps.Domain.ServiceDesk;

namespace NexaOps.Application.Incidents;

/// <summary>
/// Input validation for raising an incident. This is shape and length checking only - whether a
/// referenced category or user actually exists is a business rule and belongs in
/// <see cref="IncidentService"/>, where it can be answered against the tenant's data.
/// </summary>
public sealed class CreateIncidentCommandValidator : AbstractValidator<CreateIncidentCommand>
{
    public CreateIncidentCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("A short summary is required.")
            .MaximumLength(300);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("A description is required.")
            .MaximumLength(20000);

        RuleFor(x => x.Impact).IsInEnum();
        RuleFor(x => x.Urgency).IsInEnum();
        RuleFor(x => x.Channel).IsInEnum();

        RuleFor(x => x.Tags)
            .Must(tags => tags is null || tags.Count <= 20)
            .WithMessage("An incident may carry at most 20 tags.");

        RuleForEach(x => x.Tags)
            .MaximumLength(64).WithMessage("A tag may be at most 64 characters.")
            .When(x => x.Tags is not null);

        // Assigning to a person without naming a group leaves the queue view unable to place the
        // ticket. Requiring both keeps team workload reporting coherent.
        RuleFor(x => x.AssignmentGroupId)
            .NotNull()
            .When(x => x.AssignedToUserId is not null)
            .WithMessage("An assignment group is required when assigning to a specific agent.");
    }
}

public sealed class UpdateIncidentCommandValidator : AbstractValidator<UpdateIncidentCommand>
{
    public UpdateIncidentCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().MaximumLength(300)
            .When(x => x.Title is not null);

        RuleFor(x => x.Description)
            .NotEmpty().MaximumLength(20000)
            .When(x => x.Description is not null);

        RuleFor(x => x.Impact).IsInEnum().When(x => x.Impact is not null);
        RuleFor(x => x.Urgency).IsInEnum().When(x => x.Urgency is not null);

        RuleFor(x => x.Tags)
            .Must(tags => tags is null || tags.Count <= 20)
            .WithMessage("An incident may carry at most 20 tags.");
    }
}

public sealed class AssignIncidentCommandValidator : AbstractValidator<AssignIncidentCommand>
{
    public AssignIncidentCommandValidator()
    {
        RuleFor(x => x.Note).MaximumLength(4000);

        RuleFor(x => x.AssignmentGroupId)
            .NotNull()
            .When(x => x.AssignedToUserId is not null)
            .WithMessage("An assignment group is required when assigning to a specific agent.");
    }
}

public sealed class ChangeIncidentStatusCommandValidator : AbstractValidator<ChangeIncidentStatusCommand>
{
    public ChangeIncidentStatusCommandValidator()
    {
        RuleFor(x => x.Status).IsInEnum();

        RuleFor(x => x.PendingReason)
            .NotNull().IsInEnum()
            .When(x => x.Status == IncidentStatus.Pending)
            .WithMessage("A pending reason is required so that the paused SLA is explainable.");

        RuleFor(x => x.ResolutionCode)
            .NotNull().IsInEnum()
            .When(x => x.Status == IncidentStatus.Resolved)
            .WithMessage("A resolution code is required to resolve an incident.");

        RuleFor(x => x.Notes)
            .NotEmpty().MinimumLength(10)
            .When(x => x.Status == IncidentStatus.Resolved)
            .WithMessage("Resolution notes of at least 10 characters are required.");

        RuleFor(x => x.Notes)
            .NotEmpty()
            .When(x => x.Status == IncidentStatus.Cancelled)
            .WithMessage("A reason is required when cancelling an incident.");

        RuleFor(x => x.Notes).MaximumLength(20000);
    }
}

public sealed class ChangeIncidentPriorityCommandValidator : AbstractValidator<ChangeIncidentPriorityCommand>
{
    public ChangeIncidentPriorityCommandValidator()
    {
        RuleFor(x => x.Impact).IsInEnum().When(x => x.Impact is not null);
        RuleFor(x => x.Urgency).IsInEnum().When(x => x.Urgency is not null);
        RuleFor(x => x.OverridePriority).IsInEnum().When(x => x.OverridePriority is not null);

        RuleFor(x => x.OverrideReason)
            .NotEmpty().MinimumLength(10).MaximumLength(1000)
            .When(x => x.OverridePriority is not null)
            .WithMessage("A reason of at least 10 characters is required when overriding priority.");

        RuleFor(x => x)
            .Must(x => x.Impact is not null || x.Urgency is not null || x.OverridePriority is not null)
            .WithMessage("Supply an impact, an urgency, or an explicit priority override.");
    }
}

public sealed class AddIncidentCommentCommandValidator : AbstractValidator<AddIncidentCommentCommand>
{
    public AddIncidentCommentCommandValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("A comment cannot be empty.")
            .MaximumLength(20000);

        RuleFor(x => x.Kind).IsInEnum();
    }
}

public sealed class DeclareMajorIncidentCommandValidator : AbstractValidator<DeclareMajorIncidentCommand>
{
    public DeclareMajorIncidentCommandValidator()
        => RuleFor(x => x.Reason)
            .NotEmpty().MinimumLength(10).MaximumLength(2000)
            .WithMessage("A justification of at least 10 characters is required.");
}

public sealed class IncidentQueryValidator : AbstractValidator<IncidentQuery>
{
    public IncidentQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Tag).MaximumLength(64);

        RuleFor(x => x.SortBy)
            .Must(field => field is null || IncidentQuery.SortableFields.Contains(field))
            .WithMessage("Unsupported sort field.");

        RuleFor(x => x.CreatedTo)
            .GreaterThanOrEqualTo(x => x.CreatedFrom!.Value)
            .When(x => x.CreatedFrom is not null && x.CreatedTo is not null)
            .WithMessage("The end of the date range must not be before the start.");
    }
}
