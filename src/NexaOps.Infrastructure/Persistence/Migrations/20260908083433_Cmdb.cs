using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cmdb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cmdb");

            migrationBuilder.CreateTable(
                name: "ConfigurationItems",
                schema: "cmdb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Criticality = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Manufacturer = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Environment = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupportGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcquiredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    SupportExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_ConfigurationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfigurationItems_Groups_SupportGroupId",
                        column: x => x.SupportGroupId,
                        principalSchema: "identity",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConfigurationItems_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CiRelationships",
                schema: "cmdb",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_CiRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CiRelationships_ConfigurationItems_SourceId",
                        column: x => x.SourceId,
                        principalSchema: "cmdb",
                        principalTable: "ConfigurationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CiRelationships_ConfigurationItems_TargetId",
                        column: x => x.TargetId,
                        principalSchema: "cmdb",
                        principalTable: "ConfigurationItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CiRelationships_TargetId",
                schema: "cmdb",
                table: "CiRelationships",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_CiRelationships_Tenant_Source",
                schema: "cmdb",
                table: "CiRelationships",
                columns: new[] { "TenantId", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CiRelationships_Tenant_Target",
                schema: "cmdb",
                table: "CiRelationships",
                columns: new[] { "TenantId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "UX_CiRelationships_Source_Target_Type",
                schema: "cmdb",
                table: "CiRelationships",
                columns: new[] { "SourceId", "TargetId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationItems_OwnerUserId",
                schema: "cmdb",
                table: "ConfigurationItems",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationItems_SupportGroupId",
                schema: "cmdb",
                table: "ConfigurationItems",
                column: "SupportGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationItems_Tenant_Criticality_Status",
                schema: "cmdb",
                table: "ConfigurationItems",
                columns: new[] { "TenantId", "Criticality", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationItems_Tenant_SupportExpires",
                schema: "cmdb",
                table: "ConfigurationItems",
                columns: new[] { "TenantId", "SupportExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationItems_Tenant_SupportGroup",
                schema: "cmdb",
                table: "ConfigurationItems",
                columns: new[] { "TenantId", "SupportGroupId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationItems_Tenant_Type_Status",
                schema: "cmdb",
                table: "ConfigurationItems",
                columns: new[] { "TenantId", "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ConfigurationItems_Tenant_Name",
                schema: "cmdb",
                table: "ConfigurationItems",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ConfigurationItems_Tenant_Number",
                schema: "cmdb",
                table: "ConfigurationItems",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CiRelationships",
                schema: "cmdb");

            migrationBuilder.DropTable(
                name: "ConfigurationItems",
                schema: "cmdb");
        }
    }
}
