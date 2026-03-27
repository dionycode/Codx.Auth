using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Codx.Auth.Data.Migrations.Users
{
    /// <inheritdoc />
    public partial class AddMultiTenantAuthV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Tenants",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Companies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResourceType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ResourceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnterpriseApplications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AllowSelfRegistration = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseApplications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InviteTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserMemberships_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserMemberships_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceRoleDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceRoleDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WorkspaceContextType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnterpriseApplicationRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(100)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnterpriseApplicationRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnterpriseApplicationRoles_EnterpriseApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "EnterpriseApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvitationRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvitationRoles_Invitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "Invitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvitationRoles_WorkspaceRoleDefinitions_RoleId",
                        column: x => x.RoleId,
                        principalTable: "WorkspaceRoleDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserMembershipRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMembershipRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMembershipRoles_UserMemberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "UserMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserMembershipRoles_WorkspaceRoleDefinitions_RoleId",
                        column: x => x.RoleId,
                        principalTable: "WorkspaceRoleDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserApplicationRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserApplicationRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserApplicationRoles_EnterpriseApplicationRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "EnterpriseApplicationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAt",
                table: "AuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_EventType",
                table: "AuditLogs",
                columns: new[] { "UserId", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_EnterpriseApplicationRoles_ApplicationId_Name",
                table: "EnterpriseApplicationRoles",
                columns: new[] { "ApplicationId", "Name" },
                unique: true,
                filter: "[ApplicationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationRoles_InvitationId_RoleId",
                table: "InvitationRoles",
                columns: new[] { "InvitationId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvitationRoles_RoleId",
                table: "InvitationRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_InviteTokenHash",
                table: "Invitations",
                column: "InviteTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserApplicationRoles_RoleId",
                table: "UserApplicationRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserApplicationRoles_UserId_TenantId_CompanyId_ApplicationId_RoleId",
                table: "UserApplicationRoles",
                columns: new[] { "UserId", "TenantId", "CompanyId", "ApplicationId", "RoleId" },
                unique: true,
                filter: "[ApplicationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserMembershipRoles_MembershipId_RoleId",
                table: "UserMembershipRoles",
                columns: new[] { "MembershipId", "RoleId" },
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_UserMembershipRoles_RoleId",
                table: "UserMembershipRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMemberships_CompanyId",
                table: "UserMemberships",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMemberships_TenantId",
                table: "UserMemberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMemberships_UserId_TenantId_CompanyId",
                table: "UserMemberships",
                columns: new[] { "UserId", "TenantId", "CompanyId" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceRoleDefinitions_Code",
                table: "WorkspaceRoleDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSessions_ExpiresAt",
                table: "WorkspaceSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceSessions_UserId_Status",
                table: "WorkspaceSessions",
                columns: new[] { "UserId", "Status" });

            // --- Seed WorkspaceRoleDefinitions ---
            migrationBuilder.InsertData(
                table: "WorkspaceRoleDefinitions",
                columns: new[] { "Id", "Code", "DisplayName", "ScopeType", "IsActive", "CreatedAt" },
                values: new object[,]
                {
                    { 1, "TENANT_OWNER",    "TenantOwner",    "Tenant",  true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, "TENANT_ADMIN",    "TenantAdmin",    "Tenant",  true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, "TENANT_MANAGER",  "TenantManager",  "Tenant",  true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, "COMPANY_ADMIN",   "CompanyAdmin",   "Company", true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, "COMPANY_MANAGER", "CompanyManager", "Company", true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                    { 6, "MEMBER",          "Member",         "Company", true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
                });

            // --- Populate Tenant.Status from legacy flags ---
            migrationBuilder.Sql("UPDATE Tenants SET Status = 'Cancelled' WHERE IsActive = 0 OR IsDeleted = 1;");
            migrationBuilder.Sql("UPDATE Tenants SET Status = 'Active' WHERE Status IS NULL;");

            // --- Populate Company.Status from legacy flags ---
            migrationBuilder.Sql("UPDATE Companies SET Status = 'Cancelled' WHERE IsActive = 0 OR IsDeleted = 1;");
            migrationBuilder.Sql("UPDATE Companies SET Status = 'Active' WHERE Status IS NULL;");

            // --- Migrate TenantManagers → UserMemberships (tenant-scoped) + UserMembershipRoles (TENANT_OWNER) ---
            migrationBuilder.Sql(@"
                INSERT INTO UserMemberships (Id, UserId, TenantId, CompanyId, Status, JoinedAt)
                SELECT NEWID(), tm.UserId, tm.TenantId, NULL, 'Active', GETUTCDATE()
                FROM TenantManagers tm
                WHERE NOT EXISTS (
                    SELECT 1 FROM UserMemberships um
                    WHERE um.UserId = tm.UserId AND um.TenantId = tm.TenantId AND um.CompanyId IS NULL
                );");

            migrationBuilder.Sql(@"
                INSERT INTO UserMembershipRoles (Id, MembershipId, RoleId, Status, AssignedAt, AssignedByUserId)
                SELECT NEWID(), um.Id, 1, 'Active', GETUTCDATE(), um.UserId
                FROM UserMemberships um
                INNER JOIN TenantManagers tm ON tm.UserId = um.UserId AND tm.TenantId = um.TenantId
                WHERE um.CompanyId IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM UserMembershipRoles umr
                      WHERE umr.MembershipId = um.Id AND umr.RoleId = 1 AND umr.Status = 'Active'
                  );");

            // --- Migrate UserCompanies → UserMemberships (company-scoped) + UserMembershipRoles (MEMBER) ---
            migrationBuilder.Sql(@"
                INSERT INTO UserMemberships (Id, UserId, TenantId, CompanyId, Status, JoinedAt)
                SELECT NEWID(), uc.UserId, c.TenantId, uc.CompanyId, 'Active', GETUTCDATE()
                FROM UserCompanies uc
                INNER JOIN Companies c ON c.Id = uc.CompanyId
                WHERE NOT EXISTS (
                    SELECT 1 FROM UserMemberships um
                    WHERE um.UserId = uc.UserId AND um.TenantId = c.TenantId AND um.CompanyId = uc.CompanyId
                );");

            migrationBuilder.Sql(@"
                INSERT INTO UserMembershipRoles (Id, MembershipId, RoleId, Status, AssignedAt, AssignedByUserId)
                SELECT NEWID(), um.Id, 6, 'Active', GETUTCDATE(), um.UserId
                FROM UserMemberships um
                INNER JOIN UserCompanies uc ON uc.UserId = um.UserId AND uc.CompanyId = um.CompanyId
                WHERE um.CompanyId IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM UserMembershipRoles umr
                      WHERE umr.MembershipId = um.Id AND umr.RoleId = 6 AND umr.Status = 'Active'
                  );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "InvitationRoles");

            migrationBuilder.DropTable(
                name: "UserApplicationRoles");

            migrationBuilder.DropTable(
                name: "UserMembershipRoles");

            migrationBuilder.DropTable(
                name: "WorkspaceSessions");

            migrationBuilder.DropTable(
                name: "Invitations");

            migrationBuilder.DropTable(
                name: "EnterpriseApplicationRoles");

            migrationBuilder.DropTable(
                name: "UserMemberships");

            migrationBuilder.DropTable(
                name: "WorkspaceRoleDefinitions");

            migrationBuilder.DropTable(
                name: "EnterpriseApplications");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Companies");
        }
    }
}
