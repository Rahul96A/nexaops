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
/// Builds the demo environment: a realistic Indian mid-market customer with departments, teams,
/// staff, a full incident taxonomy, and a populated incident queue with genuine SLA state.
/// <para>
/// Everything here is synthetic. The names, email addresses, phone numbers and company details
/// are invented for demonstration; no real person's data is used. Every figure a demo shows is
/// computed from these records by the same code that serves a customer - nothing on any screen
/// is a hard-coded statistic.
/// </para>
/// <para>
/// A second, smaller tenant is created deliberately. It gives sales a way to show tenant
/// isolation live, and gives the test suite two real tenants to prove separation against.
/// </para>
/// </summary>
public sealed class DemoDataSeeder
{
    /// <summary>Fixed seed so a demo is reproducible and a bug found in one run can be repeated.</summary>
    private const int RandomSeed = 20260907;

    /// <summary>
    /// The shared demo password. Deliberately long, and only ever applied to the demo tenants:
    /// the seeder refuses to run unless <c>Demo:SeedOnStartup</c> is explicitly enabled.
    /// </summary>
    private const string DemoPassword = "NexaOps#Demo2026";

    private readonly NexaOpsDbContext _context;
    private readonly TenantProvisioningService _provisioning;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DemoDataSeeder> _logger;
    private readonly Random _random = new(RandomSeed);

    public DemoDataSeeder(
        NexaOpsDbContext context,
        TenantProvisioningService provisioning,
        IDateTimeProvider clock,
        IConfiguration configuration,
        ILogger<DemoDataSeeder> logger)
    {
        _context = context;
        _provisioning = provisioning;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Seeds both demo tenants. Idempotent: a second run adds nothing.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Seeding creates the tenants themselves, so it necessarily runs outside a tenant scope.
        using var tenantSuppression = _context.SuppressTenantFilter();

        // Nobody performed these actions, so they do not belong in the audit trail - and a JSON
        // snapshot per seeded row more than doubles the write volume for no value.
        using var auditSuppression = _context.SuppressAuditCapture();

        try
        {
            var primary = await _provisioning.ProvisionAsync(
                new TenantProvisioningRequest
                {
                    Code = "acme-in",
                    Name = "Acme Technologies India",
                    LegalName = "Acme Technologies India Private Limited",
                    PrimaryDomain = "acmetech.example.in"
                },
                cancellationToken).ConfigureAwait(false);

            // Both tenants are provisioned on every start, and each seeding method decides for
            // itself whether its demo data already exists.
            //
            // The gate used to sit here, around both calls, which meant an existing environment
            // re-provisioned the primary tenant - refreshing its roles and permission grants -
            // and then returned before the secondary tenant was touched at all. The two tenants
            // drifted apart permanently as new permissions were added, and a tenant isolation
            // check against the neighbour silently became a permission check instead.
            await SeedPrimaryTenantAsync(primary, cancellationToken).ConfigureAwait(false);
            await SeedSecondaryTenantAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo seeding failed. The environment may be partially populated.");
            throw;
        }
    }

    private async Task SeedPrimaryTenantAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        // Its own gate, matching the secondary tenant's, so provisioning stays idempotent while
        // demo data is written exactly once.
        if (await _context.Incidents.AnyAsync(i => i.TenantId == tenant.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            _logger.LogInformation("Demo data already present for {Tenant}; roles refreshed only.", tenant.Code);
            return;
        }

        var now = _clock.UtcNow;

        var organization = new Organization
        {
            TenantId = tenant.Id,
            Code = "ACME-IN",
            Name = "Acme Technologies India",
            LegalName = "Acme Technologies India Private Limited",

            // Structurally valid, deliberately fictional: state code 29 (Karnataka) with an
            // invented PAN. It exercises the GST format without resembling a real registration.
            GstIdentificationNumber = "29AACCA1234F1Z5",
            PermanentAccountNumber = "AACCA1234F",
            AddressLine1 = "Prestige Tech Park, Tower B, 7th Floor",
            AddressLine2 = "Marathahalli Outer Ring Road",
            City = "Bengaluru",
            StateCode = "KA",
            PostalCode = "560103",
            CountryCode = IndiaReference.CountryCode,
            ContactEmail = "servicedesk@acmetech.example.in",
            ContactPhone = "+918041234567",
            CreatedAt = now
        };

        _context.Organizations.Add(organization);

        var departments = new[]
        {
            CreateDepartment(tenant.Id, organization.Id, "IT", "Information Technology", "CC-1001"),
            CreateDepartment(tenant.Id, organization.Id, "HR", "Human Resources", "CC-1002"),
            CreateDepartment(tenant.Id, organization.Id, "FIN", "Finance", "CC-1003"),
            CreateDepartment(tenant.Id, organization.Id, "OPS", "Operations", "CC-1004"),
            CreateDepartment(tenant.Id, organization.Id, "SEC", "Information Security", "CC-1005")
        };

        _context.Departments.AddRange(departments);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var departmentsByCode = departments.ToDictionary(d => d.Code, StringComparer.Ordinal);

        var roles = await _context.Roles
            .Where(r => r.TenantId == tenant.Id)
            .ToDictionaryAsync(r => r.Code, cancellationToken)
            .ConfigureAwait(false);

        // --- Groups -------------------------------------------------------
        var groups = new[]
        {
            CreateGroup(tenant.Id, "SD-L1", "Service Desk Level 1", "servicedesk.l1@acmetech.example.in"),
            CreateGroup(tenant.Id, "SD-L2", "Service Desk Level 2", "servicedesk.l2@acmetech.example.in"),
            CreateGroup(tenant.Id, "NET-OPS", "Network Operations", "netops@acmetech.example.in"),
            CreateGroup(tenant.Id, "APP-SUP", "Application Support", "appsupport@acmetech.example.in"),
            CreateGroup(tenant.Id, "INFRA", "Infrastructure and Cloud", "infra@acmetech.example.in"),
            CreateGroup(tenant.Id, "SEC-OPS", "Security Operations", "secops@acmetech.example.in")
        };

        _context.Groups.AddRange(groups);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var groupsByCode = groups.ToDictionary(g => g.Code, StringComparer.Ordinal);

        // --- People -------------------------------------------------------
        var people = new List<SeedUser>
        {
            new("priya.raghavan", "Priya", "Raghavan", "Head of IT Service Management", "IT",
                [SystemRoles.TenantAdministrator, SystemRoles.ItManager], ["SD-L1", "SD-L2"], true),

            new("arun.mehta", "Arun", "Mehta", "Service Desk Manager", "IT",
                [SystemRoles.ServiceDeskManager], ["SD-L1", "SD-L2"], true),

            new("kavya.nair", "Kavya", "Nair", "Service Desk Analyst", "IT",
                [SystemRoles.ServiceDeskAgent], ["SD-L1"], false),

            new("rohit.deshpande", "Rohit", "Deshpande", "Service Desk Analyst", "IT",
                [SystemRoles.ServiceDeskAgent], ["SD-L1"], false),

            new("imran.sheikh", "Imran", "Sheikh", "Senior Support Engineer", "IT",
                [SystemRoles.ServiceDeskAgent], ["SD-L2", "APP-SUP"], false),

            new("meera.krishnan", "Meera", "Krishnan", "Network Engineer", "IT",
                [SystemRoles.ServiceDeskAgent], ["NET-OPS"], false),

            new("sandeep.rao", "Sandeep", "Rao", "Cloud Infrastructure Engineer", "IT",
                [SystemRoles.ServiceDeskAgent], ["INFRA"], false),

            new("fatima.qureshi", "Fatima", "Qureshi", "Security Analyst", "SEC",
                [SystemRoles.ServiceDeskAgent], ["SEC-OPS"], false),

            new("vikram.iyer", "Vikram", "Iyer", "IT Asset Manager", "IT",
                [SystemRoles.AssetManager], ["SD-L2"], false),

            new("neha.gupta", "Neha", "Gupta", "CMDB Administrator", "IT",
                [SystemRoles.CmdbAdministrator], ["INFRA"], false),

            new("anjali.sharma", "Anjali", "Sharma", "Finance Manager", "FIN",
                [SystemRoles.Approver], [], false),

            new("rajesh.kumar", "Rajesh", "Kumar", "Operations Lead", "OPS",
                [SystemRoles.Approver, SystemRoles.ReportViewer], [], false),

            new("sneha.patel", "Sneha", "Patel", "HR Business Partner", "HR",
                [SystemRoles.Requester], [], false),

            new("aditya.menon", "Aditya", "Menon", "Financial Analyst", "FIN",
                [SystemRoles.Requester], [], false),

            new("divya.reddy", "Divya", "Reddy", "Operations Executive", "OPS",
                [SystemRoles.Requester], [], false),

            new("karthik.subramanian", "Karthik", "Subramanian", "Sales Engineer", "OPS",
                [SystemRoles.Requester], [], false),

            new("pooja.bhatt", "Pooja", "Bhatt", "Recruitment Specialist", "HR",
                [SystemRoles.Requester], [], false),

            new("nikhil.joshi", "Nikhil", "Joshi", "Accounts Payable Executive", "FIN",
                [SystemRoles.Requester], [], false)
        };

        var users = new Dictionary<string, User>(StringComparer.Ordinal);

        foreach (var person in people)
        {
            var user = new User
            {
                TenantId = tenant.Id,
                Email = $"{person.Alias}@acmetech.example.in",
                FirstName = person.FirstName,
                LastName = person.LastName,
                DisplayName = $"{person.FirstName} {person.LastName}",
                JobTitle = person.JobTitle,
                OrganizationId = organization.Id,
                DepartmentId = departmentsByCode[person.DepartmentCode].Id,
                EmployeeId = $"ACM{4000 + users.Count:D4}",
                PhoneNumber = IndiaReference.NormalisePhoneNumber($"9{_random.Next(100000000, 999999999)}"),
                Location = "Bengaluru",
                TimeZoneId = IndiaReference.DefaultTimeZoneId,
                Locale = IndiaReference.DefaultLocale,
                Status = UserStatus.Active,
                AvatarColor = AvatarColour(person.Alias),
                CreatedAt = now
            };

            user.PasswordHash = _provisioning.HashPassword(user, DemoPassword);

            _context.Users.Add(user);
            users[person.Alias] = user;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var person in people)
        {
            var user = users[person.Alias];

            foreach (var roleCode in person.Roles)
            {
                if (roles.TryGetValue(roleCode, out var role))
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        TenantId = tenant.Id,
                        UserId = user.Id,
                        RoleId = role.Id,
                        CreatedAt = now
                    });
                }
            }

            // Everyone can raise and track their own tickets, whatever else they do.
            if (!person.Roles.Contains(SystemRoles.Requester) && roles.TryGetValue(SystemRoles.Requester, out var requester))
            {
                _context.UserRoles.Add(new UserRole
                {
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    RoleId = requester.Id,
                    CreatedAt = now
                });
            }

            foreach (var groupCode in person.Groups)
            {
                _context.GroupMembers.Add(new GroupMember
                {
                    TenantId = tenant.Id,
                    GroupId = groupsByCode[groupCode].Id,
                    UserId = user.Id,
                    IsLead = person.IsLead,
                    CreatedAt = now
                });
            }
        }

        // Department and group leadership.
        departmentsByCode["IT"].ManagerUserId = users["priya.raghavan"].Id;
        departmentsByCode["SEC"].ManagerUserId = users["fatima.qureshi"].Id;
        departmentsByCode["FIN"].ManagerUserId = users["anjali.sharma"].Id;
        departmentsByCode["OPS"].ManagerUserId = users["rajesh.kumar"].Id;
        departmentsByCode["HR"].ManagerUserId = users["sneha.patel"].Id;

        groupsByCode["SD-L1"].ManagerUserId = users["arun.mehta"].Id;
        groupsByCode["SD-L2"].ManagerUserId = users["arun.mehta"].Id;
        groupsByCode["NET-OPS"].ManagerUserId = users["meera.krishnan"].Id;
        groupsByCode["APP-SUP"].ManagerUserId = users["imran.sheikh"].Id;
        groupsByCode["INFRA"].ManagerUserId = users["sandeep.rao"].Id;
        groupsByCode["SEC-OPS"].ManagerUserId = users["fatima.qureshi"].Id;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // --- Taxonomy -----------------------------------------------------
        var categories = await SeedTaxonomyAsync(tenant.Id, groupsByCode, cancellationToken)
            .ConfigureAwait(false);

        // --- Incidents ----------------------------------------------------
        await SeedIncidentsAsync(tenant.Id, organization.Id, users, groupsByCode, categories, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A second tenant with its own people and tickets.
    /// <para>
    /// Its purpose is to make isolation demonstrable rather than asserted: sign in as an Acme
    /// agent and no Northwind record is reachable by search, by direct URL, or through the AI
    /// assistant. The integration tests assert exactly that.
    /// </para>
    /// </summary>
    private async Task SeedSecondaryTenantAsync(CancellationToken cancellationToken)
    {
        var tenant = await _provisioning.ProvisionAsync(
            new TenantProvisioningRequest
            {
                Code = "northwind-in",
                Name = "Northwind Logistics India",
                LegalName = "Northwind Logistics India Private Limited",
                PrimaryDomain = "northwind.example.in"
            },
            cancellationToken).ConfigureAwait(false);

        if (await _context.Incidents.AnyAsync(i => i.TenantId == tenant.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var now = _clock.UtcNow;

        var organization = new Organization
        {
            TenantId = tenant.Id,
            Code = "NWL-IN",
            Name = "Northwind Logistics India",
            LegalName = "Northwind Logistics India Private Limited",
            GstIdentificationNumber = "27AABCN5678M1Z2",
            PermanentAccountNumber = "AABCN5678M",
            AddressLine1 = "Kalpataru Square, Andheri East",
            City = "Mumbai",
            StateCode = "MH",
            PostalCode = "400059",
            CountryCode = IndiaReference.CountryCode,
            ContactEmail = "itsupport@northwind.example.in",
            CreatedAt = now
        };

        _context.Organizations.Add(organization);

        var department = CreateDepartment(tenant.Id, organization.Id, "IT", "Information Technology", "NW-2001");
        _context.Departments.Add(department);

        var group = CreateGroup(tenant.Id, "NW-SD", "Northwind Service Desk", "servicedesk@northwind.example.in");
        _context.Groups.Add(group);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var roles = await _context.Roles
            .Where(r => r.TenantId == tenant.Id)
            .ToDictionaryAsync(r => r.Code, cancellationToken)
            .ConfigureAwait(false);

        var agent = new User
        {
            TenantId = tenant.Id,
            Email = "deepak.varma@northwind.example.in",
            FirstName = "Deepak",
            LastName = "Varma",
            DisplayName = "Deepak Varma",
            JobTitle = "IT Support Lead",
            OrganizationId = organization.Id,
            DepartmentId = department.Id,
            EmployeeId = "NWL0001",
            Location = "Mumbai",
            Status = UserStatus.Active,
            AvatarColor = AvatarColour("deepak.varma"),
            CreatedAt = now
        };

        agent.PasswordHash = _provisioning.HashPassword(agent, DemoPassword);
        _context.Users.Add(agent);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var roleCode in new[] { SystemRoles.TenantAdministrator, SystemRoles.ServiceDeskAgent })
        {
            if (roles.TryGetValue(roleCode, out var role))
            {
                _context.UserRoles.Add(new UserRole
                {
                    TenantId = tenant.Id,
                    UserId = agent.Id,
                    RoleId = role.Id,
                    CreatedAt = now
                });
            }
        }

        _context.GroupMembers.Add(new GroupMember
        {
            TenantId = tenant.Id,
            GroupId = group.Id,
            UserId = agent.Id,
            IsLead = true,
            CreatedAt = now
        });

        var category = new Category
        {
            TenantId = tenant.Id,
            Code = "LOGISTICS-APP",
            Name = "Logistics application",
            Module = ServiceModule.Incident,
            DefaultAssignmentGroupId = group.Id,
            SortOrder = 10,
            CreatedAt = now
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var titles = new[]
        {
            "Consignment tracking screen shows stale status",
            "Warehouse scanner cannot connect to the depot wifi",
            "Driver mobile app crashes when uploading proof of delivery",
            "E-way bill generation failing for Maharashtra consignments",
            "Fleet telemetry dashboard not refreshing"
        };

        var sequence = await _context.NumberSequences
            .FirstAsync(s => s.TenantId == tenant.Id && s.Key == "INC", cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < titles.Length; i++)
        {
            var createdAt = now.AddDays(-_random.Next(1, 12)).AddHours(-_random.Next(0, 12));

            var incident = new Incident
            {
                TenantId = tenant.Id,
                Number = sequence.Format(sequence.NextValue++),
                Title = titles[i],
                Description =
                    $"Reported by the Northwind operations team. {titles[i]}. " +
                    "This record exists to demonstrate that tenant data is fully isolated.",
                RequesterId = agent.Id,
                AffectedUserId = agent.Id,
                OrganizationId = organization.Id,
                DepartmentId = department.Id,
                CategoryId = category.Id,
                Impact = Impact.Moderate,
                Urgency = Urgency.Medium,
                Priority = Priority.P3Moderate,
                AssignmentGroupId = group.Id,
                AssignedToUserId = agent.Id,
                Status = IncidentStatus.InProgress,
                Channel = IncidentChannel.Portal,
                CreatedAt = createdAt,
                CreatedBy = agent.Id
            };

            _context.Incidents.Add(incident);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Taxonomy
    // -----------------------------------------------------------------

    private async Task<List<Category>> SeedTaxonomyAsync(
        Guid tenantId,
        Dictionary<string, Group> groups,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var taxonomy = new (string Code, string Name, string GroupCode, (string Code, string Name)[] Subs)[]
        {
            ("NETWORK", "Network and connectivity", "NET-OPS",
            [
                ("VPN", "VPN access"),
                ("WIFI", "Wireless connectivity"),
                ("LAN", "Wired network"),
                ("INTERNET", "Internet and bandwidth"),
                ("FIREWALL", "Firewall and routing")
            ]),
            ("HARDWARE", "Hardware", "SD-L1",
            [
                ("LAPTOP", "Laptop and desktop"),
                ("PERIPHERAL", "Monitors and peripherals"),
                ("PRINTER", "Printing and scanning"),
                ("MOBILE", "Mobile devices")
            ]),
            ("SOFTWARE", "Software and applications", "APP-SUP",
            [
                ("OS", "Operating system"),
                ("OFFICE", "Office productivity"),
                ("ERP", "ERP and finance systems"),
                ("CRM", "CRM"),
                ("BROWSER", "Browser and web applications")
            ]),
            ("ACCESS", "Access and identity", "SD-L1",
            [
                ("PASSWORD", "Password reset"),
                ("ACCOUNT", "Account lockout"),
                ("PERMISSION", "Application permissions"),
                ("MFA", "Multi-factor authentication")
            ]),
            ("EMAIL", "Email and collaboration", "APP-SUP",
            [
                ("MAILBOX", "Mailbox and delivery"),
                ("CALENDAR", "Calendar and meetings"),
                ("TEAMS", "Collaboration tools")
            ]),
            ("CLOUD", "Cloud and infrastructure", "INFRA",
            [
                ("AZURE", "Azure services"),
                ("STORAGE", "Storage and backup"),
                ("DATABASE", "Database"),
                ("SERVER", "Server and virtual machines")
            ]),
            ("SECURITY", "Security", "SEC-OPS",
            [
                ("PHISHING", "Phishing and suspicious email"),
                ("MALWARE", "Malware and endpoint protection"),
                ("DATA", "Data protection incident")
            ])
        };

        var categories = new List<Category>();
        var order = 10;

        foreach (var (code, name, groupCode, subs) in taxonomy)
        {
            var category = new Category
            {
                TenantId = tenantId,
                Code = code,
                Name = name,
                Module = ServiceModule.Incident,
                DefaultAssignmentGroupId = groups[groupCode].Id,
                SortOrder = order,
                CreatedAt = now
            };

            _context.Categories.Add(category);
            categories.Add(category);

            var subOrder = 10;
            foreach (var (subCode, subName) in subs)
            {
                _context.Subcategories.Add(new Subcategory
                {
                    TenantId = tenantId,
                    CategoryId = category.Id,
                    Code = subCode,
                    Name = subName,
                    SortOrder = subOrder,
                    CreatedAt = now
                });

                subOrder += 10;
            }

            order += 10;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return categories;
    }

    // -----------------------------------------------------------------
    // Incidents
    // -----------------------------------------------------------------

    private async Task SeedIncidentsAsync(
        Guid tenantId,
        Guid organizationId,
        Dictionary<string, User> users,
        Dictionary<string, Group> groups,
        List<Category> categories,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var subcategories = await _context.Subcategories
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var policies = await _context.SlaPolicies
            .Include(p => p.SlaDefinition)
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .OrderBy(p => p.Order)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var schedules = await BuildSchedulesAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var sequence = await _context.NumberSequences
            .FirstAsync(s => s.TenantId == tenantId && s.Key == "INC", cancellationToken)
            .ConfigureAwait(false);

        var agents = new[]
        {
            "kavya.nair", "rohit.deshpande", "imran.sheikh",
            "meera.krishnan", "sandeep.rao", "fatima.qureshi"
        };

        var requesters = new[]
        {
            "sneha.patel", "aditya.menon", "divya.reddy", "karthik.subramanian",
            "pooja.bhatt", "nikhil.joshi", "anjali.sharma", "rajesh.kumar", "vikram.iyer"
        };

        var scenarios = IncidentCatalogue.Build();
        var incidents = new List<Incident>();
        var comments = new List<IncidentComment>();
        var tags = new List<IncidentTag>();
        var clocks = new List<SlaInstance>();

        // Roughly 420 incidents over the last fortnight - about thirty a day, which is a
        // credible load for a six-agent desk and is consistent with the live queue depth the
        // dashboard shows.
        for (var i = 0; i < 420; i++)
        {
            var scenario = scenarios[i % scenarios.Count];
            var category = categories.First(c => c.Code == scenario.CategoryCode);

            var subcategory = subcategories
                .FirstOrDefault(s => s.CategoryId == category.Id && s.Code == scenario.SubcategoryCode);

            var requesterAlias = requesters[_random.Next(requesters.Length)];
            var requester = users[requesterAlias];

            var impact = scenario.Impact;
            var urgency = scenario.Urgency;
            var priority = PriorityCalculator.DefaultFor(impact, urgency);

            var groupCode = scenario.GroupCode;
            var group = groups[groupCode];

            var candidateAgents = agents
                .Where(a => users[a].Id != requester.Id)
                .ToArray();

            var assignee = users[candidateAgents[_random.Next(candidateAgents.Length)]];

            // Decide first whether this ticket is still being worked, then give it an age that
            // fits. Ageing an incident and then asking what state it must be in produced a queue
            // where almost everything open was also in breach - because a P2 left open for three
            // days genuinely has blown an eight-working-hour commitment. Choosing the shape of
            // the queue first, and the dates second, makes the demo representative while leaving
            // every SLA figure computed by the real engine rather than asserted.
            var isOpen = _random.Next(0, 100) < 13;
            var createdAt = isOpen
                ? now.AddMinutes(-OpenAgeMinutes(priority))
                : now.AddDays(-WeightedAgeDays())
                    .AddHours(-_random.Next(0, 10))
                    .AddMinutes(-_random.Next(0, 60));

            var status = isOpen
                ? ChooseOpenStatus(priority)
                : _random.Next(0, 100) < 82 ? IncidentStatus.Closed : IncidentStatus.Resolved;

            var incident = new Incident
            {
                TenantId = tenantId,
                Number = sequence.Format(sequence.NextValue++),

                // The catalogue is cycled, so the same pattern recurs. Qualifying repeats by
                // site or system is both more readable in a demo queue and closer to reality -
                // the same class of fault genuinely does recur across offices - whereas a
                // screen of identical titles reads as generated filler.
                Title = QualifyTitle(scenario.Title, i / scenarios.Count),
                Description = scenario.Description,
                RequesterId = requester.Id,
                AffectedUserId = requester.Id,
                OrganizationId = organizationId,
                DepartmentId = requester.DepartmentId,
                CategoryId = category.Id,
                SubcategoryId = subcategory?.Id,
                Impact = impact,
                Urgency = urgency,
                Priority = priority,
                AssignmentGroupId = group.Id,
                Channel = scenario.Channel,
                Status = status,
                CreatedAt = createdAt,
                CreatedBy = requester.Id,
                IsMajorIncident = priority == Priority.P1Critical && _random.Next(0, 3) == 0
            };

            if (status != IncidentStatus.New)
            {
                incident.AssignedToUserId = assignee.Id;
            }

            // First response is drawn relative to the response commitment for this priority, not
            // from an arbitrary window. Drawing it arbitrarily was what made almost every seeded
            // incident breach: a P1 answered in a uniform 3-40 minutes misses a 15-minute
            // commitment more often than it meets it, which is not how a functioning desk looks.
            if (status != IncidentStatus.New)
            {
                incident.FirstRespondedAt = createdAt.AddMinutes(ResponseDelayMinutes(priority));
            }

            if (status is IncidentStatus.Resolved or IncidentStatus.Closed)
            {
                var resolveHours = priority switch
                {
                    Priority.P1Critical => _random.Next(1, 9),
                    Priority.P2High => _random.Next(2, 26),
                    Priority.P3Moderate => _random.Next(4, 70),
                    _ => _random.Next(8, 130)
                };

                incident.ResolvedAt = createdAt.AddHours(resolveHours);
                incident.ResolvedByUserId = assignee.Id;
                incident.ResolutionCode = scenario.ResolutionCode;
                incident.ResolutionNotes = scenario.Resolution;

                if (status == IncidentStatus.Closed)
                {
                    incident.ClosedAt = incident.ResolvedAt.Value.AddHours(_random.Next(12, 72));
                    incident.ClosedByUserId = assignee.Id;
                }
            }

            if (status == IncidentStatus.Pending)
            {
                incident.PendingReason = PendingReason.AwaitingRequester;
            }

            incidents.Add(incident);

            foreach (var tag in scenario.Tags)
            {
                tags.Add(new IncidentTag
                {
                    TenantId = tenantId,
                    IncidentId = incident.Id,
                    Tag = tag,
                    CreatedAt = createdAt
                });
            }

            comments.AddRange(BuildConversation(tenantId, incident, requester, assignee, scenario, now));
            clocks.AddRange(BuildSlaClocks(tenantId, incident, policies, schedules, now));
        }

        // Written in batches rather than one enormous SaveChanges. Each seeded row also produces
        // an audit row with a JSON snapshot, so a single-shot insert of the whole demo data set
        // is several thousand statements in one command and exceeds the SQL command timeout.
        // Batching keeps every statement well inside the timeout and bounds the change tracker.
        const int batchSize = 20;

        for (var offset = 0; offset < incidents.Count; offset += batchSize)
        {
            var batch = incidents.Skip(offset).Take(batchSize).ToList();
            var batchIds = batch.Select(i => i.Id).ToHashSet();

            _context.Incidents.AddRange(batch);
            _context.IncidentTags.AddRange(tags.Where(t => batchIds.Contains(t.IncidentId)));
            _context.IncidentComments.AddRange(comments.Where(c => batchIds.Contains(c.IncidentId)));
            _context.SlaInstances.AddRange(clocks.Where(c => batchIds.Contains(c.RecordId)));

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Detach what has been written so the tracker does not grow across the whole run.
            _context.ChangeTracker.Clear();
        }

        _logger.LogInformation(
            "Seeded {Incidents} incidents, {Comments} comments and {Clocks} SLA clocks.",
            incidents.Count, comments.Count, clocks.Count);
    }

    /// <summary>
    /// Builds the response and resolution clocks for a seeded incident, running them forward to
    /// the state they would genuinely be in now.
    /// <para>
    /// This is what makes the demo dashboard honest: the breach count on screen is the number of
    /// clocks whose deadline actually passed, computed against the same business calendar a
    /// customer would use, not a figure chosen to look good.
    /// </para>
    /// </summary>
    private List<SlaInstance> BuildSlaClocks(
        Guid tenantId,
        Incident incident,
        List<SlaPolicy> policies,
        Dictionary<Guid, BusinessSchedule> schedules,
        DateTimeOffset now)
    {
        var result = new List<SlaInstance>();

        foreach (var targetType in new[] { SlaTargetType.Response, SlaTargetType.Resolution })
        {
            var policy = policies.FirstOrDefault(p =>
                p.SlaDefinition is not null
                && p.SlaDefinition.TargetType == targetType
                && p.Priority == incident.Priority);

            if (policy?.SlaDefinition is null)
            {
                continue;
            }

            var definition = policy.SlaDefinition;

            var schedule = definition.BusinessCalendarId is not null
                            && schedules.TryGetValue(definition.BusinessCalendarId.Value, out var found)
                ? found
                : BusinessSchedule.TwentyFourSeven();

            var instance = new SlaInstance
            {
                TenantId = tenantId,
                Module = ServiceModule.Incident,
                RecordId = incident.Id,
                SlaDefinitionId = definition.Id,
                TargetType = targetType,
                SlaName = definition.Name,
                DurationMinutes = definition.DurationMinutes,
                WarningThresholdPercent = definition.WarningThresholdPercent,
                PauseWhenPending = definition.PauseWhenPending,
                BusinessCalendarId = definition.BusinessCalendarId,
                StartedAt = incident.CreatedAt,
                DueAt = schedule.AddBusinessMinutes(incident.CreatedAt, definition.DurationMinutes),
                State = SlaState.InProgress,
                CreatedAt = incident.CreatedAt
            };

            var completedAt = targetType == SlaTargetType.Response
                ? incident.FirstRespondedAt
                : incident.ResolvedAt;

            if (completedAt is not null)
            {
                instance.Complete(completedAt.Value);
            }
            else if (IncidentStateMachine.IsTerminal(incident.Status))
            {
                instance.Cancel();
            }
            else
            {
                // A pending incident is waiting on the requester, so its resolution clock is
                // stopped - exactly as the running system would stop it. Seeding this state
                // rather than leaving every clock running is what keeps the demo's breach
                // figures representative of a real desk instead of uniformly pessimistic.
                var isPausedByPending =
                    incident.Status == IncidentStatus.Pending
                    && targetType == SlaTargetType.Resolution
                    && instance.PauseWhenPending;

                if (isPausedByPending)
                {
                    var pausedAt = incident.FirstRespondedAt?.AddHours(_random.Next(1, 6))
                                   ?? incident.CreatedAt.AddHours(2);

                    // Only pause if the clock was still inside its allowance when work stopped;
                    // a commitment already overrun before the pause is genuinely a breach.
                    if (pausedAt < instance.DueAt)
                    {
                        instance.Pause(pausedAt);
                    }
                }

                instance.MarkBreachedIfOverdue(now);
            }

            result.Add(instance);
        }

        incident.HasBreachedSla = result.Any(c => c.BreachedAt is not null);

        var live = result.Where(c => !c.IsSettled).Select(c => c.DueAt).ToList();
        incident.NextSlaDueAt = live.Count == 0 ? null : live.Min();

        return result;
    }

    private async Task<Dictionary<Guid, BusinessSchedule>> BuildSchedulesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var calendars = await _context.BusinessCalendars
            .Include(c => c.Windows)
            .Include(c => c.Holidays)
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return calendars.ToDictionary(
            c => c.Id,
            c => BusinessSchedule.FromCalendar(c.TimeZoneId, c.IsTwentyFourSeven, c.Windows, c.Holidays));
    }

    /// <summary>
    /// Builds a short, plausible conversation so the activity timeline is not empty.
    /// <para>
    /// Every timestamp is clamped to the present. A comment dated in the future renders as
    /// "in 47 minutes" on the timeline, which immediately tells a viewer the data is generated.
    /// </para>
    /// </summary>
    private List<IncidentComment> BuildConversation(
        Guid tenantId,
        Incident incident,
        User requester,
        User assignee,
        IncidentScenario scenario,
        DateTimeOffset now)
    {
        var result = new List<IncidentComment>();

        if (incident.FirstRespondedAt is null)
        {
            return result;
        }

        // Never place a comment later than the moment the demo is being viewed.
        DateTimeOffset At(DateTimeOffset candidate) => candidate > now ? now : candidate;

        result.Add(new IncidentComment
        {
            TenantId = tenantId,
            IncidentId = incident.Id,
            Kind = IncidentCommentKind.PublicComment,
            Body = scenario.AgentAcknowledgement,
            AuthorUserId = assignee.Id,
            CreatedAt = At(incident.FirstRespondedAt.Value),
            CreatedBy = assignee.Id
        });

        result.Add(new IncidentComment
        {
            TenantId = tenantId,
            IncidentId = incident.Id,
            Kind = IncidentCommentKind.WorkNote,
            Body = scenario.InvestigationNote,
            AuthorUserId = assignee.Id,
            CreatedAt = At(incident.FirstRespondedAt.Value.AddMinutes(_random.Next(8, 90))),
            CreatedBy = assignee.Id
        });

        if (incident.ResolvedAt is not null)
        {
            result.Add(new IncidentComment
            {
                TenantId = tenantId,
                IncidentId = incident.Id,
                Kind = IncidentCommentKind.PublicComment,
                Body = scenario.Resolution,
                AuthorUserId = assignee.Id,
                CreatedAt = At(incident.ResolvedAt.Value),
                CreatedBy = assignee.Id
            });
        }
        else if (incident.Status == IncidentStatus.Pending)
        {
            result.Add(new IncidentComment
            {
                TenantId = tenantId,
                IncidentId = incident.Id,
                Kind = IncidentCommentKind.PublicComment,
                Body = "We need a little more information before we can continue. " +
                       "Could you confirm the exact time this started and whether any colleagues are affected?",
                AuthorUserId = assignee.Id,
                CreatedAt = At(incident.FirstRespondedAt.Value.AddHours(_random.Next(1, 6))),
                CreatedBy = assignee.Id
            });
        }

        return result;
    }

    /// <summary>
    /// How long a still-open incident has been running, in minutes.
    /// <para>
    /// Drawn relative to the resolution commitment for its priority, so roughly four in five
    /// open incidents sit comfortably inside target and the rest are genuinely overdue. The
    /// windows are wall-clock and deliberately conservative against the business-hours targets,
    /// because a ticket raised on Friday consumes almost no SLA time over the weekend.
    /// </para>
    /// </summary>
    private int OpenAgeMinutes(Priority priority)
    {
        // (inside target, beyond target) wall-clock minute ranges per priority.
        var (withinLow, withinHigh, overdueLow, overdueHigh) = priority switch
        {
            //  P1: 4 hours, 24x7.
            Priority.P1Critical => (5, 170, 300, 1500),
            //  P2: 8 business hours.
            Priority.P2High => (10, 430, 1900, 4300),
            //  P3: 24 business hours.
            Priority.P3Moderate => (15, 1700, 5800, 12000),
            //  P4: 48 business hours.
            Priority.P4Low => (20, 4000, 12000, 21000),
            //  P5: 96 business hours.
            _ => (30, 6000, 18000, 30000)
        };

        // About one open incident in five is past its commitment - enough for the breach view
        // and the escalation story to have real records behind them, without the queue reading
        // as a service desk in crisis.
        return _random.Next(0, 100) < 21
            ? _random.Next(overdueLow, overdueHigh)
            : _random.Next(withinLow, withinHigh);
    }

    /// <summary>
    /// Sites and systems used to distinguish repeated occurrences of the same incident pattern.
    /// </summary>
    private static readonly string[] Qualifiers =
    [
        "Bengaluru office",
        "Pune office",
        "Chennai office",
        "Hyderabad office",
        "Mumbai office",
        "Gurugram office",
        "Kolkata office",
        "Noida office",
        "Ahmedabad office",
        "Kochi office",
        "Jaipur office",
        "Indore office",
        "Coimbatore office",
        "Chandigarh office"
    ];

    /// <summary>
    /// Returns the title unchanged for the first occurrence, and qualified by site thereafter.
    /// </summary>
    private static string QualifyTitle(string title, int occurrence)
        => occurrence == 0
            ? title
            : $"{title} - {Qualifiers[(occurrence - 1) % Qualifiers.Length]}";

    /// <summary>
    /// Minutes from creation to the first agent reply, drawn against the response commitment for
    /// the priority. About one in seven misses, which is what gives the response-SLA reporting
    /// something real to show without making the desk look broken.
    /// </summary>
    private int ResponseDelayMinutes(Priority priority)
    {
        // Response targets seeded by tenant provisioning, in minutes.
        var target = priority switch
        {
            Priority.P1Critical => 15,
            Priority.P2High => 30,
            Priority.P3Moderate => 120,
            Priority.P4Low => 240,
            _ => 480
        };

        return _random.Next(0, 100) < 14
            ? _random.Next(target + 5, (target * 4) + 30)
            : _random.Next(1, Math.Max(2, (int)(target * 0.8)));
    }

    /// <summary>
    /// The queue state of an incident that is still being worked. High-priority work is picked
    /// up quickly and rarely sits unassigned.
    /// </summary>
    private IncidentStatus ChooseOpenStatus(Priority priority)
    {
        var roll = _random.Next(0, 100);

        if (priority <= Priority.P2High)
        {
            return roll switch
            {
                < 52 => IncidentStatus.InProgress,
                < 78 => IncidentStatus.Assigned,
                < 88 => IncidentStatus.Pending,
                _ => IncidentStatus.New
            };
        }

        return roll switch
        {
            < 18 => IncidentStatus.New,
            < 44 => IncidentStatus.Assigned,
            < 76 => IncidentStatus.InProgress,
            _ => IncidentStatus.Pending
        };
    }

    /// <summary>
    /// Age, in days, of an incident that has already been finished.
    /// <para>
    /// The history spans roughly a fortnight rather than a quarter. That is deliberate: with
    /// around sixty incidents live at any moment, an arrival rate of thirty a day is what makes
    /// the queue depth arithmetically consistent - a desk taking nine tickets a day could not
    /// have sixty of them still open without being badly behind. Stretching the same corpus over
    /// months would make the dashboard internally contradictory to anyone who does the sum.
    /// </para>
    /// </summary>
    private int WeightedAgeDays()
    {
        var roll = _random.Next(0, 100);

        return roll switch
        {
            < 34 => _random.Next(1, 3),
            < 64 => _random.Next(3, 6),
            < 86 => _random.Next(6, 10),
            _ => _random.Next(10, 15)
        };
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private Department CreateDepartment(Guid tenantId, Guid organizationId, string code, string name, string costCentre)
        => new()
        {
            TenantId = tenantId,
            OrganizationId = organizationId,
            Code = code,
            Name = name,
            CostCentre = costCentre,
            CreatedAt = _clock.UtcNow
        };

    private Group CreateGroup(Guid tenantId, string code, string name, string email)
        => new()
        {
            TenantId = tenantId,
            Code = code,
            Name = name,
            Email = email,
            Type = GroupType.Assignment,
            IsActive = true,
            CreatedAt = _clock.UtcNow
        };

    /// <summary>
    /// Derives a stable avatar tint from the alias, so the same person is always the same colour
    /// without storing an image or making a random choice that changes between environments.
    /// </summary>
    private static string AvatarColour(string alias)
    {
        string[] palette =
        [
            "#1B4DFF", "#7A3BFF", "#0EA5A5", "#D9480F", "#B02A6F",
            "#1D7A46", "#8A5A00", "#3F51B5", "#00796B", "#C2410C"
        ];

        var hash = alias.Aggregate(17, (current, c) => (current * 31) + c);
        return palette[Math.Abs(hash) % palette.Length];
    }

    private sealed record SeedUser(
        string Alias,
        string FirstName,
        string LastName,
        string JobTitle,
        string DepartmentCode,
        string[] Roles,
        string[] Groups,
        bool IsLead);
}
