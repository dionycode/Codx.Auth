using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System;

// Alias to disambiguate Enterprise ApplicationRole from AspNet ApplicationRole
using EnterpriseAppRole = Codx.Auth.Data.Entities.Enterprise.EnterpriseApplicationRole;
using EnterpriseApp = Codx.Auth.Data.Entities.Enterprise.EnterpriseApplication;

namespace Codx.Auth.Data.Contexts
{
    public class UserDbContext : IdentityDbContext
        <ApplicationUser, 
        Codx.Auth.Data.Entities.AspNet.ApplicationRole, 
        Guid,
        ApplicationUserClaim,
        ApplicationUserRole,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>
    {
        public UserDbContext(DbContextOptions<UserDbContext> options) : base(options)
        {

        }

        // --- Legacy tables (kept for migration compatibility) ---
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantManager> TenantManagers { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<UserCompany> UserCompanies { get; set; }
        public DbSet<TwoFactorCode> TwoFactorCodes { get; set; }

        // --- Multi-tenant v2 tables ---
        public DbSet<WorkspaceRoleDefinition> WorkspaceRoleDefinitions { get; set; }
        public DbSet<UserMembership> UserMemberships { get; set; }
        public DbSet<UserMembershipRole> UserMembershipRoles { get; set; }
        public DbSet<Invitation> Invitations { get; set; }
        public DbSet<InvitationRole> InvitationRoles { get; set; }
        public DbSet<EnterpriseApp> EnterpriseApplications { get; set; }
        public DbSet<EnterpriseAppRole> EnterpriseApplicationRoles { get; set; }
        public DbSet<UserApplicationRole> UserApplicationRoles { get; set; }
        public DbSet<WorkspaceSession> WorkspaceSessions { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<EmailTemplate> EmailTemplates { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(b => {
                b.HasMany(e => e.UserRoles)
                .WithOne(e => e.User)
                .HasForeignKey(ur => ur.UserId)
                .IsRequired();
            });

            builder.Entity<ApplicationUser>(b => {
                b.HasMany(e => e.UserClaims)
                .WithOne(e => e.User)
                .HasForeignKey(ur => ur.UserId)
                .IsRequired();
            });

            builder.Entity<ApplicationUser>(b => {
                b.Property(e => e.GivenName).HasMaxLength(100);
                b.Property(e => e.MiddleName).HasMaxLength(100);
                b.Property(e => e.FamilyName).HasMaxLength(100);
            });

            builder.Entity<Codx.Auth.Data.Entities.AspNet.ApplicationRole>(b => {
                b.HasMany(e => e.UserRoles)
                .WithOne(e => e.Role)
                .HasForeignKey(ur => ur.RoleId)
                .IsRequired();
            });

            builder.Entity<UserCompany>()
                .HasKey(uc => new { uc.UserId, uc.CompanyId });

            builder.Entity<UserCompany>()
                .HasOne(uc => uc.User)
                .WithMany(u => u.UserCompanies)
                .HasForeignKey(uc => uc.UserId);

            builder.Entity<UserCompany>()
                .HasOne(uc => uc.Company)
                .WithMany(c => c.UserCompanies)
                .HasForeignKey(uc => uc.CompanyId);

            builder.Entity<Tenant>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(100);
                entity.Property(e => e.Slug).HasMaxLength(100);
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.Email).HasMaxLength(100);
                entity.Property(e => e.Phone).HasMaxLength(15);
                entity.Property(e => e.Address).HasMaxLength(200);
                entity.Property(e => e.Logo).HasMaxLength(200);
                entity.Property(e => e.Theme).HasMaxLength(50);
                entity.Property(e => e.Description).HasMaxLength(500);
            });

            builder.Entity<TenantManager>()
                .HasKey(tm => new { tm.TenantId, tm.UserId });

            builder.Entity<TenantManager>()
                .HasOne(tm => tm.Manager)
                .WithMany(u => u.TenantManagers)
                .HasForeignKey(tm => tm.UserId);

            builder.Entity<TenantManager>()
                .HasOne(tm => tm.Tenant)
                .WithMany(t => t.TenantManagers)
                .HasForeignKey(tm => tm.TenantId);

            builder.Entity<Company>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(100);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.Email).HasMaxLength(100);
                entity.Property(e => e.Phone).HasMaxLength(15);
                entity.Property(e => e.Address).HasMaxLength(200);
                entity.Property(e => e.Logo).HasMaxLength(200);
                entity.Property(e => e.Theme).HasMaxLength(50);
                entity.Property(e => e.Description).HasMaxLength(500);
            });

            // Two-Factor Authentication Code entity configuration
            builder.Entity<TwoFactorCode>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).HasMaxLength(10).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
                entity.Property(e => e.IsUsed).HasDefaultValue(false);
                
                // Index for faster lookups
                entity.HasIndex(e => new { e.UserId, e.Code, e.ExpiresAt });
                
                // Foreign key relationship
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // --- WorkspaceRoleDefinition ---
            builder.Entity<WorkspaceRoleDefinition>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
                entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
                entity.Property(e => e.ScopeType).HasMaxLength(20).IsRequired();
                entity.HasIndex(e => e.Code).IsUnique();
            });

            // --- UserMembership ---
            builder.Entity<UserMembership>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
                entity.HasIndex(e => new { e.UserId, e.TenantId, e.CompanyId }).IsUnique();

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Tenant)
                    .WithMany(t => t.UserMemberships)
                    .HasForeignKey(e => e.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Company)
                    .WithMany(c => c.UserMemberships)
                    .HasForeignKey(e => e.CompanyId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // --- UserMembershipRole ---
            builder.Entity<UserMembershipRole>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();

                // Filtered unique index: only one active role assignment per (MembershipId, RoleId)
                entity.HasIndex(e => new { e.MembershipId, e.RoleId })
                    .HasFilter("[Status] = 'Active'")
                    .IsUnique();

                entity.HasOne(e => e.Membership)
                    .WithMany(m => m.MembershipRoles)
                    .HasForeignKey(e => e.MembershipId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.RoleDefinition)
                    .WithMany(r => r.MembershipRoles)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // --- Invitation ---
            builder.Entity<Invitation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
                entity.Property(e => e.InviteTokenHash).HasMaxLength(64).IsRequired();
                entity.HasIndex(e => e.InviteTokenHash);
            });

            // --- InvitationRole ---
            builder.Entity<InvitationRole>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.InvitationId, e.RoleId }).IsUnique();

                entity.HasOne(e => e.Invitation)
                    .WithMany(i => i.InvitationRoles)
                    .HasForeignKey(e => e.InvitationId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.RoleDefinition)
                    .WithMany()
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // --- EnterpriseApplication ---
            builder.Entity<EnterpriseApp>(entity =>
            {
                entity.ToTable("EnterpriseApplications");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(100);
                entity.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
            });

            // --- EnterpriseApplicationRole ---
            builder.Entity<EnterpriseAppRole>(entity =>
            {
                entity.ToTable("EnterpriseApplicationRoles");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.HasIndex(e => new { e.ApplicationId, e.Name }).IsUnique();

                entity.HasOne(e => e.Application)
                    .WithMany(a => a.Roles)
                    .HasForeignKey(e => e.ApplicationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // --- UserApplicationRole ---
            builder.Entity<UserApplicationRole>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.TenantId, e.CompanyId, e.ApplicationId, e.RoleId }).IsUnique();

                entity.HasOne(e => e.Role)
                    .WithMany(r => r.UserAssignments)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // --- WorkspaceSession ---
            builder.Entity<WorkspaceSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
                entity.Property(e => e.WorkspaceContextType).HasMaxLength(20);
                entity.Property(e => e.ClientId).HasMaxLength(200);
                entity.HasIndex(e => new { e.UserId, e.Status });
                entity.HasIndex(e => e.ExpiresAt);
            });

            // --- AuditLog ---
            builder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
                entity.Property(e => e.ResourceType).HasMaxLength(100);
                entity.Property(e => e.ResourceId).HasMaxLength(200);
                entity.Property(e => e.ClientId).HasMaxLength(200);
                entity.Property(e => e.IpAddress).HasMaxLength(45);
                entity.HasIndex(e => e.OccurredAt);
                entity.HasIndex(e => new { e.UserId, e.EventType });
                // No FK constraints on nullable ID columns — audit entries must survive referential changes
            });

            // --- EmailTemplate ---
            builder.Entity<EmailTemplate>(entity =>
            {
                entity.ToTable("EmailTemplates");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.TemplateType)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(e => e.Body)
                    .HasColumnType("nvarchar(max)")
                    .IsRequired();

                entity.HasIndex(e => new { e.TenantId, e.TemplateType })
                    .IsUnique()
                    .HasDatabaseName("IX_EmailTemplates_TenantId_TemplateType");

                entity.HasOne(e => e.Tenant)
                    .WithMany()
                    .HasForeignKey(e => e.TenantId)
                    .IsRequired(false)
                    .OnDelete(DeleteBehavior.SetNull);
            });

        }
    }
}
