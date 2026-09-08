using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexaOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequestsCatalogueApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SlaInstances_Incidents_RecordId",
                schema: "sla",
                table: "SlaInstances");

            migrationBuilder.DropIndex(
                name: "IX_SlaInstances_RecordId",
                schema: "sla",
                table: "SlaInstances");

            migrationBuilder.EnsureSchema(
                name: "approval");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "request");

            migrationBuilder.CreateTable(
                name: "Approvals",
                schema: "approval",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Module = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Rule = table.Column<int>(type: "int", nullable: false),
                    TargetKind = table.Column<int>(type: "int", nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApproverGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecordLabel = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
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
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Approvals_Groups_ApproverGroupId",
                        column: x => x.ApproverGroupId,
                        principalSchema: "identity",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Approvals_Users_ApproverUserId",
                        column: x => x.ApproverUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CatalogItems",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShortDescription = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FulfilmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    ApprovalTargetKind = table.Column<int>(type: "int", nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApproverGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovalRule = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    EstimatedDeliveryDays = table.Column<int>(type: "int", nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    MaxQuantity = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogItems_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "servicedesk",
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CatalogItems_Groups_FulfilmentGroupId",
                        column: x => x.FulfilmentGroupId,
                        principalSchema: "identity",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRequests",
                schema: "request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedForId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    FulfilmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PendingReason = table.Column<int>(type: "int", nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FulfilledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequiredByDate = table.Column<DateOnly>(type: "date", nullable: true),
                    HasBreachedSla = table.Column<bool>(type: "bit", nullable: false),
                    NextSlaDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TotalCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
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
                    table.PrimaryKey("PK_ServiceRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRequests_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "servicedesk",
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceRequests_Groups_FulfilmentGroupId",
                        column: x => x.FulfilmentGroupId,
                        principalSchema: "identity",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceRequests_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceRequests_Users_RequestedForId",
                        column: x => x.RequestedForId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceRequests_Users_RequesterId",
                        column: x => x.RequesterId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CatalogItemVariables",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HelpText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChoicesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MaxLength = table.Column<int>(type: "int", nullable: true),
                    MinValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
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
                    table.PrimaryKey("PK_CatalogItemVariables", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogItemVariables_CatalogItems_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalSchema: "catalog",
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestComments",
                schema: "request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_RequestComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestComments_ServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalSchema: "request",
                        principalTable: "ServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestComments_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestItems",
                schema: "request",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VariableValuesJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    FulfilmentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FulfilledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FulfilledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FulfilmentNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestItems_CatalogItems_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalSchema: "catalog",
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestItems_Groups_FulfilmentGroupId",
                        column: x => x.FulfilmentGroupId,
                        principalSchema: "identity",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestItems_ServiceRequests_ServiceRequestId",
                        column: x => x.ServiceRequestId,
                        principalSchema: "request",
                        principalTable: "ServiceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestItems_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_ApproverGroupId",
                schema: "approval",
                table: "Approvals",
                column: "ApproverGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_ApproverUserId",
                schema: "approval",
                table: "Approvals",
                column: "ApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Tenant_Approver_State",
                schema: "approval",
                table: "Approvals",
                columns: new[] { "TenantId", "ApproverUserId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Tenant_ApproverGroup_State",
                schema: "approval",
                table: "Approvals",
                columns: new[] { "TenantId", "ApproverGroupId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_Tenant_Record_Stage",
                schema: "approval",
                table: "Approvals",
                columns: new[] { "TenantId", "Module", "RecordId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_CategoryId",
                schema: "catalog",
                table: "CatalogItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_FulfilmentGroupId",
                schema: "catalog",
                table: "CatalogItems",
                column: "FulfilmentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_Tenant_Status_Category_Sort",
                schema: "catalog",
                table: "CatalogItems",
                columns: new[] { "TenantId", "Status", "CategoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_CatalogItems_Tenant_Code",
                schema: "catalog",
                table: "CatalogItems",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CatalogItemVariables_Item_Key",
                schema: "catalog",
                table: "CatalogItemVariables",
                columns: new[] { "CatalogItemId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_AuthorId",
                schema: "request",
                table: "RequestComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestComments_Request_Kind_Created",
                schema: "request",
                table: "RequestComments",
                columns: new[] { "ServiceRequestId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_AssignedToUserId",
                schema: "request",
                table: "RequestItems",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_CatalogItemId",
                schema: "request",
                table: "RequestItems",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_FulfilmentGroupId",
                schema: "request",
                table: "RequestItems",
                column: "FulfilmentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_Request_Status",
                schema: "request",
                table: "RequestItems",
                columns: new[] { "ServiceRequestId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestItems_Tenant_CatalogItem",
                schema: "request",
                table: "RequestItems",
                columns: new[] { "TenantId", "CatalogItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_AssignedToUserId",
                schema: "request",
                table: "ServiceRequests",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_CategoryId",
                schema: "request",
                table: "ServiceRequests",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_FulfilmentGroupId",
                schema: "request",
                table: "ServiceRequests",
                column: "FulfilmentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_RequestedForId",
                schema: "request",
                table: "ServiceRequests",
                column: "RequestedForId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_RequesterId",
                schema: "request",
                table: "ServiceRequests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Tenant_Assignee_Status",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Tenant_Breached_Status",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "HasBreachedSla", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Tenant_Group_Status",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "FulfilmentGroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Tenant_NextSlaDue",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "NextSlaDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Tenant_RequestedFor_Status",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "RequestedForId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Tenant_Requester_Status",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "RequesterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_Tenant_Status_Priority_Created",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "Status", "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ServiceRequests_Tenant_Number",
                schema: "request",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Approvals",
                schema: "approval");

            migrationBuilder.DropTable(
                name: "CatalogItemVariables",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "RequestComments",
                schema: "request");

            migrationBuilder.DropTable(
                name: "RequestItems",
                schema: "request");

            migrationBuilder.DropTable(
                name: "CatalogItems",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ServiceRequests",
                schema: "request");

            migrationBuilder.CreateIndex(
                name: "IX_SlaInstances_RecordId",
                schema: "sla",
                table: "SlaInstances",
                column: "RecordId");

            migrationBuilder.AddForeignKey(
                name: "FK_SlaInstances_Incidents_RecordId",
                schema: "sla",
                table: "SlaInstances",
                column: "RecordId",
                principalSchema: "servicedesk",
                principalTable: "Incidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
