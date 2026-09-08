using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexaOps.Domain.Identity;

namespace NexaOps.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.LegalName).HasMaxLength(256);
        builder.Property(x => x.PrimaryDomain).HasMaxLength(256);
        builder.Property(x => x.EntraTenantId).HasMaxLength(64);
        builder.Property(x => x.TimeZoneId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Locale).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.DateFormat).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DataRegion).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_Tenants_Code");
        builder.HasIndex(x => x.PrimaryDomain).HasDatabaseName("IX_Tenants_PrimaryDomain");
    }
}

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.LegalName).HasMaxLength(256);
        builder.Property(x => x.GstIdentificationNumber).HasMaxLength(15);
        builder.Property(x => x.PermanentAccountNumber).HasMaxLength(10);
        builder.Property(x => x.CorporateIdentityNumber).HasMaxLength(21);
        builder.Property(x => x.AddressLine1).HasMaxLength(256);
        builder.Property(x => x.AddressLine2).HasMaxLength(256);
        builder.Property(x => x.City).HasMaxLength(128);
        builder.Property(x => x.StateCode).HasMaxLength(4);
        builder.Property(x => x.PostalCode).HasMaxLength(12);
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.ContactEmail).HasMaxLength(256);
        builder.Property(x => x.ContactPhone).HasMaxLength(24);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Tenant)
            .WithMany(t => t.Organizations)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("UX_Organizations_Tenant_Code");
    }
}

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.CostCentre).HasMaxLength(64);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Organization)
            .WithMany(o => o.Departments)
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ParentDepartment)
            .WithMany(d => d.ChildDepartments)
            .HasForeignKey(x => x.ParentDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("UX_Departments_Tenant_Code");
        builder.HasIndex(x => x.OrganizationId).HasDatabaseName("IX_Departments_Organization");
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.FirstName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(24);
        builder.Property(x => x.EmployeeId).HasMaxLength(64);
        builder.Property(x => x.JobTitle).HasMaxLength(128);
        builder.Property(x => x.Location).HasMaxLength(128);
        builder.Property(x => x.TimeZoneId).HasMaxLength(128);
        builder.Property(x => x.Locale).HasMaxLength(16);
        builder.Property(x => x.AvatarColor).HasMaxLength(9);
        builder.Property(x => x.Status).HasConversion<int>();

        // Credential material. PasswordHash is long enough for PBKDF2 v3 output plus headroom
        // for a future algorithm change without a schema migration.
        builder.Property(x => x.PasswordHash).HasMaxLength(512);
        builder.Property(x => x.SecurityStamp).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalObjectId).HasMaxLength(128);
        builder.Property(x => x.ExternalIssuer).HasMaxLength(256);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Organization).WithMany()
            .HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Department).WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Manager).WithMany()
            .HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);

        // Email is the sign-in identifier and must be unique inside a tenant, but the same
        // person may legitimately exist in two tenants.
        builder.HasIndex(x => new { x.TenantId, x.Email }).IsUnique().HasDatabaseName("UX_Users_Tenant_Email");
        builder.HasIndex(x => x.Email).HasDatabaseName("IX_Users_Email");
        builder.HasIndex(x => x.ExternalObjectId).HasDatabaseName("IX_Users_ExternalObjectId");
        builder.HasIndex(x => new { x.TenantId, x.Status }).HasDatabaseName("IX_Users_Tenant_Status");
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("UX_Roles_Tenant_Code");
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PermissionCode).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.RoleId, x.PermissionCode })
            .IsUnique()
            .HasDatabaseName("UX_RolePermissions_Role_Permission");
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique().HasDatabaseName("UX_UserRoles_User_Role");
    }
}

public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("Groups", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1024);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Manager).WithMany()
            .HasForeignKey(x => x.ManagerUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("UX_Groups_Tenant_Code");
        builder.HasIndex(x => new { x.TenantId, x.Type }).HasDatabaseName("IX_Groups_Tenant_Type");
    }
}

public sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("GroupMembers", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(u => u.GroupMemberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique().HasDatabaseName("UX_GroupMembers_Group_User");
        builder.HasIndex(x => x.UserId).HasDatabaseName("IX_GroupMembers_User");
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", "identity");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
        builder.Property(x => x.RevokedReason).HasMaxLength(256);
        builder.Property(x => x.CreatedFromIp).HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        // Lookup on presentation is by hash, so this index is on the hot path of every refresh.
        builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        builder.HasIndex(x => new { x.UserId, x.SessionId }).HasDatabaseName("IX_RefreshTokens_User_Session");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("IX_RefreshTokens_ExpiresAt");
    }
}
