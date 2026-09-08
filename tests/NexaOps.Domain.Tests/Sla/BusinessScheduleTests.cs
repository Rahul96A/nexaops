using NexaOps.Domain.Sla;

namespace NexaOps.Domain.Tests.Sla;

/// <summary>
/// The SLA clock arithmetic.
/// <para>
/// These are the numbers a customer will argue about in a contract review, so they are pinned
/// here to the minute against a realistic Indian business calendar: Monday to Friday,
/// 09:00-18:00 IST, with national holidays excluded.
/// </para>
/// </summary>
public sealed class BusinessScheduleTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    /// <summary>Monday 7 September 2026. Every case below is anchored to this week.</summary>
    private static readonly DateOnly Monday = new(2026, 9, 7);

    private static BusinessSchedule StandardWeek(params (int Month, int Day, string Name)[] holidays)
    {
        var calendarId = Guid.NewGuid();

        var windows = new[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday
            }
            .Select(day => new BusinessCalendarWindow
            {
                BusinessCalendarId = calendarId,
                DayOfWeek = day,
                StartMinute = 9 * 60,
                EndMinute = 18 * 60
            })
            .ToList();

        var holidayRows = holidays
            .Select(h => new BusinessCalendarHoliday
            {
                BusinessCalendarId = calendarId,
                Date = new DateOnly(2026, h.Month, h.Day),
                Name = h.Name
            })
            .ToList();

        return BusinessSchedule.FromCalendar("India Standard Time", false, windows, holidayRows);
    }

    /// <summary>Builds a UTC instant from a local IST wall-clock time.</summary>
    private static DateTimeOffset Ist_(DateOnly date, int hour, int minute = 0)
        => new DateTimeOffset(date.ToDateTime(new TimeOnly(hour, minute)), Ist).ToUniversalTime();

    [Fact]
    public void Adding_minutes_inside_one_working_day_stays_on_that_day()
    {
        var schedule = StandardWeek();

        var due = schedule.AddBusinessMinutes(Ist_(Monday, 10), 120);

        due.ShouldBe(Ist_(Monday, 12));
    }

    [Fact]
    public void Adding_minutes_past_closing_time_continues_the_next_morning()
    {
        var schedule = StandardWeek();

        // 17:00 leaves one working hour on Monday; the second hour lands on Tuesday morning.
        var due = schedule.AddBusinessMinutes(Ist_(Monday, 17), 120);

        due.ShouldBe(Ist_(Monday.AddDays(1), 10));
    }

    [Fact]
    public void Weekends_do_not_consume_sla_time()
    {
        var schedule = StandardWeek();
        var friday = Monday.AddDays(4);

        // One hour on Friday evening, then the balance from Monday's opening.
        var due = schedule.AddBusinessMinutes(Ist_(friday, 17), 120);

        due.ShouldBe(Ist_(Monday.AddDays(7), 10));
    }

    [Fact]
    public void Holidays_do_not_consume_sla_time()
    {
        // Thursday 1 October 2026 with Gandhi Jayanti on Friday the 2nd, then the weekend.
        var schedule = StandardWeek((10, 2, "Gandhi Jayanti"));
        var thursday = new DateOnly(2026, 10, 1);

        var due = schedule.AddBusinessMinutes(Ist_(thursday, 17), 120);

        due.ShouldBe(Ist_(new DateOnly(2026, 10, 5), 10));
    }

    [Fact]
    public void A_clock_started_outside_working_hours_begins_at_the_next_opening()
    {
        var schedule = StandardWeek();
        var sunday = Monday.AddDays(-1);

        var due = schedule.AddBusinessMinutes(Ist_(sunday, 12), 60);

        due.ShouldBe(Ist_(Monday, 10));
    }

    [Fact]
    public void A_clock_started_before_opening_does_not_count_the_early_hours()
    {
        var schedule = StandardWeek();

        var due = schedule.AddBusinessMinutes(Ist_(Monday, 6), 60);

        due.ShouldBe(Ist_(Monday, 10));
    }

    [Fact]
    public void Elapsed_time_counts_only_working_minutes()
    {
        var schedule = StandardWeek();
        var friday = Monday.AddDays(4);

        // Friday 17:00 to Monday 10:00 is 65 wall-clock hours but only two working hours.
        var minutes = schedule.BusinessMinutesBetween(Ist_(friday, 17), Ist_(Monday.AddDays(7), 10));

        minutes.ShouldBe(120);
    }

    [Fact]
    public void Elapsed_time_over_a_full_working_week_is_forty_five_hours()
    {
        var schedule = StandardWeek();

        var minutes = schedule.BusinessMinutesBetween(Ist_(Monday, 9), Ist_(Monday.AddDays(7), 9));

        // Five days at nine hours each.
        minutes.ShouldBe(5 * 9 * 60);
    }

    [Fact]
    public void Elapsed_time_is_zero_when_the_end_precedes_the_start()
    {
        var schedule = StandardWeek();

        schedule.BusinessMinutesBetween(Ist_(Monday, 12), Ist_(Monday, 10)).ShouldBe(0);
    }

    [Fact]
    public void Adding_zero_or_negative_minutes_returns_the_start()
    {
        var schedule = StandardWeek();
        var start = Ist_(Monday, 10);

        schedule.AddBusinessMinutes(start, 0).ShouldBe(start);
        schedule.AddBusinessMinutes(start, -30).ShouldBe(start);
    }

    [Fact]
    public void A_twenty_four_seven_schedule_counts_every_minute()
    {
        var schedule = BusinessSchedule.TwentyFourSeven();
        var saturday = Monday.AddDays(5);

        // A P1 commitment does not stop at six o'clock, or at the weekend.
        schedule.AddBusinessMinutes(Ist_(saturday, 22), 240)
            .ShouldBe(Ist_(saturday.AddDays(1), 2));

        schedule.BusinessMinutesBetween(Ist_(saturday, 0), Ist_(saturday.AddDays(2), 0))
            .ShouldBe(2 * 24 * 60);
    }

    [Fact]
    public void A_calendar_with_no_working_windows_falls_back_to_counting_every_minute()
    {
        // Failing towards a tighter commitment is deliberate: an SLA that is too aggressive is
        // visible on the dashboard, whereas one that can never fire is silently useless.
        var schedule = BusinessSchedule.FromCalendar("India Standard Time", false, [], []);

        schedule.AddBusinessMinutes(Ist_(Monday, 22), 120).ShouldBe(Ist_(Monday.AddDays(1), 0));
    }

    [Fact]
    public void Windows_with_a_non_positive_span_are_ignored_rather_than_stalling_the_walker()
    {
        var calendarId = Guid.NewGuid();

        var windows = new List<BusinessCalendarWindow>
        {
            // Malformed: end at or before start. Must not make the day-walker spin.
            new() { BusinessCalendarId = calendarId, DayOfWeek = DayOfWeek.Monday, StartMinute = 600, EndMinute = 600 },
            new() { BusinessCalendarId = calendarId, DayOfWeek = DayOfWeek.Tuesday, StartMinute = 540, EndMinute = 1080 }
        };

        var schedule = BusinessSchedule.FromCalendar("India Standard Time", false, windows, []);

        schedule.AddBusinessMinutes(Ist_(Monday, 10), 60)
            .ShouldBe(Ist_(Monday.AddDays(1), 10));
    }

    [Fact]
    public void Several_windows_in_one_day_exclude_the_gap_between_them()
    {
        var calendarId = Guid.NewGuid();

        // 09:00-13:00 and 14:00-18:00, with an hour closed for lunch.
        var windows = new List<BusinessCalendarWindow>
        {
            new() { BusinessCalendarId = calendarId, DayOfWeek = DayOfWeek.Monday, StartMinute = 540, EndMinute = 780 },
            new() { BusinessCalendarId = calendarId, DayOfWeek = DayOfWeek.Monday, StartMinute = 840, EndMinute = 1080 }
        };

        var schedule = BusinessSchedule.FromCalendar("India Standard Time", false, windows, []);

        // Two hours from noon: one before the break, one after it.
        schedule.AddBusinessMinutes(Ist_(Monday, 12), 120).ShouldBe(Ist_(Monday, 15));

        // Noon to 15:00 is three wall-clock hours but only two working ones.
        schedule.BusinessMinutesBetween(Ist_(Monday, 12), Ist_(Monday, 15)).ShouldBe(120);
    }

    [Fact]
    public void A_recurring_holiday_applies_in_every_year()
    {
        var calendarId = Guid.NewGuid();

        var windows = Enum.GetValues<DayOfWeek>()
            .Select(day => new BusinessCalendarWindow
            {
                BusinessCalendarId = calendarId,
                DayOfWeek = day,
                StartMinute = 540,
                EndMinute = 1080
            })
            .ToList();

        var holidays = new List<BusinessCalendarHoliday>
        {
            new()
            {
                BusinessCalendarId = calendarId,
                Date = new DateOnly(2020, 1, 26),
                Name = "Republic Day",
                IsRecurringAnnually = true
            }
        };

        var schedule = BusinessSchedule.FromCalendar("India Standard Time", false, windows, holidays);

        schedule.IsHoliday(new DateOnly(2026, 1, 26)).ShouldBeTrue();
        schedule.IsHoliday(new DateOnly(2030, 1, 26)).ShouldBeTrue();
        schedule.IsHoliday(new DateOnly(2026, 1, 27)).ShouldBeFalse();
    }

    [Fact]
    public void An_unknown_timezone_falls_back_to_india_standard_time_rather_than_throwing()
    {
        // A tenant carrying an identifier this host does not know must not take the SLA engine
        // down. IST is the product default and has no daylight saving, so the fallback is exact.
        var zone = BusinessSchedule.ResolveTimeZone("Mars/Olympus_Mons");

        zone.BaseUtcOffset.ShouldBe(TimeSpan.FromMinutes(330));
    }

    [Theory]
    [InlineData("India Standard Time")]
    [InlineData("Asia/Kolkata")]
    public void Both_windows_and_iana_timezone_identifiers_resolve_to_ist(string timeZoneId)
    {
        BusinessSchedule.ResolveTimeZone(timeZoneId).BaseUtcOffset
            .ShouldBe(TimeSpan.FromMinutes(330));
    }

    [Fact]
    public void Working_days_exclude_weekends_and_holidays()
    {
        var schedule = StandardWeek((10, 2, "Gandhi Jayanti"));

        schedule.IsWorkingDay(Monday).ShouldBeTrue();
        schedule.IsWorkingDay(Monday.AddDays(5)).ShouldBeFalse();  // Saturday
        schedule.IsWorkingDay(Monday.AddDays(6)).ShouldBeFalse();  // Sunday
        schedule.IsWorkingDay(new DateOnly(2026, 10, 2)).ShouldBeFalse();
    }
}
