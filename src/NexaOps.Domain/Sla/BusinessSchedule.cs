namespace NexaOps.Domain.Sla;

/// <summary>
/// An immutable, database-free snapshot of a <see cref="BusinessCalendar"/> plus the arithmetic
/// that SLA targets depend on.
/// <para>
/// Keeping this a pure value object matters: SLA due dates are the number a customer will argue
/// about in a contract review, so the logic must be unit-testable to the minute without a
/// database, a clock, or a DI container. Everything here operates on UTC instants at the
/// boundary and converts to calendar-local time internally, which is what makes daylight-saving
/// and cross-timezone tenants behave correctly.
/// </para>
/// </summary>
public sealed class BusinessSchedule
{
    /// <summary>Safety stop for the day-walking loops: no SLA target may exceed ten years.</summary>
    private const int MaxDaysToWalk = 3660;

    private readonly TimeZoneInfo _timeZone;
    private readonly bool _isTwentyFourSeven;

    /// <summary>Working windows indexed by weekday, each sorted and non-overlapping.</summary>
    private readonly Dictionary<DayOfWeek, List<(int Start, int End)>> _windows;

    private readonly HashSet<DateOnly> _fixedHolidays;
    private readonly HashSet<(int Month, int Day)> _recurringHolidays;

    private BusinessSchedule(
        TimeZoneInfo timeZone,
        bool isTwentyFourSeven,
        Dictionary<DayOfWeek, List<(int, int)>> windows,
        HashSet<DateOnly> fixedHolidays,
        HashSet<(int, int)> recurringHolidays)
    {
        _timeZone = timeZone;
        _isTwentyFourSeven = isTwentyFourSeven;
        _windows = windows;
        _fixedHolidays = fixedHolidays;
        _recurringHolidays = recurringHolidays;
    }

    /// <summary>True when every minute of every day counts towards SLA targets.</summary>
    public bool IsTwentyFourSeven => _isTwentyFourSeven;

    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>Builds a schedule from persisted calendar rows.</summary>
    public static BusinessSchedule FromCalendar(
        string timeZoneId,
        bool isTwentyFourSeven,
        IEnumerable<BusinessCalendarWindow> windows,
        IEnumerable<BusinessCalendarHoliday> holidays)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(holidays);

        var byDay = new Dictionary<DayOfWeek, List<(int, int)>>();
        foreach (var window in windows)
        {
            if (window.EndMinute <= window.StartMinute)
            {
                // A malformed window would otherwise cause the walker to make no progress.
                continue;
            }

            var clampedStart = Math.Clamp(window.StartMinute, 0, MinutesPerDay);
            var clampedEnd = Math.Clamp(window.EndMinute, 0, MinutesPerDay);
            if (clampedEnd <= clampedStart)
            {
                continue;
            }

            if (!byDay.TryGetValue(window.DayOfWeek, out var list))
            {
                list = [];
                byDay[window.DayOfWeek] = list;
            }

            list.Add((clampedStart, clampedEnd));
        }

        foreach (var list in byDay.Values)
        {
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }

        var fixedHolidays = new HashSet<DateOnly>();
        var recurringHolidays = new HashSet<(int, int)>();
        foreach (var holiday in holidays)
        {
            if (holiday.IsRecurringAnnually)
            {
                recurringHolidays.Add((holiday.Date.Month, holiday.Date.Day));
            }
            else
            {
                fixedHolidays.Add(holiday.Date);
            }
        }

        return new BusinessSchedule(
            ResolveTimeZone(timeZoneId),
            isTwentyFourSeven,
            byDay,
            fixedHolidays,
            recurringHolidays);
    }

    /// <summary>A schedule where every minute counts. Used for P1 commitments and as a fallback.</summary>
    public static BusinessSchedule TwentyFourSeven(string timeZoneId = "India Standard Time")
        => new(ResolveTimeZone(timeZoneId), true, [], [], []);

    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Resolves a timezone id tolerantly. .NET maps between Windows and IANA identifiers, but a
    /// tenant carrying an id unknown to the host must not take the SLA engine down - we fall
    /// back to IST, which is the product default, rather than throwing.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return IndiaStandardTime;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return IndiaStandardTime;
        }
        catch (InvalidTimeZoneException)
        {
            return IndiaStandardTime;
        }
    }

    private static TimeZoneInfo IndiaStandardTime
    {
        get
        {
            foreach (var candidate in new[] { "India Standard Time", "Asia/Kolkata", "Asia/Calcutta" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(candidate);
                }
                catch (TimeZoneNotFoundException)
                {
                    // Try the next identifier.
                }
                catch (InvalidTimeZoneException)
                {
                    // Try the next identifier.
                }
            }

            // Last resort: a fixed +05:30 offset. India has no daylight saving, so this is exact.
            return TimeZoneInfo.CreateCustomTimeZone("NexaOps-IST", TimeSpan.FromMinutes(330), "IST", "IST");
        }
    }

    /// <summary>True when the given local date is a working day on this calendar.</summary>
    public bool IsWorkingDay(DateOnly date)
    {
        if (_isTwentyFourSeven)
        {
            return true;
        }

        return !IsHoliday(date) && _windows.ContainsKey(date.DayOfWeek);
    }

    public bool IsHoliday(DateOnly date)
        => _fixedHolidays.Contains(date) || _recurringHolidays.Contains((date.Month, date.Day));

    /// <summary>
    /// Adds a number of working minutes to an instant and returns the resulting instant.
    /// This is how an SLA due date is calculated from a start time and a target duration.
    /// </summary>
    public DateTimeOffset AddBusinessMinutes(DateTimeOffset startUtc, int minutes)
    {
        if (minutes <= 0)
        {
            return startUtc;
        }

        if (_isTwentyFourSeven || _windows.Count == 0)
        {
            // With no working windows configured, treating the calendar as 24x7 is the safe
            // failure mode: an SLA that is too tight is visible, one that never fires is not.
            return startUtc.AddMinutes(minutes);
        }

        var local = TimeZoneInfo.ConvertTime(startUtc, _timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var minuteOfDay = (local.Hour * 60) + local.Minute;
        var remaining = minutes;

        for (var day = 0; day <= MaxDaysToWalk; day++)
        {
            if (day > 0)
            {
                minuteOfDay = 0;
            }

            foreach (var (windowStart, windowEnd) in WindowsFor(date))
            {
                if (windowEnd <= minuteOfDay)
                {
                    continue;
                }

                var from = Math.Max(minuteOfDay, windowStart);
                var available = windowEnd - from;

                if (available >= remaining)
                {
                    return ToUtc(date, from + remaining);
                }

                remaining -= available;
            }

            date = date.AddDays(1);
        }

        throw new InvalidOperationException(
            $"Could not place {minutes} business minutes within {MaxDaysToWalk} days. " +
            "The business calendar has too few working windows for this target.");
    }

    /// <summary>
    /// Counts the working minutes between two instants. Used to report elapsed SLA time and to
    /// recompute a due date after a pause.
    /// </summary>
    public int BusinessMinutesBetween(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (toUtc <= fromUtc)
        {
            return 0;
        }

        if (_isTwentyFourSeven || _windows.Count == 0)
        {
            var totalMinutes = (toUtc - fromUtc).TotalMinutes;
            return totalMinutes >= int.MaxValue ? int.MaxValue : (int)totalMinutes;
        }

        var localFrom = TimeZoneInfo.ConvertTime(fromUtc, _timeZone);
        var localTo = TimeZoneInfo.ConvertTime(toUtc, _timeZone);

        var date = DateOnly.FromDateTime(localFrom.DateTime);
        var endDate = DateOnly.FromDateTime(localTo.DateTime);
        var startMinuteOfDay = (localFrom.Hour * 60) + localFrom.Minute;
        var endMinuteOfDay = (localTo.Hour * 60) + localTo.Minute;

        var total = 0;
        for (var day = 0; day <= MaxDaysToWalk && date <= endDate; day++)
        {
            var lowerBound = day == 0 ? startMinuteOfDay : 0;
            var upperBound = date == endDate ? endMinuteOfDay : MinutesPerDay;

            foreach (var (windowStart, windowEnd) in WindowsFor(date))
            {
                var overlapStart = Math.Max(windowStart, lowerBound);
                var overlapEnd = Math.Min(windowEnd, upperBound);
                if (overlapEnd > overlapStart)
                {
                    total += overlapEnd - overlapStart;
                }
            }

            date = date.AddDays(1);
        }

        return total;
    }

    private IEnumerable<(int Start, int End)> WindowsFor(DateOnly date)
    {
        if (IsHoliday(date))
        {
            return [];
        }

        return _windows.TryGetValue(date.DayOfWeek, out var windows)
            ? windows
            : (IEnumerable<(int, int)>)[];
    }

    /// <summary>
    /// Converts a calendar-local date plus minute-of-day back to a UTC instant, tolerating the
    /// invalid and ambiguous local times that daylight-saving transitions produce.
    /// </summary>
    private DateTimeOffset ToUtc(DateOnly date, int minuteOfDay)
    {
        var carriedDays = Math.DivRem(minuteOfDay, MinutesPerDay, out var withinDay);
        var effectiveDate = date.AddDays(carriedDays);

        var naive = effectiveDate.ToDateTime(new TimeOnly(withinDay / 60, withinDay % 60));
        naive = DateTime.SpecifyKind(naive, DateTimeKind.Unspecified);

        if (_timeZone.IsInvalidTime(naive))
        {
            // The wall clock skipped this instant. Step forward past the gap.
            naive = naive.AddHours(1);
        }

        var offset = _timeZone.GetUtcOffset(naive);
        return new DateTimeOffset(naive, offset).ToUniversalTime();
    }
}
