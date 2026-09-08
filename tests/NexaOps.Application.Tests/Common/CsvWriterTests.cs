using NexaOps.Application.Common;

namespace NexaOps.Application.Tests.Common;

/// <summary>
/// CSV rendering.
/// <para>
/// The two things worth protecting: a file that opens correctly wherever it is opened, and a
/// file that cannot execute when it is opened. Both fail silently if they fail at all.
/// </para>
/// </summary>
public sealed class CsvWriterTests
{
    [Fact]
    public void A_plain_row_needs_no_quoting()
    {
        CsvWriter.Write(["Number", "Title"], [["INC0001", "Printer jam"]])
            .ShouldBe("Number,Title\r\nINC0001,Printer jam\r\n");
    }

    [Fact]
    public void A_value_containing_the_separator_is_quoted()
    {
        CsvWriter.Write(["Title"], [["Payroll, HR and Finance affected"]])
            .ShouldBe("Title\r\n\"Payroll, HR and Finance affected\"\r\n");
    }

    [Fact]
    public void Quotes_are_doubled_inside_a_quoted_value()
    {
        CsvWriter.Write(["Title"], [["The \"urgent\" one, apparently"]])
            .ShouldBe("Title\r\n\"The \"\"urgent\"\" one, apparently\"\r\n");
    }

    [Fact]
    public void A_value_spanning_lines_is_quoted_rather_than_flattened()
    {
        // Ticket descriptions span lines routinely. Stripping the break would change the data;
        // leaving it unquoted would corrupt every row after it.
        var csv = CsvWriter.Write(["Notes"], [["First line\nSecond line"]]);

        csv.ShouldBe("Notes\r\n\"First line\nSecond line\"\r\n");
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+44 20 7946 0000")]
    [InlineData("-5 minutes late")]
    [InlineData("@channel please look")]
    public void A_value_a_spreadsheet_would_treat_as_a_formula_is_neutralised(string value)
    {
        // A ticket title is written by a user and read by somebody who trusts the export. Excel
        // executing it is a real path from "anyone can raise a ticket" to code on a manager's
        // laptop. The apostrophe is stripped on display and forces the cell to text.
        var csv = CsvWriter.Write(["Title"], [[value]]);

        csv.ShouldContain($"'{value}");
    }

    [Fact]
    public void A_neutralised_value_is_still_quoted_when_it_needs_to_be()
    {
        CsvWriter.Write(["Title"], [["=SUM(A1,A2)"]])
            .ShouldBe("Title\r\n\"'=SUM(A1,A2)\"\r\n");
    }

    [Fact]
    public void Dates_and_numbers_render_the_same_wherever_the_export_runs()
    {
        // Invariant culture throughout. A decimal rendered "1,5" in one locale produces a file
        // that only opens correctly on the machine that made it.
        var csv = CsvWriter.Write(
            ["Date", "Cost", "Minutes"],
            [[new DateOnly(2026, 9, 8), 1234.5m, TimeSpan.FromHours(2)]]);

        csv.ShouldBe("Date,Cost,Minutes\r\n2026-09-08,1234.5,120\r\n");
    }

    [Fact]
    public void A_null_renders_as_an_empty_cell_rather_than_the_word_null()
    {
        CsvWriter.Write(["A", "B"], [[null, "x"]]).ShouldBe("A,B\r\n,x\r\n");
    }

    [Fact]
    public void A_timestamp_renders_as_an_unambiguous_instant()
    {
        var when = new DateTimeOffset(2026, 9, 8, 14, 30, 0, TimeSpan.FromHours(5.5));

        CsvWriter.Write(["When"], [[when]]).ShouldContain("2026-09-08T14:30:00.0000000+05:30");
    }

    [Fact]
    public void An_empty_result_still_produces_its_header()
    {
        // A report with no rows is a finding. A zero-byte file is a bug report.
        CsvWriter.Write(["Number", "Title"], []).ShouldBe("Number,Title\r\n");
    }
}
