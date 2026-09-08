using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProblemManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "problem");

            migrationBuilder.CreateTable(
                name: "Problems",
                schema: "problem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubcategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    AssignmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InvestigationStartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    KnownErrorAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RootCause = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    RootCauseConfidence = table.Column<int>(type: "int", nullable: true),
                    Workaround = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    PermanentFix = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfigurationItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LinkedIncidentCount = table.Column<int>(type: "int", nullable: false),
                    IsMajorProblem = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_Problems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Problems_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "servicedesk",
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Problems_Groups_AssignmentGroupId",
                        column: x => x.AssignmentGroupId,
                        principalSchema: "identity",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Problems_Subcategories_SubcategoryId",
                        column: x => x.SubcategoryId,
                        principalSchema: "servicedesk",
                        principalTable: "Subcategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Problems_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Problems_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProblemComments",
                schema: "problem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProblemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_ProblemComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemComments_Problems_ProblemId",
                        column: x => x.ProblemId,
                        principalSchema: "problem",
                        principalTable: "Problems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProblemComments_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemComments_AuthorId",
                schema: "problem",
                table: "ProblemComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemComments_Problem_Kind_Created",
                schema: "problem",
                table: "ProblemComments",
                columns: new[] { "ProblemId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Problems_AssignedToUserId",
                schema: "problem",
                table: "Problems",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_AssignmentGroupId",
                schema: "problem",
                table: "Problems",
                column: "AssignmentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_CategoryId",
                schema: "problem",
                table: "Problems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_OwnerUserId",
                schema: "problem",
                table: "Problems",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_SubcategoryId",
                schema: "problem",
                table: "Problems",
                column: "SubcategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Problems_Tenant_Assignee_Status",
                schema: "problem",
                table: "Problems",
                columns: new[] { "TenantId", "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Problems_Tenant_LinkedIncidents",
                schema: "problem",
                table: "Problems",
                columns: new[] { "TenantId", "LinkedIncidentCount" });

            migrationBuilder.CreateIndex(
                name: "IX_Problems_Tenant_Owner_Status",
                schema: "problem",
                table: "Problems",
                columns: new[] { "TenantId", "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Problems_Tenant_Status_Category",
                schema: "problem",
                table: "Problems",
                columns: new[] { "TenantId", "Status", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Problems_Tenant_Status_Priority_Created",
                schema: "problem",
                table: "Problems",
                columns: new[] { "TenantId", "Status", "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Problems_Tenant_Number",
                schema: "problem",
                table: "Problems",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProblemComments",
                schema: "problem");

            migrationBuilder.DropTable(
                name: "Problems",
                schema: "problem");
        }
    }
}
