using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Services;
using Duende.IdentityServer.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Extensions
{
    /// <summary>
    /// IdentityServer custom token request validator that enforces workspace context.
    ///
    /// The flow is two-phased:
    ///   Phase 1 (initial login) — no workspace params are sent; the validator passes through,
    ///     and IdentityServer issues a basic identity token. The client uses this token to call
    ///     the "list memberships" API so the user can pick a workspace.
    ///   Phase 2 (workspace token) — the client re-requests a token with tenant_id /
    ///     company_id / workspace_context_type. The validator then:
    ///       1. Validates membership and access rights in UserMemberships.
    ///       2. Populates IWorkspaceContextAccessor so CustomProfileService can emit claims.
    ///       3. Creates/revokes WorkspaceSession rows for session exclusivity.
    ///
    /// Controlled by the "EnableWorkspaceContext" feature flag. When false the validator
    /// is a no-op and the legacy path in CustomProfileService takes over.
    /// </summary>
    public class WorkspaceContextValidator : ICustomTokenRequestValidator
    {
        private readonly UserDbContext _db;
        private readonly IdentityServerDbContext _isDb;
        private readonly IWorkspaceContextAccessor _accessor;
        private readonly IWorkspaceSessionStore _sessionStore;
        private readonly IAuditService _audit;
        private readonly ILogger<WorkspaceContextValidator> _logger;
        private readonly bool _enabled;

        public WorkspaceContextValidator(
            UserDbContext db,
            IdentityServerDbContext isDb,
            IWorkspaceContextAccessor accessor,
            IWorkspaceSessionStore sessionStore,
            IAuditService audit,
            IConfiguration configuration,
            ILogger<WorkspaceContextValidator> logger)
        {
            _db = db;
            _isDb = isDb;
            _accessor = accessor;
            _sessionStore = sessionStore;
            _audit = audit;
            _logger = logger;
            _enabled = configuration.GetValue<bool>("EnableWorkspaceContext");
        }

        public async Task ValidateAsync(CustomTokenRequestValidationContext context)
        {
            // Feature flag — when disabled, skip and let legacy path run.
            if (!_enabled)
            {
                return;
            }

            var request = context.Result.ValidatedRequest;
            var raw = request.Raw;

            // --- Extract parameters ---
            var tenantIdStr = raw.Get("tenant_id");
            var companyIdStr = raw.Get("company_id");
            var workspaceContextType = raw.Get("workspace_context_type") ?? "tenant";
            var clientId = request.ClientId;

            var subject = request.Subject;
            if (subject == null)
            {
                Fail(context, "No subject principal on token request.");
                return;
            }

            var subClaim = subject.FindFirst("sub");
            if (subClaim == null || !Guid.TryParse(subClaim.Value, out var userId))
            {
                Fail(context, "Invalid or missing sub claim.");
                return;
            }

            // tenant_id absent (or zero GUID) on a refresh_token grant → this is a silent renewal
            // from the SPA's OIDC library which does not re-send workspace params. Reuse the
            // existing active WorkspaceSession for this user+client so the renewed token carries
            // the same workspace context without requiring the user to re-select a workspace.
            var isZeroGuid = Guid.TryParse(tenantIdStr, out var parsedCheck) && parsedCheck == Guid.Empty;
            if (string.IsNullOrWhiteSpace(tenantIdStr) || isZeroGuid)
            {
                if (request.GrantType == "refresh_token")
                {
                    var existingSession = await _sessionStore.GetActiveAsync(userId, clientId);
                    if (existingSession != null)
                    {
                        _logger.LogDebug(
                            "WorkspaceContext: no workspace params on refresh — reusing active session {SessionId} for userId={UserId}",
                            existingSession.Id, userId);

                        // Resolve membership for the existing session context
                        var existingMembership = await _db.UserMemberships.AsNoTracking()
                            .FirstOrDefaultAsync(m =>
                                m.UserId == userId &&
                                m.TenantId == existingSession.TenantId &&
                                m.CompanyId == existingSession.CompanyId &&
                                m.Status == "Active");

                        if (existingMembership == null)
                        {
                            Fail(context, "No active membership found for the existing workspace session.");
                            return;
                        }

                        var existingRoleCodes = await _db.UserMembershipRoles.AsNoTracking()
                            .Where(umr => umr.MembershipId == existingMembership.Id && umr.Status == "Active")
                            .Join(_db.WorkspaceRoleDefinitions.Where(wrd => wrd.IsActive),
                                umr => umr.RoleId,
                                wrd => wrd.Id,
                                (umr, wrd) => wrd.Code)
                            .ToListAsync();

                        _accessor.UserId = userId;
                        _accessor.TenantId = existingSession.TenantId;
                        _accessor.CompanyId = existingSession.CompanyId;
                        _accessor.MembershipId = existingMembership.Id;
                        _accessor.WorkspaceContextType = existingSession.WorkspaceContextType;
                        _accessor.WorkspaceSessionId = existingSession.Id;
                        _accessor.WorkspaceRoleCodes = existingRoleCodes.AsReadOnly();
                        return;
                    }
                }

                // Phase 1: initial login with no workspace params — issue a basic identity token.
                return;
            }

            if (!Guid.TryParse(tenantIdStr, out var tenantId))
            {
                Fail(context, "tenant_id must be a valid GUID.");
                return;
            }

            // --- Validate tenant exists and is Active ---
            // Check both Status (new) and IsActive (legacy) so tenants created either way are accepted.
            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId && (t.Status == "Active" || t.IsActive));

            if (tenant == null)
            {
                await _audit.LogAsync("TokenIssuanceFailed", userId: userId, tenantId: tenantId,
                    details: "Tenant not found or inactive", clientId: clientId);
                Fail(context, "Requested tenant does not exist or is not active.");
                return;
            }

            // --- Resolve company_id (optional) ---
            Guid? companyId = null;
            if (!string.IsNullOrWhiteSpace(companyIdStr))
            {
                if (!Guid.TryParse(companyIdStr, out var parsedCompanyId))
                {
                    Fail(context, "company_id must be a valid GUID.");
                    return;
                }
                companyId = parsedCompanyId;
            }

            // --- Cross-tenant ownership check when company context is requested ---
            if (companyId.HasValue && workspaceContextType == "company")
            {
                var company = await _db.Companies.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == companyId.Value);

                if (company == null || company.TenantId != tenantId)
                {
                    await _audit.LogAsync("TokenIssuanceFailed", userId: userId, tenantId: tenantId,
                        companyId: companyId, details: "Company does not belong to requested tenant",
                        clientId: clientId);
                    Fail(context, "Requested company does not belong to the specified tenant.");
                    return;
                }
            }

            // --- Resolve membership ---
            var membership = await _db.UserMemberships.AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.UserId == userId &&
                    m.TenantId == tenantId &&
                    m.CompanyId == companyId &&
                    m.Status == "Active");

            if (membership == null)
            {
                await _audit.LogAsync("TokenIssuanceFailed", userId: userId, tenantId: tenantId,
                    companyId: companyId, details: "No active membership found", clientId: clientId);
                Fail(context, "No active membership found for the requested workspace context.");
                return;
            }

            // --- On refresh token: reject if membership is no longer Active ---
            if (request.GrantType == "refresh_token" && membership.Status != "Active")
            {
                await _audit.LogAsync("TokenIssuanceFailed", userId: userId, tenantId: tenantId,
                    companyId: companyId, details: "Membership no longer active on refresh",
                    clientId: clientId);
                Fail(context, "invalid_grant");
                return;
            }

            // --- Resolve workspace role codes ---
            var roleCodes = await _db.UserMembershipRoles.AsNoTracking()
                .Where(umr => umr.MembershipId == membership.Id && umr.Status == "Active")
                .Join(_db.WorkspaceRoleDefinitions.Where(wrd => wrd.IsActive),
                    umr => umr.RoleId,
                    wrd => wrd.Id,
                    (umr, wrd) => wrd.Code)
                .ToListAsync();

            // --- Session exclusivity: revoke any existing active session, then create a new one ---
            await _sessionStore.RevokeAllForUserClientAsync(userId, clientId);

            var accessTokenLifetime = request.AccessTokenLifetime; // seconds
            var session = new WorkspaceSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                CompanyId = companyId,
                ClientId = clientId,
                WorkspaceContextType = workspaceContextType,
                Status = "Active",
                ExpiresAt = DateTime.UtcNow.AddSeconds(accessTokenLifetime),
                CreatedAt = DateTime.UtcNow
            };

            await _sessionStore.CreateAsync(session);

            await _audit.LogAsync("WorkspaceSessionCreated",
                userId: userId,
                actorUserId: userId,
                tenantId: tenantId,
                companyId: companyId,
                resourceType: "WorkspaceSession",
                resourceId: session.Id.ToString(),
                clientId: clientId);

            // Auto-assign any IsDefault application roles for apps the user has no existing
            // assignment for. This ensures every member can use the application immediately
            // without waiting for an admin to manually grant them a role.
            await AutoAssignDefaultRolesAsync(userId, tenantId, companyId, request);

            // --- Populate the scoped accessor for CustomProfileService ---
            _accessor.UserId = userId;
            _accessor.TenantId = tenantId;
            _accessor.CompanyId = companyId;
            _accessor.MembershipId = membership.Id;
            _accessor.WorkspaceContextType = workspaceContextType;
            _accessor.WorkspaceSessionId = session.Id;
            _accessor.WorkspaceRoleCodes = roleCodes.AsReadOnly();

            _logger.LogDebug(
                "WorkspaceContext resolved: userId={UserId} tenantId={TenantId} companyId={CompanyId} roles=[{Roles}]",
                userId, tenantId, companyId, string.Join(", ", roleCodes));
        }

        /// <summary>
        /// For each EnterpriseApplication whose scopes are being requested, if the user has no
        /// existing UserApplicationRole assignment for that app in this workspace, auto-assigns
        /// all roles marked IsDefault. This prevents the "no role → no access" bootstrap problem
        /// for new members.
        /// </summary>
        private async Task AutoAssignDefaultRolesAsync(
            Guid userId, Guid tenantId, Guid? companyId, ValidatedTokenRequest request)
        {
            if (!companyId.HasValue) return;

            // Map requested scopes → API resource names (used as ApplicationId)
            var requestedScopes = request.RequestedScopes?.ToList() ?? new List<string>();
            if (!requestedScopes.Any()) return;

            var appIds = await _isDb.ApiResourceScopes
                .Where(rs => requestedScopes.Contains(rs.Scope))
                .Select(rs => rs.ApiResource.Name)
                .Distinct()
                .ToListAsync();

            if (!appIds.Any()) return;

            // Check existing assignments for this user/workspace
            var existingAppIds = await _db.UserApplicationRoles
                .Where(uar =>
                    uar.UserId == userId &&
                    uar.TenantId == tenantId &&
                    uar.CompanyId == companyId.Value &&
                    appIds.Contains(uar.ApplicationId))
                .Select(uar => uar.ApplicationId)
                .Distinct()
                .ToListAsync();

            // Only auto-assign for apps where the user has NO existing assignment at all
            var appsWithoutRoles = appIds.Except(existingAppIds).ToList();
            if (!appsWithoutRoles.Any()) return;

            var defaultRoles = await _db.EnterpriseApplicationRoles
                .Where(r =>
                    appsWithoutRoles.Contains(r.ApplicationId) &&
                    r.IsActive &&
                    r.IsDefault)
                .ToListAsync();

            if (!defaultRoles.Any()) return;

            var now = DateTime.UtcNow;
            var newAssignments = defaultRoles.Select(role => new UserApplicationRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                CompanyId = companyId.Value,
                ApplicationId = role.ApplicationId,
                RoleId = role.Id,
                AssignedAt = now,
                AssignedByUserId = userId   // self-assigned via default role policy
            }).ToList();

            _db.UserApplicationRoles.AddRange(newAssignments);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "AutoAssignDefaultRoles: assigned {Count} default role(s) for userId={UserId} in tenantId={TenantId}/companyId={CompanyId} [apps: {Apps}]",
                newAssignments.Count, userId, tenantId, companyId,
                string.Join(", ", defaultRoles.Select(r => $"{r.ApplicationId}:{r.Name}")));
        }

        private static void Fail(CustomTokenRequestValidationContext context, string error)
        {
            context.Result.IsError = true;
            context.Result.Error = error;
        }
    }
}
