using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Infrastructure.Lifecycle;
using Codx.Auth.Models.WorkspaceUsers;
using Codx.Auth.Services.Interfaces;
using Duende.IdentityServer.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public class WorkspaceUserService : IWorkspaceUserService
    {
        private readonly UserDbContext _userDb;
        private readonly IdentityServerDbContext _isDb;
        private readonly IInvitationService _invitationService;
        private readonly IPersistedGrantStore _persistedGrantStore;
        private readonly ILogger<WorkspaceUserService> _logger;

        public WorkspaceUserService(
            UserDbContext userDb,
            IdentityServerDbContext isDb,
            IInvitationService invitationService,
            IPersistedGrantStore persistedGrantStore,
            ILogger<WorkspaceUserService> logger)
        {
            _userDb = userDb;
            _isDb   = isDb;
            _invitationService = invitationService;
            _persistedGrantStore = persistedGrantStore;
            _logger = logger;
        }

        public async Task<WorkspaceUsersResponse?> GetWorkspaceUsersAsync(
            Guid   tenantId,
            Guid   companyId,
            string clientId,
            int    page,
            int    pageSize,
            string? email,
            CancellationToken cancellationToken = default)
        {
            // 1. Resolve application_id from ClientProperties via client_id claim
            var applicationId = await _isDb.Clients
                .Where(c => c.ClientId == clientId)
                .SelectMany(c => c.Properties)
                .Where(p => p.Key == "application_id")
                .Select(p => p.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (applicationId == null)
                return null;

            // 2. Build the user query with all lifecycle filters
            var query =
                from uar in _userDb.UserApplicationRoles
                join u   in _userDb.Users                    on uar.UserId  equals u.Id
                join ear in _userDb.EnterpriseApplicationRoles on uar.RoleId equals ear.Id
                join t   in _userDb.Tenants                  on uar.TenantId equals t.Id
                join c   in _userDb.Companies                on uar.CompanyId equals c.Id
                where uar.TenantId      == tenantId
                   && uar.CompanyId     == companyId
                   && uar.ApplicationId == applicationId
                   && uar.Status        == LifecycleStatus.RoleAssignment.Active
                   && t.Status          == LifecycleStatus.Tenant.Active
                   && c.TenantId        == tenantId
                   && c.Status          == LifecycleStatus.Company.Active
                   && _userDb.UserMemberships.Any(um =>
                          um.UserId    == uar.UserId
                       && um.TenantId  == tenantId
                       && (um.CompanyId == null || um.CompanyId == companyId)
                       && um.Status    == LifecycleStatus.Membership.Active)
                select new
                {
                    UserId          = u.Id,
                    u.GivenName,
                    u.MiddleName,
                    u.FamilyName,
                    u.UserName,
                    u.Email,
                    u.NormalizedEmail,
                    ApplicationRole = ear.Name,
                };

            // 3. Apply optional email filter
            if (!string.IsNullOrEmpty(email))
            {
                var upper = email.ToUpperInvariant();
                query = query.Where(x => x.NormalizedEmail != null && x.NormalizedEmail.Contains(upper));
            }

            // 4. Count (for pagination metadata) then fetch the page
            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

            var rows = await query
                .OrderBy(x => x.GivenName)
                .ThenBy(x => x.FamilyName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            var data = rows
                .Select(x =>
                {
                    var nameParts = new[] { x.GivenName, x.MiddleName, x.FamilyName }
                        .Where(p => !string.IsNullOrWhiteSpace(p));
                    var fullName = string.Join(" ", nameParts).Trim();
                    if (string.IsNullOrEmpty(fullName))
                        fullName = x.UserName ?? x.Email ?? string.Empty;

                    return new WorkspaceUserDto(
                        x.UserId,
                        fullName,
                        x.Email    ?? string.Empty,
                        x.ApplicationRole);
                })
                .ToList();

            var pagination = new PaginationMeta(
                Page:           page,
                PageSize:       pageSize,
                TotalCount:     totalCount,
                TotalPages:     totalPages,
                HasNextPage:    page < totalPages,
                HasPreviousPage: page > 1);

            return new WorkspaceUsersResponse(data, pagination);
        }

        // ────────────────────────────────────────────────────────────────
        // Add or Invite (spec 003-03)
        // ────────────────────────────────────────────────────────────────

        public async Task<AddWorkspaceUserResult> AddOrInviteUserAsync(
            AddWorkspaceUserRequest request,
            WorkspaceAddCallerContext callerContext,
            CancellationToken cancellationToken = default)
        {
            // 1. Resolve application_id from ClientProperties via client_id claim
            var applicationId = await _isDb.Clients
                .Where(c => c.ClientId == callerContext.ClientId)
                .SelectMany(c => c.Properties)
                .Where(p => p.Key == "application_id")
                .Select(p => p.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (applicationId == null)
                return AddWorkspaceUserResult.Forbidden(
                    "Client is not mapped to an enterprise application.");

            // 2. Resolve effective applicationRoles
            IReadOnlyList<string> resolvedRoles;
            bool roleWasExplicit;

            if (!string.IsNullOrWhiteSpace(request.ApplicationRole))
            {
                // Explicit role: validate it exists and is active for this application
                var roleExists = await _userDb.EnterpriseApplicationRoles
                    .AnyAsync(r => r.ApplicationId == applicationId
                                && r.Name          == request.ApplicationRole
                                && r.Status        == LifecycleStatus.AppRole.Active,
                              cancellationToken);

                if (!roleExists)
                    return AddWorkspaceUserResult.BadRequest(
                        $"Role '{request.ApplicationRole}' is not a valid role for this application.");

                resolvedRoles   = new[] { request.ApplicationRole };
                roleWasExplicit = true;
            }
            else
            {
                // Default path: all IsDefault = true roles for this application
                var defaults = await _userDb.EnterpriseApplicationRoles
                    .Where(r => r.ApplicationId == applicationId
                             && r.IsDefault
                             && r.Status == LifecycleStatus.AppRole.Active)
                    .Select(r => r.Name)
                    .ToListAsync(cancellationToken);

                if (defaults.Count == 0)
                    return AddWorkspaceUserResult.BadRequest(
                        "applicationRole is required — no default roles are configured for this application.");

                resolvedRoles   = defaults;
                roleWasExplicit = false;
            }

            // 3. Role constraint matrix — only for explicitly supplied roles (AD-15)
            if (roleWasExplicit)
            {
                var role = resolvedRoles[0];
                var forbidden = false;

                if (callerContext.CallerWorkspaceRoles.Contains("COMPANY_ADMIN",
                        StringComparer.OrdinalIgnoreCase)
                    && !callerContext.CallerWorkspaceRoles.Any(r =>
                            r.Equals("TENANT_OWNER", StringComparison.OrdinalIgnoreCase) ||
                            r.Equals("TENANT_ADMIN",  StringComparison.OrdinalIgnoreCase)))
                {
                    // COMPANY_ADMIN may only assign User
                    forbidden = !role.Equals("User", StringComparison.OrdinalIgnoreCase);
                }
                else if (callerContext.CallerWorkspaceRoles.Contains("TENANT_ADMIN",
                        StringComparer.OrdinalIgnoreCase)
                    && !callerContext.CallerWorkspaceRoles.Any(r =>
                            r.Equals("TENANT_OWNER", StringComparison.OrdinalIgnoreCase)))
                {
                    // TENANT_ADMIN may not assign Admin
                    forbidden = role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
                }

                if (forbidden)
                    return AddWorkspaceUserResult.Forbidden(
                        $"Your workspace role does not permit assigning the '{role}' application role.");
            }

            // 4. Look up user by email
            var existingUser = await _userDb.Users
                .Where(u => u.NormalizedEmail == request.Email.ToUpperInvariant())
                .Select(u => new { u.Id, u.Email })
                .FirstOrDefaultAsync(cancellationToken);

            if (existingUser != null)
            {
                // ── Add path ──
                // 5a. Duplicate active membership check
                var alreadyMember = await _userDb.UserMemberships
                    .AnyAsync(m => m.UserId    == existingUser.Id
                               && m.TenantId  == callerContext.TenantId
                               && m.CompanyId == callerContext.CompanyId
                               && m.Status    == LifecycleStatus.Membership.Active,
                             cancellationToken);

                if (alreadyMember)
                    return AddWorkspaceUserResult.Conflict("USER_ALREADY_MEMBER");

                // 5b. Atomic membership + role creation
                await CreateMembershipAsync(
                    userId:         existingUser.Id,
                    tenantId:       callerContext.TenantId,
                    companyId:      callerContext.CompanyId,
                    applicationId:  applicationId,
                    applicationRoles: resolvedRoles,
                    membershipRoleCode: "MEMBER",
                    assignedByUserId: callerContext.CallerId,
                    cancellationToken: cancellationToken);

                return AddWorkspaceUserResult.Added(new AddWorkspaceUserResponse
                {
                    Outcome          = "added",
                    UserId           = existingUser.Id,
                    Email            = existingUser.Email ?? request.Email,
                    ApplicationRoles = resolvedRoles,
                });
            }
            else
            {
                // ── Invite path ──
                // 6a. Duplicate pending invitation check
                var alreadyPending = await _userDb.Invitations
                    .AnyAsync(i => i.Email     == request.Email
                               && i.TenantId  == callerContext.TenantId
                               && i.CompanyId == callerContext.CompanyId
                               && i.Status    == "Pending",
                             cancellationToken);

                if (alreadyPending)
                    return AddWorkspaceUserResult.Conflict("INVITATION_ALREADY_PENDING");

                // 6b. Resolve redirect URI — ClientProperties.invitation_redirect_uri preferred;
                // falls back to the client's first registered RedirectUri so that invitations
                // can be sent even before the property is explicitly configured.
                var redirectUri = await _isDb.Clients
                    .Where(c => c.ClientId == callerContext.ClientId)
                    .SelectMany(c => c.Properties)
                    .Where(p => p.Key == "invitation_redirect_uri")
                    .Select(p => p.Value)
                    .FirstOrDefaultAsync(cancellationToken);

                if (string.IsNullOrWhiteSpace(redirectUri))
                {
                    redirectUri = await _isDb.Clients
                        .Where(c => c.ClientId == callerContext.ClientId)
                        .SelectMany(c => c.RedirectUris)
                        .Select(r => r.RedirectUri)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                // RedirectUri may legitimately be null — AccountController falls back to Login.

                // 6c. Create the workspace invitation
                var (success, invitationId, error) = await _invitationService
                    .CreateWorkspaceInvitationAsync(
                        email:            request.Email,
                        tenantId:         callerContext.TenantId,
                        companyId:        callerContext.CompanyId,
                        applicationId:    applicationId,
                        applicationRoles: resolvedRoles,
                        membershipRole:   "MEMBER",
                        redirectUri:      redirectUri,
                        createdByUserId:  callerContext.CallerId,
                        cancellationToken: cancellationToken);

                if (!success)
                    return AddWorkspaceUserResult.BadRequest(error);

                return AddWorkspaceUserResult.Invited(new AddWorkspaceUserResponse
                {
                    Outcome          = "invited",
                    InvitationId     = invitationId,
                    Email            = request.Email,
                    ApplicationRoles = resolvedRoles,
                });
            }
        }

        /// <summary>
        /// Atomically creates UserMembership + UserMembershipRole + one UserApplicationRole
        /// per resolved role in a single SaveChangesAsync call.
        /// Shared by the direct-add path and the invitation acceptance handler.
        /// </summary>
        public async Task CreateMembershipAsync(
            Guid userId,
            Guid tenantId,
            Guid companyId,
            string applicationId,
            IReadOnlyList<string> applicationRoles,
            string membershipRoleCode,
            Guid assignedByUserId,
            CancellationToken cancellationToken = default)
        {
            // Resolve WorkspaceRoleDefinition.Id for the membershipRoleCode
            var roleDefId = await _userDb.WorkspaceRoleDefinitions
                .Where(r => r.Code   == membershipRoleCode
                         && r.Status == LifecycleStatus.RoleDefinition.Active)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (roleDefId == 0)
                throw new InvalidOperationException(
                    $"WorkspaceRoleDefinition with Code='{membershipRoleCode}' not found or inactive.");

            // Resolve EnterpriseApplicationRole IDs for each role name
            var roleEntities = await _userDb.EnterpriseApplicationRoles
                .Where(r => r.ApplicationId == applicationId
                         && applicationRoles.Contains(r.Name)
                         && r.Status == LifecycleStatus.AppRole.Active)
                .Select(r => new { r.Id, r.Name })
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;

            // ── UserMembership: reactivate if previously removed, otherwise insert ──
            var existingMembership = await _userDb.UserMemberships
                .FirstOrDefaultAsync(
                    m => m.UserId    == userId
                      && m.TenantId  == tenantId
                      && m.CompanyId == companyId,
                    cancellationToken);

            UserMembership membership;

            if (existingMembership is not null)
            {
                // Reactivate the soft-deleted membership in-place
                existingMembership.Status            = LifecycleStatus.Membership.Active;
                existingMembership.StatusChangedAt   = now;
                existingMembership.StatusChangedBy   = assignedByUserId;

                // Revoke all existing membership roles, then re-add
                var existingMembershipRoles = await _userDb.UserMembershipRoles
                    .Where(r => r.MembershipId == existingMembership.Id)
                    .ToListAsync(cancellationToken);

                foreach (var r in existingMembershipRoles)
                {
                    r.Status            = LifecycleStatus.MembershipRole.Removed;
                    r.StatusChangedAt   = now;
                    r.StatusChangedBy   = assignedByUserId;
                }

                _userDb.UserMembershipRoles.Add(new UserMembershipRole
                {
                    Id               = Guid.NewGuid(),
                    MembershipId     = existingMembership.Id,
                    RoleId           = roleDefId,
                    Status           = LifecycleStatus.MembershipRole.Active,
                    AssignedAt       = now,
                    AssignedByUserId = assignedByUserId,
                });

                membership = existingMembership;
            }
            else
            {
                membership = new UserMembership
                {
                    Id        = Guid.NewGuid(),
                    UserId    = userId,
                    TenantId  = tenantId,
                    CompanyId = companyId,
                    Status    = LifecycleStatus.Membership.Active,
                    JoinedAt  = now,
                    MembershipRoles = new List<UserMembershipRole>
                    {
                        new()
                        {
                            Id               = Guid.NewGuid(),
                            RoleId           = roleDefId,
                            Status           = LifecycleStatus.MembershipRole.Active,
                            AssignedAt       = now,
                            AssignedByUserId = assignedByUserId,
                        }
                    }
                };

                await _userDb.UserMemberships.AddAsync(membership, cancellationToken);
            }

            // ── UserApplicationRoles: reactivate if revoked, otherwise insert ──
            foreach (var roleEntity in roleEntities)
            {
                var existingUar = await _userDb.UserApplicationRoles
                    .FirstOrDefaultAsync(
                        r => r.UserId        == userId
                          && r.TenantId      == tenantId
                          && r.CompanyId     == companyId
                          && r.ApplicationId == applicationId
                          && r.RoleId        == roleEntity.Id,
                        cancellationToken);

                if (existingUar is not null)
                {
                    existingUar.Status           = LifecycleStatus.RoleAssignment.Active;
                    existingUar.AssignedAt       = now;
                    existingUar.AssignedByUserId = assignedByUserId;
                    existingUar.RevokedAt        = null;
                    existingUar.RevokedByUserId  = null;
                }
                else
                {
                    await _userDb.UserApplicationRoles.AddAsync(new UserApplicationRole
                    {
                        Id               = Guid.NewGuid(),
                        UserId           = userId,
                        TenantId         = tenantId,
                        CompanyId        = companyId,
                        ApplicationId    = applicationId,
                        RoleId           = roleEntity.Id,
                        Status           = LifecycleStatus.RoleAssignment.Active,
                        AssignedAt       = now,
                        AssignedByUserId = assignedByUserId,
                    }, cancellationToken);
                }
            }

            await _userDb.SaveChangesAsync(cancellationToken);
        }

        // ────────────────────────────────────────────────────────────────
        // Update Member Role (spec 003-04)
        // ────────────────────────────────────────────────────────────────

        public async Task<ServiceResult<UpdateMemberRoleResponse>> UpdateMemberRoleAsync(
            Guid userId,
            UpdateMemberRoleRequest request,
            WorkspaceAddCallerContext callerContext,
            CancellationToken cancellationToken = default)
        {
            // 1. Self-modification guard
            if (userId == callerContext.CallerId)
                return ServiceResult<UpdateMemberRoleResponse>.Forbidden("CANNOT_MODIFY_SELF");

            // 2. Resolve application_id from ClientProperties
            var applicationId = await _isDb.Clients
                .Where(c => c.ClientId == callerContext.ClientId)
                .SelectMany(c => c.Properties)
                .Where(p => p.Key == "application_id")
                .Select(p => p.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (applicationId == null)
                return ServiceResult<UpdateMemberRoleResponse>.Forbidden("CLIENT_NOT_MAPPED");

            // 3. Validate role value (dynamic — ApplicationId-scoped via EnterpriseApplicationRoles)
            var roleEntity = await _userDb.EnterpriseApplicationRoles
                .Where(r => r.ApplicationId == applicationId
                         && r.Name          == request.ApplicationRole
                         && r.Status        == LifecycleStatus.AppRole.Active)
                .Select(r => new { r.Id, r.Name })
                .FirstOrDefaultAsync(cancellationToken);

            if (roleEntity == null)
                return ServiceResult<UpdateMemberRoleResponse>.BadRequest("ROLE_INVALID");

            // 4. Role constraint check — caller's workspace_role limits assignable roles
            var forbidden = false;

            if (callerContext.CallerWorkspaceRoles.Contains("COMPANY_ADMIN", StringComparer.OrdinalIgnoreCase)
                && !callerContext.CallerWorkspaceRoles.Any(r =>
                        r.Equals("TENANT_OWNER", StringComparison.OrdinalIgnoreCase) ||
                        r.Equals("TENANT_ADMIN",  StringComparison.OrdinalIgnoreCase)))
            {
                // COMPANY_ADMIN may only assign User
                forbidden = !roleEntity.Name.Equals("User", StringComparison.OrdinalIgnoreCase);
            }
            else if (callerContext.CallerWorkspaceRoles.Contains("TENANT_ADMIN",
                        StringComparer.OrdinalIgnoreCase)
                && !callerContext.CallerWorkspaceRoles.Any(r =>
                        r.Equals("TENANT_OWNER", StringComparison.OrdinalIgnoreCase)))
            {
                // TENANT_ADMIN may not assign Admin
                forbidden = roleEntity.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            }

            if (forbidden)
                return ServiceResult<UpdateMemberRoleResponse>.Forbidden("ROLE_NOT_PERMITTED");

            // 5. Active membership check
            var membership = await FindMembershipAsync(userId, callerContext.TenantId, callerContext.CompanyId, cancellationToken);
            if (membership == null)
                return ServiceResult<UpdateMemberRoleResponse>.NotFound();
            if (membership.Status != LifecycleStatus.Membership.Active)
                return ServiceResult<UpdateMemberRoleResponse>.Conflict("MEMBERSHIP_NOT_ACTIVE");

            // 6. Idempotency check — return success immediately if role already matches
            var alreadyActive = await _userDb.UserApplicationRoles
                .AnyAsync(uar => uar.UserId        == userId
                              && uar.CompanyId     == callerContext.CompanyId
                              && uar.ApplicationId == applicationId
                              && uar.RoleId        == roleEntity.Id
                              && uar.Status        == LifecycleStatus.RoleAssignment.Active,
                          cancellationToken);

            if (alreadyActive)
                return ServiceResult<UpdateMemberRoleResponse>.Success(
                    new UpdateMemberRoleResponse { UserId = userId, ApplicationRole = request.ApplicationRole });

            // 7. Atomic lifecycle transition — deactivate old, insert new
            var now = DateTime.UtcNow;

            var existingRoles = await _userDb.UserApplicationRoles
                .Where(uar => uar.UserId        == userId
                           && uar.CompanyId     == callerContext.CompanyId
                           && uar.ApplicationId == applicationId
                           && uar.Status        == LifecycleStatus.RoleAssignment.Active)
                .ToListAsync(cancellationToken);

            foreach (var uar in existingRoles)
            {
                uar.Status         = LifecycleStatus.RoleAssignment.Revoked;
                uar.RevokedAt      = now;
                uar.RevokedByUserId = callerContext.CallerId;
            }

            await _userDb.UserApplicationRoles.AddAsync(new UserApplicationRole
            {
                Id               = Guid.NewGuid(),
                UserId           = userId,
                TenantId         = callerContext.TenantId,
                CompanyId        = callerContext.CompanyId,
                ApplicationId    = applicationId,
                RoleId           = roleEntity.Id,
                Status           = LifecycleStatus.RoleAssignment.Active,
                AssignedAt       = now,
                AssignedByUserId = callerContext.CallerId,
            }, cancellationToken);

            await _userDb.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Member role updated: userId={UserId}, newRole={Role}, by={CallerId}, companyId={CompanyId}",
                userId, request.ApplicationRole, callerContext.CallerId, callerContext.CompanyId);

            return ServiceResult<UpdateMemberRoleResponse>.Success(
                new UpdateMemberRoleResponse { UserId = userId, ApplicationRole = request.ApplicationRole });
        }

        // ────────────────────────────────────────────────────────────────
        // Remove Member (spec 003-04)
        // ────────────────────────────────────────────────────────────────

        public async Task<ServiceResult<RemoveMemberResponse>> RemoveMemberAsync(
            Guid userId,
            WorkspaceAddCallerContext callerContext,
            CancellationToken cancellationToken = default)
        {
            // 1. Self-modification guard
            if (userId == callerContext.CallerId)
                return ServiceResult<RemoveMemberResponse>.Forbidden("CANNOT_MODIFY_SELF");

            // 2. Active membership check
            var membership = await FindMembershipAsync(userId, callerContext.TenantId, callerContext.CompanyId, cancellationToken);
            if (membership == null)
                return ServiceResult<RemoveMemberResponse>.NotFound();
            if (membership.Status != LifecycleStatus.Membership.Active)
                return ServiceResult<RemoveMemberResponse>.Conflict("MEMBERSHIP_ALREADY_REMOVED");

            // 3. Atomic soft-removal
            var now = DateTime.UtcNow;

            membership.Status          = LifecycleStatus.Membership.Removed;
            membership.StatusChangedAt = now;
            membership.StatusChangedBy = callerContext.CallerId;

            var activeRoles = await _userDb.UserApplicationRoles
                .Where(uar => uar.UserId    == userId
                           && uar.CompanyId == callerContext.CompanyId
                           && uar.Status    == LifecycleStatus.RoleAssignment.Active)
                .ToListAsync(cancellationToken);

            foreach (var uar in activeRoles)
            {
                uar.Status          = LifecycleStatus.RoleAssignment.Revoked;
                uar.RevokedAt       = now;
                uar.RevokedByUserId = callerContext.CallerId;
            }

            await _userDb.SaveChangesAsync(cancellationToken);

            // 4. Revoke persisted grants (refresh tokens) — non-fatal if it fails
            try
            {
                await _persistedGrantStore.RemoveAllAsync(new Duende.IdentityServer.Stores.PersistedGrantFilter
                {
                    SubjectId = userId.ToString(),
                    ClientId  = callerContext.ClientId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to revoke persisted grants for userId={UserId}, clientId={ClientId}. " +
                    "Membership has already been removed; token expiry is the safety net.",
                    userId, callerContext.ClientId);
            }

            _logger.LogInformation(
                "Member removed: userId={UserId}, by={CallerId}, companyId={CompanyId}",
                userId, callerContext.CallerId, callerContext.CompanyId);

            return ServiceResult<RemoveMemberResponse>.Success(
                new RemoveMemberResponse { UserId = userId, Outcome = "removed" });
        }

        // ────────────────────────────────────────────────────────────────
        // Private helpers
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Looks up a UserMembership for the given userId/tenantId/companyId with NO Status filter.
        /// Returns null when no record exists (→ 404). Caller inspects Status to distinguish
        /// Active (→ proceed) from non-Active (→ 409).
        /// </summary>
        private async Task<UserMembership?> FindMembershipAsync(
            Guid userId, Guid tenantId, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            return await _userDb.UserMemberships
                .Where(um => um.UserId    == userId
                          && um.TenantId  == tenantId
                          && um.CompanyId == companyId)
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
