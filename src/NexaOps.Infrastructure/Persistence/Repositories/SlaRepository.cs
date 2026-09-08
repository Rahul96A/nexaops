using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Sla;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;

namespace NexaOps.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ISlaRepository" />
public sealed class SlaRepository : ISlaRepository, ISlaRepositoryScheduleAccessor
{
    private readonly NexaOpsDbContext _context;
    private readonly IApplicationCache _cache;
    private readonly ITenantContext _tenant;

    /// <summary>
    /// Schedules are rebuilt from calendar rows on every SLA calculation, so they are held for
    /// the life of the request as well as in the distributed cache. A single incident detail
    /// page can ask for the same calendar a dozen times.
    /// </summary>
    private readonly Dictionary<Guid, BusinessSchedule> _scopeCache = [];

    private BusinessSchedule? _defaultSchedule;

    public SlaRepository(NexaOpsDbContext context, IApplicationCache cache, ITenantContext tenant)
    {
        _context = context;
        _cache = cache;
        _tenant = tenant;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlaPolicy>> GetActivePoliciesAsync(
        ServiceModule module,
        CancellationToken cancellationToken = default)
        => await _context.SlaPolicies
            .AsNoTracking()
            .Include(p => p.SlaDefinition)
            .Where(p => p.Module == module && p.IsActive && !p.IsArchived)
            .OrderBy(p => p.Order)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<BusinessSchedule> GetScheduleAsync(
        Guid? businessCalendarId,
        CancellationToken cancellationToken = default)
    {
        if (businessCalendarId is null)
        {
            return _defaultSchedule ??= await LoadDefaultScheduleAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_scopeCache.TryGetValue(businessCalendarId.Value, out var cached))
        {
            return cached;
        }

        var calendar = await LoadCalendarAsync(businessCalendarId.Value, cancellationToken)
            .ConfigureAwait(false);

        // A policy pointing at a calendar that has since been archived must not stop the SLA
        // engine. Falling back to 24x7 makes the commitment tighter, which is visible and safe,
        // rather than looser, which would silently under-report breaches.
        var schedule = calendar is null
            ? BusinessSchedule.TwentyFourSeven(_tenant.TimeZoneId)
            : BusinessSchedule.FromCalendar(
                calendar.TimeZoneId,
                calendar.IsTwentyFourSeven,
                calendar.Windows,
                calendar.Holidays);

        _scopeCache[businessCalendarId.Value] = schedule;
        return schedule;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlaInstance>> GetInstancesForRecordAsync(
        ServiceModule module,
        Guid recordId,
        CancellationToken cancellationToken = default)
        => await _context.SlaInstances
            .Include(s => s.SlaDefinition)
            .Where(s => s.Module == module && s.RecordId == recordId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<BusinessSchedule> LoadDefaultScheduleAsync(CancellationToken cancellationToken)
    {
        var calendar = await _context.BusinessCalendars
            .AsNoTracking()
            .Include(c => c.Windows)
            .Include(c => c.Holidays)
            .Where(c => c.IsDefault && c.IsActive && !c.IsArchived)
            .OrderBy(c => c.Code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return calendar is null
            ? BusinessSchedule.TwentyFourSeven(_tenant.TimeZoneId)
            : BusinessSchedule.FromCalendar(
                calendar.TimeZoneId,
                calendar.IsTwentyFourSeven,
                calendar.Windows,
                calendar.Holidays);
    }

    private Task<BusinessCalendar?> LoadCalendarAsync(Guid id, CancellationToken cancellationToken)
        => _context.BusinessCalendars
            .AsNoTracking()
            .Include(c => c.Windows)
            .Include(c => c.Holidays)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsArchived, cancellationToken);
}
