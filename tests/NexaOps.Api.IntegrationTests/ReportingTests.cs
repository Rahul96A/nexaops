using System.Net;
using System.Net.Http.Json;
using NexaOps.Api.IntegrationTests.Infrastructure;
using NexaOps.Application.Common;
using NexaOps.Application.Incidents;
using NexaOps.Application.Reporting;

namespace NexaOps.Api.IntegrationTests;

/// <summary>
/// Reporting end to end, over real HTTP against real SQL Server.
/// <para>
/// The behaviours worth protecting: the numbers come from records rather than from anywhere
/// else, a rate with no denominator is absent rather than zero, a tenant's report cannot see a
/// neighbour's work, and downloading the rows is a different permission from reading the total.
/// </para>
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ReportingTests
{
    private readonly TestEnvironment _env;

    public ReportingTests(TestEnvironment env) => _env = env;

    private static string Window(int days = 30)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return $"from={to.AddDays(-days):yyyy-MM-dd}&to={to:yyyy-MM-dd}";
    }

    [Fact]
    public async Task An_incident_raised_today_appears_in_todays_figures()
    {
        // The whole claim of the module is that the numbers come from the records. This is that
        // claim, tested: raise one, and watch the count move by one.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var before = await ServiceDeskAsync(manager);

        await RaiseAsync(manager, "Incident that should appear in the report");

        var after = await ServiceDeskAsync(manager);

        after.Created.ShouldBe(before.Created + 1);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayRow = after.Daily.Single(d => d.Date == today);
        var beforeToday = before.Daily.Single(d => d.Date == today);

        todayRow.Created.ShouldBe(beforeToday.Created + 1);
    }

    [Fact]
    public async Task The_report_says_when_its_last_day_is_not_a_whole_day()
    {
        // A period ending today is comparing a partial day against complete ones. Saying so is
        // the difference between a chart people trust and one they learn to discount.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var report = await ServiceDeskAsync(manager);
        report.IsPartialPeriod.ShouldBeTrue();

        var past = await manager.GetFromJsonAsync<ServiceDeskReportDto>(
            $"/api/v1/reports/service-desk?from=2026-01-01&to=2026-01-31", TestEnvironment.Json);

        past!.IsPartialPeriod.ShouldBeFalse();
    }

    [Fact]
    public async Task Every_day_in_the_window_is_present_even_the_quiet_ones()
    {
        // A chart that omits empty days compresses a quiet week into a busy-looking line.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var report = await manager.GetFromJsonAsync<ServiceDeskReportDto>(
            $"/api/v1/reports/service-desk?{Window(13)}", TestEnvironment.Json);

        report!.Daily.Count.ShouldBe(14);
        report.Daily.Select(d => d.Date).ShouldBeUnique();
    }

    [Fact]
    public async Task A_rate_with_nothing_to_measure_is_absent_rather_than_zero()
    {
        // Nothing was raised in 2020. "0% breached" would read as a perfect month; the honest
        // answer is that there is no rate.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var report = await manager.GetFromJsonAsync<ServiceDeskReportDto>(
            "/api/v1/reports/service-desk?from=2020-01-01&to=2020-01-31", TestEnvironment.Json);

        report!.Created.ShouldBe(0);
        report.SlaAttainment.Percent.ShouldBeNull();
        report.SlaAttainment.Denominator.ShouldBe(0);
        report.ReopenRate.Percent.ShouldBeNull();

        // And no average resolution time, which is not the same as an average of zero.
        report.TimeToResolve.Mean.ShouldBeNull();
        report.TimeToResolve.Median.ShouldBeNull();
        report.TimeToResolve.Sample.ShouldBe(0);
    }

    [Fact]
    public async Task A_period_that_starts_after_it_ends_is_refused()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.GetAsync(
            "/api/v1/reports/service-desk?from=2026-06-01&to=2026-05-01");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("report.invalid_period");
    }

    [Fact]
    public async Task A_window_longer_than_a_year_is_refused_rather_than_run()
    {
        // Not a licensing limit. The daily series is one row per day and the breakdowns scan the
        // period, so an unbounded range is a way for one request to hold the database for
        // minutes.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.GetAsync(
            "/api/v1/reports/service-desk?from=2020-01-01&to=2026-01-01");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("report.period_too_long");
    }

    [Fact]
    public async Task A_period_ending_in_the_future_is_clamped_rather_than_refused()
    {
        // "This month", run on the eighth, is a reasonable thing to ask for. The answer is the
        // eight days that exist.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var report = await manager.GetFromJsonAsync<ServiceDeskReportDto>(
            $"/api/v1/reports/service-desk?from={today.AddDays(-7):yyyy-MM-dd}&to={today.AddYears(1):yyyy-MM-dd}",
            TestEnvironment.Json);

        report!.To.ShouldBe(today);
    }

    [Fact]
    public async Task One_tenants_report_does_not_count_a_neighbours_work()
    {
        var acme = await _env.ClientForAsync(_env.Acme.Manager);
        var northwind = await _env.ClientForAsync(_env.Northwind.Manager);

        var acmeBefore = await ServiceDeskAsync(acme);

        await RaiseAsync(northwind, "A neighbour's incident that Acme must not count");

        var acmeAfter = await ServiceDeskAsync(acme);

        acmeAfter.Created.ShouldBe(acmeBefore.Created);
    }

    [Fact]
    public async Task Resolution_time_reports_both_a_mean_and_a_median()
    {
        // Both, not one. The mean is dominated by the handful nobody closed and the median hides
        // a tail that is somebody's whole month; where they disagree, the disagreement is the
        // finding.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var incident = await RaiseAsync(manager, "Incident to resolve for the timing figures");

        var response = await manager.PostAsJsonAsync(
            $"/api/v1/incidents/{incident.Id}/status",
            new
            {
                status = "Resolved",
                resolutionCode = "Resolved",
                notes = "Resolved by the reporting suite."
            },
            TestEnvironment.Json);

        response.EnsureSuccessStatusCode();

        var report = await ServiceDeskAsync(manager);

        report.TimeToResolve.Sample.ShouldBeGreaterThan(0);
        report.TimeToResolve.Mean.ShouldNotBeNull();
        report.TimeToResolve.Median.ShouldNotBeNull();
    }

    [Fact]
    public async Task Sla_attainment_counts_only_clocks_that_finished()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var report = await manager.GetFromJsonAsync<SlaReportDto>(
            $"/api/v1/reports/sla?{Window()}", TestEnvironment.Json);

        report.ShouldNotBeNull();

        // Every row's denominator is its own met plus breached — nothing else is counted.
        foreach (var row in report.Rows)
        {
            row.Attainment.Denominator.ShouldBe(row.Met + row.Breached);
        }

        report.Overall.Denominator.ShouldBe(report.Rows.Sum(r => r.Met + r.Breached));
    }

    [Fact]
    public async Task A_change_that_has_not_been_reviewed_has_no_outcome_to_count()
    {
        // The outcome is recorded at review. Counting an implemented-but-unreviewed change as
        // successful would be an assumption presented as a measurement.
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var report = await manager.GetFromJsonAsync<ChangeReportDto>(
            $"/api/v1/reports/changes?{Window()}", TestEnvironment.Json);

        report!.Reviewed.ShouldBe(report.Outcomes.Sum(o => o.Count));
        report.SuccessRate.Denominator.ShouldBe(report.Reviewed);
    }

    // -----------------------------------------------------------------
    // Export
    // -----------------------------------------------------------------

    [Fact]
    public async Task An_agent_can_read_a_report_but_not_download_it()
    {
        // A figure on a screen stays inside the application; a file leaves with whoever
        // downloaded it. The permissions are split for that reason.
        var agent = await _env.ClientForAsync(_env.Acme.Agent);

        (await agent.GetAsync($"/api/v1/reports/service-desk?{Window()}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await agent.GetAsync($"/api/v1/reports/service-desk/export?{Window()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_export_is_csv_with_a_header_and_a_dated_file_name()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.GetAsync($"/api/v1/reports/service-desk/export?{Window(6)}");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.ShouldNotBeNull();
        response.Content.Headers.ContentDisposition.FileName.ShouldContain("service-desk");

        var csv = await response.Content.ReadAsStringAsync();

        csv.ShouldContain("Date,Created,Resolved");

        // Seven days requested, seven rows plus the header. An export that silently drops quiet
        // days does not reconcile with the chart it came from.
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(8);
    }

    [Fact]
    public async Task Asking_for_a_report_that_does_not_exist_says_so()
    {
        var manager = await _env.ClientForAsync(_env.Acme.Manager);

        var response = await manager.GetAsync($"/api/v1/reports/not-a-report/export?{Window()}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(TestEnvironment.Json);
        problem!.Code.ShouldBe("report.unknown");
    }

    [Fact]
    public async Task Reporting_is_closed_to_anyone_without_the_permission()
    {
        // An ordinary employee can see their own tickets. Aggregate figures about everybody
        // else's work are a different question.
        var employee = await _env.ClientForAsync(_env.Acme.Employee);

        (await employee.GetAsync($"/api/v1/reports/service-desk?{Window()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static async Task<ServiceDeskReportDto> ServiceDeskAsync(HttpClient client)
        => (await client.GetFromJsonAsync<ServiceDeskReportDto>(
            $"/api/v1/reports/service-desk?{Window()}", TestEnvironment.Json))!;

    private static async Task<IncidentDetailDto> RaiseAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/incidents",
            new
            {
                title,
                description = "Raised by the reporting suite to exercise the figures end to end.",
                impact = "Moderate",
                urgency = "Medium"
            },
            TestEnvironment.Json);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Expected 201 but got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<IncidentDetailDto>(TestEnvironment.Json))!;
    }
}
