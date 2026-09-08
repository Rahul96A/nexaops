using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexaOps.Application.Abstractions;
using NexaOps.Application.Security;
using NexaOps.Domain.Identity;
using NexaOps.Domain.Localisation;
using NexaOps.Domain.Platform;
using NexaOps.Domain.ServiceDesk;
using NexaOps.Domain.Sla;
using NexaOps.Infrastructure.Persistence;

namespace NexaOps.Api.Seeding;

/// <summary>
/// Stands up everything a new tenant needs before anyone can use it: roles and permission
/// grants, the priority matrix, business calendars, SLA definitions and policies, the incident
/// taxonomy, and record number sequences.
/// <para>
/// This runs for every customer, not only for demos. It is idempotent, so re-running it after a
/// release that adds a permission or an SLA target tops the tenant up without disturbing
/// anything an administrator has customised.
/// </para>
/// </summary>
public sealed class TenantProvisioningService
{
    private readonly NexaOpsDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        NexaOpsDbContext context,
        IPasswordHasher<User> passwordHasher,
        IDateTimeProvider clock,
        ILogger<TenantProvisioningService> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Creates the tenant if it does not exist and provisions its baseline configuration.
    /// Returns the tenant, whether it was newly created or already present.
    /// </summary>
    public async Task<Tenant> ProvisionAsync(
        TenantProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Provisioning legitimately spans tenants: it runs before any tenant scope exists.
        // The scope restores whatever the caller had, so a seeder that already suppressed the
        // filter is not silently un-suppressed when this method returns.
        using var suppression = _context.SuppressTenantFilter();

        // Nobody performed these writes: they are the tenant's initial configuration, applied by
        // the system. Auditing them would add a JSON snapshot for every one of several hundred
        // permission grants, which is noise in the trail and the dominant cost of provisioning.
        // Deliberate administrative changes made afterwards are audited normally.
        using var auditSuppression = _context.SuppressAuditCapture();

    var code = request.Code.Trim().ToLowerInvariant();

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Code = code,
                Name = request.Name,
                LegalName = request.LegalName,
                PrimaryDomain = request.PrimaryDomain,
                Status = request.Status,
                TimeZoneId = IndiaReference.DefaultTimeZoneId,
                Locale = IndiaReference.DefaultLocale,
                CurrencyCode = IndiaReference.CurrencyCode,
                DateFormat = IndiaReference.DefaultDateFormat,
                DataRegion = "centralindia",
                CreatedAt = _clock.UtcNow
            };

            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Provisioned tenant {TenantCode} ({TenantId}).", tenant.Code, tenant.Id);
        }

        await ProvisionRolesAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        await ProvisionPriorityMatrixAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        await ProvisionNumberSequencesAsync(tenant.Id, cancellationToken).ConfigureAwait(false);

        var calendars = await ProvisionCalendarsAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
        await ProvisionSlaAsync(tenant.Id, calendars, cancellationToken).ConfigureAwait(false);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return tenant;
    }

    /// <summary>
    /// Creates any missing system role and tops up its permission grants.
    /// <para>
    /// Grants are only ever added, never removed. A customer who has deliberately taken a
    /// permission away from a role must not have it silently restored by a deployment.
    /// </para>
    /// </summary>
    private async Task ProvisionRolesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = await _context.Roles
            .Include(r => r.RolePermissions)
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var definition in SystemRoles.All)
        {
            var role = existing.FirstOrDefault(r =>
                string.Equals(r.Code, definition.Code, StringComparison.Ordinal));

            if (role is null)
            {
                role = new Role
                {
                    TenantId = tenantId,
                    Code = definition.Code,
                    Name = definition.Name,
                    Description = definition.Description,
                    IsSystem = true,
                    IsPlatformScoped = definition.IsPlatformScoped,
                    CreatedAt = _clock.UtcNow
                };

                _context.Roles.Add(role);
                existing.Add(role);
            }

            var held = role.RolePermissions
                .Select(rp => rp.PermissionCode)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var permission in definition.Permissions.Where(Permissions.IsKnown))
            {
                if (held.Add(permission))
                {
                    var grant = new RolePermission
                    {
                        TenantId = tenantId,
                        RoleId = role.Id,
                        PermissionCode = permission,
                        CreatedAt = _clock.UtcNow
                    };

                    role.RolePermissions.Add(grant);
                    _context.RolePermissions.Add(grant);
                }
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Seeds the 4x4 impact-by-urgency grid, leaving any cell an admin has re-tuned.</summary>
    private async Task ProvisionPriorityMatrixAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = await _context.PriorityMatrixEntries
            .Where(e => e.TenantId == tenantId)
            .Select(e => new { e.Impact, e.Urgency })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var present = existing.Select(e => (e.Impact, e.Urgency)).ToHashSet();

        foreach (var (impact, urgency, priority) in PriorityCalculator.DefaultMatrix())
        {
            if (present.Contains((impact, urgency)))
            {
                continue;
            }

            _context.PriorityMatrixEntries.Add(new PriorityMatrixEntry
            {
                TenantId = tenantId,
                Impact = impact,
                Urgency = urgency,
                Priority = priority,
                CreatedAt = _clock.UtcNow
            });
        }
    }

    private async Task ProvisionNumberSequencesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = await _context.NumberSequences
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Sequences for later modules are created now so their numbering starts at 1 whenever
        // those modules ship, rather than being invented mid-life.
        foreach (var key in new[] { "INC", "REQ", "CHG", "PRB", "TASK", "KB", "ASSET", "CI" })
        {
            if (existing.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            _context.NumberSequences.Add(new NumberSequence
            {
                TenantId = tenantId,
                Key = key,
                Prefix = key,
                NextValue = 1,
                PadWidth = 7,
                CreatedAt = _clock.UtcNow
            });
        }
    }

    /// <summary>
    /// Creates the two calendars almost every Indian customer needs: standard business hours
    /// (Monday to Friday, 09:00-18:00 IST, national holidays) and a 24x7 calendar for
    /// production-outage commitments.
    /// </summary>
    private async Task<CalendarSet> ProvisionCalendarsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var calendars = await _context.BusinessCalendars
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var business = calendars.FirstOrDefault(c => c.Code == "IN-BUSINESS");
        var alwaysOn = calendars.FirstOrDefault(c => c.Code == "24X7");

        if (business is null)
        {
            business = new BusinessCalendar
            {
                TenantId = tenantId,
                Code = "IN-BUSINESS",
                Name = "India business hours",
                Description = "Monday to Friday, 09:00-18:00 IST, excluding national holidays.",
                TimeZoneId = IndiaReference.DefaultTimeZoneId,
                IsDefault = true,
                IsTwentyFourSeven = false,
                CreatedAt = _clock.UtcNow
            };

            _context.BusinessCalendars.Add(business);

            foreach (var day in new[]
                     {
                         DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                         DayOfWeek.Thursday, DayOfWeek.Friday
                     })
            {
                _context.BusinessCalendarWindows.Add(new BusinessCalendarWindow
                {
                    TenantId = tenantId,
                    BusinessCalendarId = business.Id,
                    DayOfWeek = day,
                    StartMinute = 9 * 60,
                    EndMinute = 18 * 60,
                    CreatedAt = _clock.UtcNow
                });
            }

            // Fixed-date national holidays only. Festival dates follow the lunar calendar and
            // are added per year by the customer rather than guessed at in code.
            var year = _clock.UtcNow.Year;
            foreach (var (month, day, name) in IndiaReference.FixedNationalHolidays)
            {
                _context.BusinessCalendarHolidays.Add(new BusinessCalendarHoliday
                {
                    TenantId = tenantId,
                    BusinessCalendarId = business.Id,
                    Date = new DateOnly(year, month, day),
                    Name = name,
                    IsRecurringAnnually = true,
                    CreatedAt = _clock.UtcNow
                });
            }
        }

        if (alwaysOn is null)
        {
            alwaysOn = new BusinessCalendar
            {
                TenantId = tenantId,
                Code = "24X7",
                Name = "24x7",
                Description = "Every minute counts. Used for critical production commitments.",
                TimeZoneId = IndiaReference.DefaultTimeZoneId,
                IsDefault = false,
                IsTwentyFourSeven = true,
                CreatedAt = _clock.UtcNow
            };

            _context.BusinessCalendars.Add(alwaysOn);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CalendarSet(business.Id, alwaysOn.Id);
    }

    /// <summary>
    /// Seeds response and resolution commitments per priority, and the policies that bind them.
    /// <para>
    /// P1 runs on the 24x7 calendar because a production outage does not pause at six o'clock;
    /// everything else runs on business hours, which is what a customer is actually paying for.
    /// </para>
    /// </summary>
    private async Task ProvisionSlaAsync(
        Guid tenantId,
        CalendarSet calendars,
        CancellationToken cancellationToken)
    {
        var existingCodes = await _context.SlaDefinitions
            .Where(d => d.TenantId == tenantId)
            .Select(d => d.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var targets = new (Priority Priority, int ResponseMinutes, int ResolutionMinutes, bool AlwaysOn)[]
        {
            (Priority.P1Critical, 15, 240, true),
            (Priority.P2High, 30, 480, false),
            (Priority.P3Moderate, 120, 1440, false),
            (Priority.P4Low, 240, 2880, false),
            (Priority.P5Planning, 480, 5760, false)
        };

        var definitions = new List<SlaDefinition>();

        foreach (var (priority, responseMinutes, resolutionMinutes, alwaysOn) in targets)
        {
            var calendarId = alwaysOn ? calendars.AlwaysOnId : calendars.BusinessId;

            definitions.Add(EnsureDefinition(
                tenantId, existingCodes, $"INC-{priority}-RESPONSE",
                $"{Describe(priority)} response", SlaTargetType.Response,
                responseMinutes, calendarId));

            definitions.Add(EnsureDefinition(
                tenantId, existingCodes, $"INC-{priority}-RESOLUTION",
                $"{Describe(priority)} resolution", SlaTargetType.Resolution,
                resolutionMinutes, calendarId));
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var allDefinitions = await _context.SlaDefinitions
            .Where(d => d.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingPolicies = await _context.SlaPolicies
            .Where(p => p.TenantId == tenantId)
            .Select(p => new { p.SlaDefinitionId, p.Priority })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var order = 10;

        foreach (var (priority, _, _, _) in targets)
        {
            foreach (var suffix in new[] { "RESPONSE", "RESOLUTION" })
            {
                var definition = allDefinitions
                    .FirstOrDefault(d => d.Code == $"INC-{priority}-{suffix}");

                if (definition is null
                    || existingPolicies.Any(p => p.SlaDefinitionId == definition.Id && p.Priority == priority))
                {
                    continue;
                }

                _context.SlaPolicies.Add(new SlaPolicy
                {
                    TenantId = tenantId,
                    Name = definition.Name,
                    SlaDefinitionId = definition.Id,
                    Module = ServiceModule.Incident,
                    Priority = priority,
                    Order = order,
                    IsActive = true,
                    CreatedAt = _clock.UtcNow
                });

                order += 10;
            }
        }
    }

    private SlaDefinition EnsureDefinition(
        Guid tenantId,
        List<string> existingCodes,
        string code,
        string name,
        SlaTargetType targetType,
        int durationMinutes,
        Guid calendarId)
    {
        var definition = new SlaDefinition
        {
            TenantId = tenantId,
            Code = code,
            Name = name,
            Module = ServiceModule.Incident,
            TargetType = targetType,
            DurationMinutes = durationMinutes,
            BusinessCalendarId = calendarId,
            WarningThresholdPercent = 80,

            // Response measures time to first contact, so it never pauses; waiting on the
            // requester before anyone has replied is still the service desk's silence.
            PauseWhenPending = targetType != SlaTargetType.Response,
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

        if (!existingCodes.Contains(code, StringComparer.Ordinal))
        {
            _context.SlaDefinitions.Add(definition);
            existingCodes.Add(code);
        }

        return definition;
    }

    private static string Describe(Priority priority) => priority switch
    {
        Priority.P1Critical => "P1 critical",
        Priority.P2High => "P2 high",
        Priority.P3Moderate => "P3 moderate",
        Priority.P4Low => "P4 low",
        _ => "P5 planning"
    };

    /// <summary>Hashes a password for a seeded account.</summary>
    internal string HashPassword(User user, string password) => _passwordHasher.HashPassword(user, password);

    private sealed record CalendarSet(Guid BusinessId, Guid AlwaysOnId);
}

/// <summary>Everything needed to stand up a tenant.</summary>
public sealed class TenantProvisioningRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? LegalName { get; init; }
    public string? PrimaryDomain { get; init; }
    public TenantStatus Status { get; init; } = TenantStatus.Active;
}
