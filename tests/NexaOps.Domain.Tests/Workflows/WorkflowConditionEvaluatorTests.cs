using NexaOps.Domain.Workflows;

namespace NexaOps.Domain.Tests.Workflows;

/// <summary>
/// Rule matching.
/// <para>
/// This is the part of the engine most expensive to get wrong: a condition that matches when it
/// should not silently reroutes other people's work, and one that fails to match is an
/// automation nobody can find the fault in. It is pure, so it can be tested exhaustively.
/// </para>
/// </summary>
public sealed class WorkflowConditionEvaluatorTests
{
    private static readonly Dictionary<string, string?> Incident = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Status"] = "InProgress",
        ["Priority"] = "1",
        ["CategoryName"] = "Network",
        ["Title"] = "Branch router unreachable",
        ["AssignedToUserId"] = null,
        ["IsMajor"] = "true"
    };

    private static WorkflowCondition Condition(
        string field,
        WorkflowConditionOperator op,
        string? value = null) => new() { Field = field, Operator = op, Value = value };

    [Fact]
    public void No_conditions_matches_everything()
    {
        // A rule with no conditions fires on every record of its trigger. That is a legitimate
        // thing to want — "notify the desk whenever a P1 is raised" is expressed as a trigger
        // plus one condition, "notify on every change" as a trigger and none.
        WorkflowConditionEvaluator.Evaluate([], Incident).Matched.ShouldBeTrue();
    }

    [Fact]
    public void Conditions_are_combined_with_and()
    {
        var conditions = new[]
        {
            Condition("Status", WorkflowConditionOperator.Equals, "InProgress"),
            Condition("CategoryName", WorkflowConditionOperator.Equals, "Network")
        };

        WorkflowConditionEvaluator.Evaluate(conditions, Incident).Matched.ShouldBeTrue();

        var withOneFalse = new[]
        {
            conditions[0],
            Condition("CategoryName", WorkflowConditionOperator.Equals, "Payroll")
        };

        WorkflowConditionEvaluator.Evaluate(withOneFalse, Incident).Matched.ShouldBeFalse();
    }

    [Fact]
    public void A_failed_match_names_the_condition_that_stopped_it()
    {
        // "Why did my rule not fire" is the only question anybody asks about automation, and it
        // is unanswerable from a log that records only what ran.
        var result = WorkflowConditionEvaluator.Evaluate(
            [Condition("CategoryName", WorkflowConditionOperator.Equals, "Payroll")],
            Incident);

        result.Matched.ShouldBeFalse();
        result.FailedOn.ShouldNotBeNull();
        result.FailedOn.Field.ShouldBe("CategoryName");
        result.Explanation.ShouldNotBeNull();
        result.Explanation.ShouldContain("Network");
        result.Explanation.ShouldContain("Payroll");
    }

    [Fact]
    public void Evaluation_stops_at_the_first_failure()
    {
        var result = WorkflowConditionEvaluator.Evaluate(
            [
                Condition("Status", WorkflowConditionOperator.Equals, "Resolved"),
                Condition("CategoryName", WorkflowConditionOperator.Equals, "Payroll")
            ],
            Incident);

        // The explanation should name the first failure, not the last. An administrator fixing
        // the wrong condition is worse than no explanation.
        result.FailedOn!.Field.ShouldBe("Status");
    }

    [Theory]
    [InlineData("InProgress", true)]
    [InlineData("inprogress", true)]
    [InlineData("INPROGRESS", true)]
    [InlineData("Resolved", false)]
    public void Equality_ignores_case(string value, bool expected)
    {
        // Values arrive from a UI, an API caller and a seeder. Case-sensitivity here would
        // produce rules that work for whoever created them and not for anybody else.
        WorkflowConditionEvaluator
            .Evaluate([Condition("Status", WorkflowConditionOperator.Equals, value)], Incident)
            .Matched.ShouldBe(expected);
    }

    [Fact]
    public void Field_names_are_matched_case_insensitively_when_the_fact_bag_says_so()
    {
        WorkflowConditionEvaluator
            .Evaluate([Condition("status", WorkflowConditionOperator.Equals, "InProgress")], Incident)
            .Matched.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Network,Payroll,Email", true)]
    [InlineData("Network", true)]
    [InlineData(" Network , Payroll ", true)]
    [InlineData("Payroll,Email", false)]
    [InlineData("", false)]
    public void In_matches_any_of_a_comma_separated_list(string list, bool expected)
    {
        WorkflowConditionEvaluator
            .Evaluate([Condition("CategoryName", WorkflowConditionOperator.In, list)], Incident)
            .Matched.ShouldBe(expected);
    }

    [Fact]
    public void Contains_is_a_substring_test()
    {
        WorkflowConditionEvaluator
            .Evaluate([Condition("Title", WorkflowConditionOperator.Contains, "router")], Incident)
            .Matched.ShouldBeTrue();

        WorkflowConditionEvaluator
            .Evaluate([Condition("Title", WorkflowConditionOperator.Contains, "printer")], Incident)
            .Matched.ShouldBeFalse();
    }

    [Fact]
    public void An_absent_field_is_empty_rather_than_an_error()
    {
        // A rule written against a field a module has stopped publishing should quietly stop
        // matching. Throwing would break the record the rule is attached to, which is a far
        // worse failure than a rule that does nothing.
        var result = WorkflowConditionEvaluator.Evaluate(
            [Condition("FieldThatDoesNotExist", WorkflowConditionOperator.Equals, "anything")],
            Incident);

        result.Matched.ShouldBeFalse();
    }

    [Fact]
    public void An_absent_field_satisfies_is_empty()
    {
        WorkflowConditionEvaluator
            .Evaluate([Condition("AssignedToUserId", WorkflowConditionOperator.IsEmpty)], Incident)
            .Matched.ShouldBeTrue();

        WorkflowConditionEvaluator
            .Evaluate([Condition("Status", WorkflowConditionOperator.IsEmpty)], Incident)
            .Matched.ShouldBeFalse();
    }

    [Fact]
    public void An_unassigned_record_does_not_satisfy_not_equals()
    {
        // "Assignee is not Priya" is a statement about a record that has an assignee. An
        // unassigned record satisfying it would route every new record through rules meant for
        // reassignment, which is exactly the surprise that gets automation switched off.
        WorkflowConditionEvaluator
            .Evaluate(
                [Condition("AssignedToUserId", WorkflowConditionOperator.NotEquals, Guid.NewGuid().ToString())],
                Incident)
            .Matched.ShouldBeFalse();
    }

    [Theory]
    [InlineData(WorkflowConditionOperator.LessThan, "2", true)]
    [InlineData(WorkflowConditionOperator.LessThan, "1", false)]
    [InlineData(WorkflowConditionOperator.GreaterThan, "0", true)]
    [InlineData(WorkflowConditionOperator.GreaterThan, "1", false)]
    public void Numeric_operators_compare_numerically(
        WorkflowConditionOperator op,
        string value,
        bool expected)
    {
        // Priority is numerically inverted — P1 is 1 — so "more urgent than P2" is LessThan 2.
        WorkflowConditionEvaluator
            .Evaluate([Condition("Priority", op, value)], Incident)
            .Matched.ShouldBe(expected);
    }

    [Fact]
    public void Numeric_comparison_does_not_fall_back_to_string_ordering()
    {
        // "10" is not less than "9". Falling back to text ordering produces a rule that is
        // correct for single digits and wrong above them: a defect that survives every demo.
        var facts = new Dictionary<string, string?> { ["Count"] = "10" };

        WorkflowConditionEvaluator
            .Evaluate([Condition("Count", WorkflowConditionOperator.GreaterThan, "9")], facts)
            .Matched.ShouldBeTrue();

        WorkflowConditionEvaluator
            .Evaluate([Condition("Count", WorkflowConditionOperator.LessThan, "9")], facts)
            .Matched.ShouldBeFalse();
    }

    [Theory]
    [InlineData(WorkflowConditionOperator.GreaterThan)]
    [InlineData(WorkflowConditionOperator.LessThan)]
    public void A_non_numeric_value_fails_a_numeric_comparison_rather_than_matching(
        WorkflowConditionOperator op)
    {
        WorkflowConditionEvaluator
            .Evaluate([Condition("CategoryName", op, "5")], Incident)
            .Matched.ShouldBeFalse();
    }

    [Fact]
    public void A_boolean_fact_is_matched_as_text()
    {
        WorkflowConditionEvaluator
            .Evaluate([Condition("IsMajor", WorkflowConditionOperator.Equals, "true")], Incident)
            .Matched.ShouldBeTrue();
    }
}
