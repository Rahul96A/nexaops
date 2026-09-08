using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "change");

            migrationBuilder.CreateTable(
                name: "Changes",
                schema: "change",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Risk = table.Column<int>(type: "int", nullable: false),
                    Impact = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ImplementationPlan = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    RollbackPlan = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    TestPlan = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ImpactAssessment = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    PlannedStartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlannedEndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequiresDowntime = table.Column<bool>(type: "bit", nullable: false),
                    AssignmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProblemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfigurationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_Changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Changes_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "servicedesk",
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Changes_Groups_AssignmentGroupId",
                        column: x => x.AssignmentGroupId,
                        principalSchema: "identity",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Changes_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Changes_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeComments",
                schema: "change",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
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
                    table.PrimaryKey("PK_ChangeComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeComments_Changes_ChangeId",
                        column: x => x.ChangeId,
                        principalSchema: "change",
                        principalTable: "Changes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeComments_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeComments_AuthorId",
                schema: "change",
                table: "ChangeComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeComments_Change_Kind_Created",
                schema: "change",
                table: "ChangeComments",
                columns: new[] { "ChangeId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Changes_AssignedToUserId",
                schema: "change",
                table: "Changes",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_AssignmentGroupId",
                schema: "change",
                table: "Changes",
                column: "AssignmentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_CategoryId",
                schema: "change",
                table: "Changes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_RequestedByUserId",
                schema: "change",
                table: "Changes",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_Tenant_Assignee_Status",
                schema: "change",
                table: "Changes",
                columns: new[] { "TenantId", "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Changes_Tenant_Problem",
                schema: "change",
                table: "Changes",
                columns: new[] { "TenantId", "ProblemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Changes_Tenant_Status_Type_PlannedStart",
                schema: "change",
                table: "Changes",
                columns: new[] { "TenantId", "Status", "Type", "PlannedStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Changes_Tenant_Window",
                schema: "change",
                table: "Changes",
                columns: new[] { "TenantId", "PlannedStartAt", "PlannedEndAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Changes_Tenant_Number",
                schema: "change",
                table: "Changes",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeComments",
                schema: "change");

            migrationBuilder.DropTable(
                name: "Changes",
                schema: "change");
        }
    }
}
