using System.Globalization;
using System.Text;

namespace NexaOps.Application.Common;

/// <summary>
/// Renders rows as CSV for export.
/// <para>
/// Small on purpose. The only two things a CSV writer has to get right are quoting and the
/// spreadsheet formula problem, and both are the kind of thing that is silently wrong until
/// somebody's export opens as a broken sheet — or worse, executes.
/// </para>
/// </summary>
public static class CsvWriter
{
    /// <summary>
    /// Characters that make a spreadsheet treat a cell as a formula rather than as text.
    /// <para>
    /// A ticket titled <c>=cmd|' /c calc'!A1</c> is a valid ticket title and an attack when the
    /// export is opened in Excel. Since the data is written by users of the system and read by
    /// people who trust the system, the export prefixes such cells with an apostrophe, which
    /// spreadsheets strip on display and treat as text.
    /// </para>
    /// </summary>
    private static readonly char[] FormulaLeaders = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>Writes a header row and body rows, with CRLF line endings as RFC 4180 asks.</summary>
    public static string Write(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();

        builder.Append(string.Join(',', headers.Select(Escape))).Append("\r\n");

        foreach (var row in rows)
        {
            builder.Append(string.Join(',', row.Select(Format).Select(Escape))).Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders one value.
    /// <para>
    /// Invariant culture throughout: an export is a data interchange format, not a display. A
    /// decimal rendered as "1,5" in one locale and "1.5" in another produces files that only
    /// open correctly on the machine that made them.
    /// </para>
    /// </summary>
    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",

        // ISO 8601 so a spreadsheet and a script read the same instant.
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),

        // Durations as whole minutes: a report reader wants a number they can total, not
        // "1.02:03:04".
        TimeSpan t => ((long)t.TotalMinutes).ToString(CultureInfo.InvariantCulture),

        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string value)
    {
        var text = value ?? string.Empty;

        if (text.Length > 0 && Array.IndexOf(FormulaLeaders, text[0]) >= 0)
        {
            text = "'" + text;
        }

        // Quoting is required for the separator, quotes themselves, and anything containing a
        // line break — a ticket description spanning lines is normal, not exceptional.
        if (text.Contains(',', StringComparison.Ordinal)
            || text.Contains('"', StringComparison.Ordinal)
            || text.Contains('\n', StringComparison.Ordinal)
            || text.Contains('\r', StringComparison.Ordinal))
        {
            return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return text;
    }
}
