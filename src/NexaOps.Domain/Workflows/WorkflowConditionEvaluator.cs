using System.Globalization;

namespace NexaOps.Domain.Workflows;

/// <summary>Whether a rule matched, and if not, which condition stopped it.</summary>
/// <param name="Matched">True when every condition passed.</param>
/// <param name="FailedOn">The first condition that failed, for the run history.</param>
/// <param name="Explanation">A sentence an administrator can act on.</param>
public readonly record struct WorkflowMatch(bool Matched, WorkflowCondition? FailedOn, string? Explanation)
{
    public static WorkflowMatch Match() => new(true, null, null);
}

/// <summary>
/// Decides whether a record satisfies a rule's conditions.
/// <para>
/// Pure: no database, no clock, no services. That is the point — this is the part of the engine
/// most likely to be wrong and most expensive to be wrong about, and it can be exhaustively
/// tested in milliseconds. It is also the part that has to behave identically whether it is
/// evaluating an incident or a change.
/// </para>
/// </summary>
public static class WorkflowConditionEvaluator
{
    /// <summary>
    /// Evaluates every condition against a record's facts. Conditions are combined with AND, and
    /// evaluation stops at the first failure so the explanation names the reason rather than the
    /// last of several.
    /// </summary>
    public static WorkflowMatch Evaluate(
        IEnumerable<WorkflowCondition> conditions,
        IReadOnlyDictionary<string, string?> facts)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(facts);

        foreach (var condition in conditions)
        {
            // A key the module does not publish reads as absent rather than throwing. A rule
            // written against a field that has since stopped being published should quietly stop
            // matching; breaking the record it was attached to would be a much worse failure.
            facts.TryGetValue(condition.Field, out var actual);

            if (!Satisfies(condition, actual))
            {
                return new WorkflowMatch(
                    false,
                    condition,
                    Explain(condition, actual));
            }
        }

        return WorkflowMatch.Match();
    }

    private static bool Satisfies(WorkflowCondition condition, string? actual) => condition.Operator switch
    {
        WorkflowConditionOperator.IsEmpty => string.IsNullOrWhiteSpace(actual),
        WorkflowConditionOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(actual),

        // Every remaining operator compares against a value. A record with no value for the
        // field cannot equal, exceed or contain anything, so it fails rather than matching by
        // accident — including NotEquals, where "the field is absent" is not the same statement
        // as "the field holds something else".
        _ when string.IsNullOrWhiteSpace(actual) => false,

        WorkflowConditionOperator.Equals =>
            string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),

        WorkflowConditionOperator.NotEquals =>
            !string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),

        WorkflowConditionOperator.In => SplitList(condition.Value)
            .Any(candidate => string.Equals(candidate, actual, StringComparison.OrdinalIgnoreCase)),

        WorkflowConditionOperator.Contains =>
            !string.IsNullOrEmpty(condition.Value)
            && actual.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),

        WorkflowConditionOperator.GreaterThan => CompareNumbers(actual, condition.Value) > 0,
        WorkflowConditionOperator.LessThan => CompareNumbers(actual, condition.Value) < 0,

        _ => false
    };

    private static IEnumerable<string> SplitList(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Numeric comparison, or no comparison at all.
    /// <para>
    /// Falling back to string ordering would make "10" less than "9" and produce a rule that is
    /// wrong only for two-digit values — the kind of defect that survives a demo and fails in
    /// production. A non-numeric operand fails the condition instead.
    /// </para>
    /// </summary>
    private static int CompareNumbers(string actual, string? expected)
    {
        if (!decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var left)
            || !decimal.TryParse(expected, NumberStyles.Number, CultureInfo.InvariantCulture, out var right))
        {
            // Sentinel that fails both GreaterThan and LessThan.
            return 0;
        }

        return left.CompareTo(right);
    }

    private static string Explain(WorkflowCondition condition, string? actual)
    {
        var seen = string.IsNullOrWhiteSpace(actual) ? "nothing" : $"\"{actual}\"";

        return condition.Operator switch
        {
            WorkflowConditionOperator.IsEmpty => $"{condition.Field} held {seen}, and the rule needs it empty.",
            WorkflowConditionOperator.IsNotEmpty => $"{condition.Field} was empty, and the rule needs a value.",
            _ => $"{condition.Field} held {seen}, and the rule needs it "
                 + $"{Describe(condition.Operator)} \"{condition.Value}\"."
        };
    }

    private static string Describe(WorkflowConditionOperator op) => op switch
    {
        WorkflowConditionOperator.Equals => "to equal",
        WorkflowConditionOperator.NotEquals => "to differ from",
        WorkflowConditionOperator.In => "to be one of",
        WorkflowConditionOperator.GreaterThan => "greater than",
        WorkflowConditionOperator.LessThan => "less than",
        WorkflowConditionOperator.Contains => "to contain",
        _ => "to match"
    };
}
