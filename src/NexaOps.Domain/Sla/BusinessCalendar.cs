using NexaOps.Domain.Common;

namespace NexaOps.Domain.Sla;

/// <summary>
/// A working-time definition: which hours on which days count towards an SLA, and which dates
/// are holidays. Indian customers typically run one calendar per location because the public
/// holiday list differs by state.
/// </summary>
public class BusinessCalendar : TenantEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Windows and holidays are interpreted in this timezone. Defaults to IST.</summary>
    public string TimeZoneId { get; set; } = "India Standard Time";

    /// <summary>Used by SLA policies that do not name a calendar explicitly.</summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// When true, every minute counts and the windows are ignored. This is the correct setting
    /// for P1 production-outage commitments, which do not stop at 6pm.
    /// </summary>
    public bool IsTwentyFourSeven { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<BusinessCalendarWindow> Windows { get; set; } = new List<BusinessCalendarWindow>();
    public ICollection<BusinessCalendarHoliday> Holidays { get; set; } = new List<BusinessCalendarHoliday>();
}

/// <summary>
/// One working window on one weekday, expressed as minutes from local midnight. Several
/// windows per day are supported so a calendar can exclude a lunch break.
/// </summary>
public class BusinessCalendarWindow : TenantEntity
{
    public Guid BusinessCalendarId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Inclusive start, minutes from local midnight. 540 = 09:00.</summary>
    public int StartMinute { get; set; }

    /// <summary>Exclusive end, minutes from local midnight. 1080 = 18:00.</summary>
    public int EndMinute { get; set; }

    public BusinessCalendar? BusinessCalendar { get; set; }
}

/// <summary>A non-working date on a calendar.</summary>
public class BusinessCalendarHoliday : TenantEntity
{
    public Guid BusinessCalendarId { get; set; }

    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// True for fixed-date holidays such as Republic Day (26 January) that repeat every year.
    /// Festival dates that follow the lunar calendar are stored year by year instead.
    /// </summary>
    public bool IsRecurringAnnually { get; set; }

    public BusinessCalendar? BusinessCalendar { get; set; }
}
