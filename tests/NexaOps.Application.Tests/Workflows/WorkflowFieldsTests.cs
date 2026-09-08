using NexaOps.Application.Workflows;
using NexaOps.Domain.Changes;
using NexaOps.Domain.Problems;
using NexaOps.Domain.Requests;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Workflows;

namespace NexaOps.Application.Tests.Workflows;

/// <summary>
/// The published field list against what the entities actually publish.
/// <para>
/// These two lists are maintained in different files and must agree. A field offered to a rule
/// author that the entity does not publish produces a rule that looks configured, is accepted by
/// the validator, and silently never matches — the worst failure mode automation has. This test
/// is the reason nobody has to remember to keep them in step.
/// </para>
/// </summary>
public sealed class WorkflowFieldsTests
{
    public static TheoryData<ServiceModule, IWorkflowTarget> Targets() => new()
    {
        { ServiceModule.Incident, new Incident { Number = "INC1", Title = "t" } },
        { ServiceModule.Request, new ServiceRequest { Number = "REQ1", Title = "t" } },
        { ServiceModule.Problem, new Problem { Number = "PRB1", Title = "t" } },
        { ServiceModule.Change, new Change { Number = "CHG1", Title = "t" } }
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void Every_offered_field_is_one_the_entity_actually_publishes(
        ServiceModule module,
        IWorkflowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var published = target.WorkflowFacts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in WorkflowFields.For(module))
        {
            published.ShouldContain(
                field.Field,
                $"{module} offers \"{field.Field}\" to rule authors, but the entity does not publish it. "
                + "A rule written against it would be accepted and never match.");
        }
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void Every_published_fact_is_offered_to_rule_authors(
        ServiceModule module,
        IWorkflowTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // The other direction. A fact the entity publishes but the list withholds is a field
        // people can see on the record and cannot write a rule about, which reads as a bug.
        var offered = WorkflowFields.For(module).Select(f => f.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in target.WorkflowFacts.Keys)
        {
            offered.ShouldContain(key, $"{module} publishes \"{key}\" but does not offer it to rule authors.");
        }
    }

    [Fact]
    public void Every_supported_module_has_fields()
    {
        foreach (var module in WorkflowFields.SupportedModules)
        {
            WorkflowFields.For(module).ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void A_module_the_engine_is_not_driven_from_offers_nothing()
    {
        // Knowledge articles and assets do not raise workflow triggers. Offering fields for them
        // would advertise rules that could be saved and would never run.
        WorkflowFields.For(ServiceModule.Knowledge).ShouldBeEmpty();
        WorkflowFields.For(ServiceModule.Asset).ShouldBeEmpty();
        WorkflowFields.For(ServiceModule.ConfigurationItem).ShouldBeEmpty();
    }

    [Fact]
    public void The_priority_field_warns_that_it_is_inverted()
    {
        // P1 is 1, so "more urgent than P2" is "less than 2". Somebody writing their first
        // escalation rule will get this backwards unless the editor tells them.
        foreach (var module in WorkflowFields.SupportedModules)
        {
            var priority = WorkflowFields.For(module).Single(f => f.Field == "Priority");
            priority.Hint.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
