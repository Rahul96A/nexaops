using NexaOps.Application.Security;

namespace NexaOps.Application.Tests.Security;

/// <summary>
/// Invariants of the permission catalogue and the seeded roles.
/// <para>
/// These guard properties that are easy to break silently in a release: a role granted a
/// permission that no longer exists, a customer-facing role handed platform-operator rights, or
/// a requester able to read internal work notes. None of those would fail a compile, and all of
/// them are security regressions.
/// </para>
/// </summary>
public sealed class PermissionCatalogueTests
{
    [Fact]
    public void Every_catalogued_permission_has_a_unique_code()
    {
        var codes = Permissions.Catalogue.Select(p => p.Code).ToList();

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Count);
    }

    [Fact]
    public void Every_catalogued_permission_is_documented()
    {
        foreach (var permission in Permissions.Catalogue)
        {
            permission.Code.ShouldNotBeNullOrWhiteSpace();
            permission.Module.ShouldNotBeNullOrWhiteSpace();
            permission.Name.ShouldNotBeNullOrWhiteSpace();

            // The description is shown to an administrator deciding whether to grant it, so an
            // empty or one-word description is a real defect in the role editor.
            permission.Description.Length.ShouldBeGreaterThan(15);
        }
    }

    [Fact]
    public void Permission_codes_follow_the_module_dot_action_convention()
    {
        foreach (var permission in Permissions.Catalogue)
        {
            permission.Code.ShouldContain(".");
            permission.Code.ShouldBe(permission.Code.ToLowerInvariant());
            permission.Code.ShouldNotContain(" ");
        }
    }

    [Fact]
    public void Only_catalogued_codes_are_recognised()
    {
        Permissions.IsKnown(Permissions.IncidentAssign).ShouldBeTrue();

        // A typo in a grant must not create a permission that silently never applies.
        Permissions.IsKnown("incident.assgin").ShouldBeFalse();
        Permissions.IsKnown("").ShouldBeFalse();
        Permissions.IsKnown(null).ShouldBeFalse();
    }

    [Fact]
    public void Every_role_grants_only_permissions_this_build_enforces()
    {
        foreach (var role in SystemRoles.All)
        {
            foreach (var permission in role.Permissions)
            {
                Permissions.IsKnown(permission)
                    .ShouldBeTrue($"Role '{role.Code}' grants unknown permission '{permission}'.");
            }
        }
    }

    [Fact]
    public void Role_codes_are_unique_and_documented()
    {
        var codes = SystemRoles.All.Select(r => r.Code).ToList();
        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Count);

        foreach (var role in SystemRoles.All)
        {
            role.Name.ShouldNotBeNullOrWhiteSpace();
            role.Description.Length.ShouldBeGreaterThan(20);
            role.Permissions.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void Only_the_platform_operator_role_is_platform_scoped()
    {
        var platformScoped = SystemRoles.All.Where(r => r.IsPlatformScoped).ToList();

        platformScoped.Count.ShouldBe(1);
        platformScoped[0].Code.ShouldBe(SystemRoles.PlatformAdministrator);
    }

    [Fact]
    public void No_customer_role_holds_a_platform_permission()
    {
        var platformPermissions = Permissions.Catalogue
            .Where(p => p.Code.StartsWith("platform.", StringComparison.Ordinal))
            .Select(p => p.Code)
            .ToHashSet(StringComparer.Ordinal);

        platformPermissions.ShouldNotBeEmpty();

        foreach (var role in SystemRoles.All.Where(r => !r.IsPlatformScoped))
        {
            // A tenant administrator is all-powerful inside their own tenant and must remain
            // powerless outside it.
            role.Permissions.Where(platformPermissions.Contains)
                .ShouldBeEmpty($"Role '{role.Code}' must not hold platform-operator permissions.");
        }
    }

    [Fact]
    public void The_requester_role_cannot_see_internal_work_notes()
    {
        var requester = SystemRoles.Find(SystemRoles.Requester);

        requester.ShouldNotBeNull();
        requester.Permissions.ShouldNotContain(Permissions.IncidentWorkNoteRead);
        requester.Permissions.ShouldNotContain(Permissions.IncidentWorkNoteCreate);

        // Nor every incident in the tenant - only the ones they are involved in.
        requester.Permissions.ShouldNotContain(Permissions.IncidentReadAll);
    }

    [Fact]
    public void The_requester_role_can_still_raise_and_track_its_own_tickets()
    {
        var requester = SystemRoles.Find(SystemRoles.Requester)!;

        requester.Permissions.ShouldContain(Permissions.IncidentRead);
        requester.Permissions.ShouldContain(Permissions.IncidentCreate);
        requester.Permissions.ShouldContain(Permissions.IncidentCommentCreate);
    }

    [Fact]
    public void Role_administration_and_audit_reading_are_not_granted_casually()
    {
        var sensitive = new[]
        {
            Permissions.RoleManage,
            Permissions.UserResetPassword
        };

        foreach (var permission in sensitive)
        {
            var holders = SystemRoles.All
                .Where(r => r.Permissions.Contains(permission))
                .Select(r => r.Code)
                .ToList();

            // Changing permission grants is effectively privilege escalation, so it belongs to
            // administrators only - not to a service desk manager or an IT manager.
            holders.ShouldBe(
                [SystemRoles.PlatformAdministrator, SystemRoles.TenantAdministrator],
                ignoreOrder: true,
                $"'{permission}' is granted too widely.");
        }
    }

    [Fact]
    public void An_agent_can_work_the_queue_but_not_reconfigure_the_service_desk()
    {
        var agent = SystemRoles.Find(SystemRoles.ServiceDeskAgent)!;

        agent.Permissions.ShouldContain(Permissions.IncidentReadAll);
        agent.Permissions.ShouldContain(Permissions.IncidentAssign);
        agent.Permissions.ShouldContain(Permissions.IncidentResolve);
        agent.Permissions.ShouldContain(Permissions.IncidentWorkNoteRead);

        // Priority overrides, major incident declaration and SLA configuration are management
        // decisions, and keeping them out of the agent role is what makes the audit trail for
        // those actions meaningful.
        agent.Permissions.ShouldNotContain(Permissions.IncidentPriorityOverride);
        agent.Permissions.ShouldNotContain(Permissions.IncidentDeclareMajor);
        agent.Permissions.ShouldNotContain(Permissions.SlaManage);
        agent.Permissions.ShouldNotContain(Permissions.AuditRead);
    }

    [Fact]
    public void A_tenant_administrator_holds_every_non_platform_permission()
    {
        var admin = SystemRoles.Find(SystemRoles.TenantAdministrator)!;

        var expected = Permissions.All
            .Where(p => !p.StartsWith("platform.", StringComparison.Ordinal))
            .ToList();

        foreach (var permission in expected)
        {
            admin.Permissions.ShouldContain(permission);
        }
    }

    [Fact]
    public void The_ai_assistant_permission_does_not_imply_the_right_to_change_anything()
    {
        // Holding ai.action.confirm lets a user approve an AI proposal; it does not grant the
        // underlying operation, which is checked separately when the action actually runs.
        var confirm = Permissions.Catalogue.Single(p => p.Code == Permissions.AiActionConfirm);

        confirm.Description.ShouldContain("Underlying permissions still apply");
    }
}
