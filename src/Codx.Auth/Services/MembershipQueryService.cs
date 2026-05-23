using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Infrastructure.Lifecycle;
using Codx.Auth.Models.Responses;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public interface IMembershipQueryService
    {
        /// <summary>
        /// Returns company-scoped memberships for the user where the membership, tenant and
        /// company are all active. For each company membership the workspace roles of the
        /// parent tenant-scoped membership (if any) are merged into WorkspaceRoles so that
        /// tenant-level role grants are visible in every workspace context.
        ///
        /// Pass activeTenantId / activeCompanyId to populate IsActive on each entry.
        /// Pass applicationId to resolve the ApplicationRole claim for each workspace.
        /// Both are optional — omit when workspace context is not yet established
        /// (e.g. the workspace-selection screen).
        /// </summary>
        Task<List<WorkspaceMembershipResponse>> GetCompanyMembershipsAsync(
            Guid userId,
            Guid? activeTenantId = null,
            Guid? activeCompanyId = null,
            string applicationId = null,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the TenantId of the user's oldest active membership.
        /// Used for pre-auth tenant resolution (e.g. password reset email templating).
        /// Returns null if the user has no active memberships, which causes callers to
        /// fall back to the global email template.
        /// </summary>
        Task<Guid?> GetPrimaryTenantIdAsync(Guid userId);
    }

    public class MembershipQueryService : IMembershipQueryService
    {
        private readonly UserDbContext _db;

        public MembershipQueryService(UserDbContext db)
        {
            _db = db;
        }

        public async Task<List<WorkspaceMembershipResponse>> GetCompanyMembershipsAsync(
            Guid userId,
            Guid? activeTenantId = null,
            Guid? activeCompanyId = null,
            string applicationId = null,
            CancellationToken ct = default)
        {
            // 1. Load active company-scoped memberships (same criteria as workspace-select)
            var companyMemberships = await _db.UserMemberships
                .Include(m => m.Tenant)
                .Include(m => m.Company)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(mr => mr.RoleDefinition)
                .Where(m => m.UserId == userId
                         && m.CompanyId.HasValue
                         && m.Status == LifecycleStatus.Membership.Active
                         && m.Tenant.Status == LifecycleStatus.Tenant.Active
                         && m.Company.Status == LifecycleStatus.Company.Active)
                .AsNoTracking()
                .ToListAsync(ct);

            if (!companyMemberships.Any())
                return new List<WorkspaceMembershipResponse>();

            // 2. Load parent tenant-scoped memberships (CompanyId = null) for the same tenants
            //    so their roles can be merged into each company workspace entry.
            var tenantIds = companyMemberships.Select(m => m.TenantId).Distinct().ToList();

            var tenantMemberships = await _db.UserMemberships
                .Include(m => m.MembershipRoles)
                    .ThenInclude(mr => mr.RoleDefinition)
                .Where(m => m.UserId == userId
                         && !m.CompanyId.HasValue
                         && tenantIds.Contains(m.TenantId)
                         && m.Status == LifecycleStatus.Membership.Active)
                .AsNoTracking()
                .ToListAsync(ct);

            var tenantMembershipByTenantId = tenantMemberships.ToDictionary(m => m.TenantId);

            // 3. Optionally load application-role assignments for this user + application
            List<UserApplicationRole> appRoles = null;
            if (!string.IsNullOrEmpty(applicationId))
            {
                appRoles = await _db.UserApplicationRoles
                    .Include(ar => ar.Role)
                    .Where(ar => ar.UserId == userId
                              && ar.ApplicationId == applicationId
                              && ar.Status == LifecycleStatus.RoleAssignment.Active)
                    .AsNoTracking()
                    .ToListAsync(ct);
            }

            return companyMemberships.Select(m =>
            {
                // Roles from the company-level membership
                var companyRoles = m.MembershipRoles
                    .Where(mr => mr.Status == LifecycleStatus.MembershipRole.Active
                              && mr.RoleDefinition != null)
                    .Select(mr => mr.RoleDefinition.Code)
                    .ToList();

                // Roles from the parent tenant-level membership (if any)
                var tenantRoles = tenantMembershipByTenantId.TryGetValue(m.TenantId, out var tm)
                    ? tm.MembershipRoles
                        .Where(mr => mr.Status == LifecycleStatus.MembershipRole.Active
                                  && mr.RoleDefinition != null)
                        .Select(mr => mr.RoleDefinition.Code)
                        .ToList()
                    : new List<string>();

                // Merge — company roles first, then tenant-only roles deduplicated
                var workspaceRoles = companyRoles.Union(tenantRoles).ToList();

                // Application role scoped to this tenant + company
                var appRole = appRoles?
                    .FirstOrDefault(ar => ar.TenantId == m.TenantId
                                       && ar.CompanyId == m.CompanyId.Value)
                    ?.Role?.Name;

                var isActive = activeTenantId.HasValue
                    && m.TenantId == activeTenantId.Value
                    && m.CompanyId == activeCompanyId;

                return new WorkspaceMembershipResponse
                {
                    MembershipId = m.Id,
                    TenantId = m.TenantId,
                    TenantName = m.Tenant?.Name,
                    ContextType = "company",
                    CompanyId = m.CompanyId,
                    CompanyName = m.Company?.Name,
                    JoinedAt = m.JoinedAt,
                    ApplicationRole = appRole,
                    WorkspaceRoles = workspaceRoles,
                    IsActive = isActive,
                };
            }).ToList();
        }

        public async Task<Guid?> GetPrimaryTenantIdAsync(Guid userId)
        {
            var membership = await _db.UserMemberships
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.Status == LifecycleStatus.Membership.Active)
                .OrderBy(m => m.JoinedAt)
                .FirstOrDefaultAsync();

            return membership?.TenantId;
        }
    }
}
