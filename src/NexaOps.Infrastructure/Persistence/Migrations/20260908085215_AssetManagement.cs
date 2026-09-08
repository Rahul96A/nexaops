using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssetManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "asset");

            migrationBuilder.CreateTable(
                name: "Assets",
                schema: "asset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AssetTag = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Manufacturer = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PurchasedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchaseCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PurchaseOrderNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WarrantyExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UsefulLifeMonths = table.Column<int>(type: "int", nullable: true),
                    ConfigurationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisposalNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DisposedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArchivedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_ConfigurationItems_ConfigurationItemId",
                        column: x => x.ConfigurationItemId,
                        principalSchema: "cmdb",
                        principalTable: "ConfigurationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assets_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SoftwareLicences",
                schema: "asset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Publisher = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AgreementReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Model = table.Column<int>(type: "int", nullable: false),
                    EntitlementCount = table.Column<int>(type: "int", nullable: false),
                    DeployedCount = table.Column<int>(type: "int", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    AnnualCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArchivedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareLicences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetAssignments",
                schema: "asset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReturnedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AssignmentNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArchivedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Assets_AssetId",
                        column: x => x.AssetId,
                        principalSchema: "asset",
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_Asset_AssignedAt",
                schema: "asset",
                table: "AssetAssignments",
                columns: new[] { "AssetId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_Tenant_User_Returned",
                schema: "asset",
                table: "AssetAssignments",
                columns: new[] { "TenantId", "UserId", "ReturnedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetAssignments_UserId",
                schema: "asset",
                table: "AssetAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_AssignedToUserId",
                schema: "asset",
                table: "Assets",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_ConfigurationItemId",
                schema: "asset",
                table: "Assets",
                column: "ConfigurationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Tenant_Assignee",
                schema: "asset",
                table: "Assets",
                columns: new[] { "TenantId", "AssignedToUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Tenant_ConfigurationItem",
                schema: "asset",
                table: "Assets",
                columns: new[] { "TenantId", "ConfigurationItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Tenant_Status_Kind",
                schema: "asset",
                table: "Assets",
                columns: new[] { "TenantId", "Status", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Tenant_WarrantyExpires",
                schema: "asset",
                table: "Assets",
                columns: new[] { "TenantId", "WarrantyExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "UX_Assets_Tenant_Number",
                schema: "asset",
                table: "Assets",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Assets_Tenant_Tag",
                schema: "asset",
                table: "Assets",
                columns: new[] { "TenantId", "AssetTag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareLicences_Tenant_Expires",
                schema: "asset",
                table: "SoftwareLicences",
                columns: new[] { "TenantId", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareLicences_Tenant_Product",
                schema: "asset",
                table: "SoftwareLicences",
                columns: new[] { "TenantId", "ProductName" });

            migrationBuilder.CreateIndex(
                name: "UX_SoftwareLicences_Tenant_Number",
                schema: "asset",
                table: "SoftwareLicences",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetAssignments",
                schema: "asset");

            migrationBuilder.DropTable(
                name: "SoftwareLicences",
                schema: "asset");

            migrationBuilder.DropTable(
                name: "Assets",
                schema: "asset");
        }
    }
}
