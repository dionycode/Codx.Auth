using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Services;
using Duende.IdentityServer.Validation;
using Microsoft.AspNetCore.Http;
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
    /// <summary>
    /// Complete workspace context resolved by WorkspaceContextValidator.
    /// Stored in HttpContext.Items[WorkspaceContextKeys.Snapshot] to bridge the DI scope
    /// boundary between the validator and CustomProfileService.
    ///
    /// Duende IdentityServer resolves ICustomTokenRequestValidator and IProfileService in
    /// separate child DI scopes (both are children of the root, not the HTTP request scope).
    /// IWorkspaceContextAccessor (scoped) is therefore a DIFFERENT instance in each component.
    /// HttpContext.Items is shared for the lifetime of the HTTP request regardless of DI
    /// scope, making it the only reliable cross-component channel within a token request.
    ///
    /// Storing the full snapshot (not just a session ID + DB round-trip) eliminates:
    ///   - EF Core change-tracker visibility gaps between the two scopes' DbContexts
    ///   - Any race between SaveChangesAsync commit and the profile-service DB query
    ///   - An extra database round-trip per token issuance
    /// </summary>
    internal sealed class WorkspaceContextSnapshot
    {
        public Guid UserId { get; init; }
        public Guid TenantId { get; init; }
        public Guid? CompanyId { get; init; }
        public Guid MembershipId { get; init; }
        public string WorkspaceContextType { get; init; }
        public Guid WorkspaceSessionId { get; init; }
        public IReadOnlyList<string> WorkspaceRoleCodes { get; init; }
    }

    internal static class WorkspaceContextKeys
    {
        /// <summary>Key for the WorkspaceContextSnapshot stored in HttpContext.Items.</summary>
        internal const string Snapshot = "Codx.Auth.WorkspaceContext";
    }

    public class WorkspaceContextValidator : ICustomTokenRequestValidator
    {
        private readonly UserDbContext _db;
        private readonly IdentityServerDbContext _isDb;
        private readonly IWorkspaceContextAccessor _accessor;
        private readonly IWorkspaceSessionStore _sessionStore;
        private readonly IAuditService _audit;
        private readonly IHttpContextAccessor _httpCtx;
        private readonly ILogger<WorkspaceContextValidator> _logger;
        private readonly bool _enabled;

        public WorkspaceContextValidator(
            UserDbContext db,
            IdentityServerDbContext isDb,
            IWorkspaceContextAccessor accessor,
            IWorkspaceSessionStore sessionStore,
            IAuditService audit,
            IHttpContextAccessor httpCtx,
            IConfiguration configuration,
            ILogger<WorkspaceContextValidator> logger)
        {
            _db = db;
            _isDb = isDb;
            _accessor = accessor;
            _sessionStore = sessionStore;
            _audit = audit;
            _httpCtx = httpCtx;
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

                        // Renew the session so it stays alive across back-to-back token refreshes.
                        await _sessionStore.RevokeAllForUserClientAsync(userId, clientId);
                        var sessionLifetimeSecs = request.Client?.AbsoluteRefreshTokenLifetime > 0
                            ? request.Client.AbsoluteRefreshTokenLifetime
                            : Math.Max(request.AccessTokenLifetime, 86400);
                        var renewedSession = new WorkspaceSession
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            TenantId = existingSession.TenantId,
                            CompanyId = existingSession.CompanyId,
                            ClientId = clientId,
                            WorkspaceContextType = existingSession.WorkspaceContextType,
                            Status = "Active",
                            ExpiresAt = DateTime.UtcNow.AddSeconds(sessionLifetimeSecs),
                            CreatedAt = DateTime.UtcNow
                        };
                        await _sessionStore.CreateAsync(renewedSession);

                        var renewedSnapshot = new WorkspaceContextSnapshot
                        {
                            UserId = userId,
                            TenantId = existingSession.TenantId,
                            CompanyId = existingSession.CompanyId,
                            MembershipId = existingMembership.Id,
                            WorkspaceContextType = existingSession.WorkspaceContextType,
                            WorkspaceSessionId = renewedSession.Id,
                            WorkspaceRoleCodes = existingRoleCodes.AsReadOnly()
                        };

                        _accessor.UserId = renewedSnapshot.UserId;
                        _accessor.TenantId = renewedSnapshot.TenantId;
                        _accessor.CompanyId = renewedSnapshot.CompanyId;
                        _accessor.MembershipId = renewedSnapshot.MembershipId;
                        _accessor.WorkspaceContextType = renewedSnapshot.WorkspaceContextType;
                        _accessor.WorkspaceSessionId = renewedSnapshot.WorkspaceSessionId;
                        _accessor.WorkspaceRoleCodes = renewedSnapshot.WorkspaceRoleCodes;

                        // Bridge DI scope gap — store full snapshot in HttpContext.Items so
                        // CustomProfileService reads it directly without a second DB round-trip.
                        if (_httpCtx.HttpContext != null)
                            _httpCtx.HttpContext.Items[WorkspaceContextKeys.Snapshot] = renewedSnapshot;

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
                Fail(context, "access_denied");
                return;
            }

            // --- Resolve company_id (optional) ---
            Guid? companyId = null;
            if (!string.IsNullOrWhiteSpace(companyIdStr))
            {
                if (!Guid.TryParse(companyIdStr, out var parsedCompanyId))
                {
                    Fail(context, "invalid_request");
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
                    Fail(context, "access_denied");
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
                Fail(context, "access_denied");
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
            // Use the client's absolute refresh token lifetime so the session survives all token
            // refreshes until the refresh token itself expires. Fall back to 24 h minimum.
            var newSessionLifetime = request.Client?.AbsoluteRefreshTokenLifetime > 0
                ? request.Client.AbsoluteRefreshTokenLifetime
                : Math.Max(accessTokenLifetime, 86400);
            var session = new WorkspaceSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TenantId = tenantId,
                CompanyId = companyId,
                ClientId = clientId,
                WorkspaceContextType = workspaceContextType,
                Status = "Active",
                ExpiresAt = DateTime.UtcNow.AddSeconds(newSessionLifetime),
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

            // Build snapshot once — used for both the scoped accessor (same DI scope, if any)
            // and HttpContext.Items (reliable cross-scope bridge for CustomProfileService).
            var snapshot = new WorkspaceContextSnapshot
            {
                UserId = userId,
                TenantId = tenantId,
                CompanyId = companyId,
                MembershipId = membership.Id,
                WorkspaceContextType = workspaceContextType,
                WorkspaceSessionId = session.Id,
                WorkspaceRoleCodes = roleCodes.AsReadOnly()
            };

            // Populate the scoped accessor (works when validator and profile service share a scope).
            _accessor.UserId = snapshot.UserId;
            _accessor.TenantId = snapshot.TenantId;
            _accessor.CompanyId = snapshot.CompanyId;
            _accessor.MembershipId = snapshot.MembershipId;
            _accessor.WorkspaceContextType = snapshot.WorkspaceContextType;
            _accessor.WorkspaceSessionId = snapshot.WorkspaceSessionId;
            _accessor.WorkspaceRoleCodes = snapshot.WorkspaceRoleCodes;

            // Bridge DI scope gap — store full snapshot in HttpContext.Items so
            // CustomProfileService reads it directly without a second DB round-trip.
            if (_httpCtx.HttpContext != null)
                _httpCtx.HttpContext.Items[WorkspaceContextKeys.Snapshot] = snapshot;

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

            try
            {
                // Map requested scopes → API resource names (used as ApplicationId).
                // For refresh_token grants where no scope param is sent, RequestedScopes is null/empty.
                // Fall back to the client's full AllowedScopes so the auto-assign always runs.
                var requestedScopes = request.RequestedScopes?.ToList();
                if (requestedScopes == null || !requestedScopes.Any())
                    requestedScopes = request.Client?.AllowedScopes?.ToList() ?? new List<string>();

                if (!requestedScopes.Any())
                {
                    _logger.LogDebug("AutoAssignDefaultRoles: no scopes available for userId={UserId}, skipping", userId);
                    return;
                }

                // Primary lookup: find API resource names via IS4 ApiResourceScopes join
                var appIds = await _isDb.ApiResourceScopes
                    .Where(rs => requestedScopes.Contains(rs.Scope))
                    .Select(rs => rs.ApiResource.Name)
                    .Distinct()
                    .ToListAsync();

                // Fallback: scope names often match EnterpriseApplication IDs directly
                // (e.g. scope "ezra-api" → EnterpriseApplication.Id "ezra-api")
                if (!appIds.Any())
                {
                    _logger.LogDebug(
                        "AutoAssignDefaultRoles: IS4 scope join returned nothing for scopes [{Scopes}], trying direct EnterpriseApplication ID match",
                        string.Join(", ", requestedScopes));

                    appIds = await _db.EnterpriseApplications
                        .Where(a => a.IsActive && requestedScopes.Contains(a.Id))
                        .Select(a => a.Id)
                        .ToListAsync();
                }

                if (!appIds.Any())
                {
                    _logger.LogDebug("AutoAssignDefaultRoles: no matching EnterpriseApplications found for scopes [{Scopes}]", string.Join(", ", requestedScopes));
                    return;
                }

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

                if (!defaultRoles.Any())
                {
                    _logger.LogDebug(
                        "AutoAssignDefaultRoles: no IsDefault roles found for apps [{Apps}]",
                        string.Join(", ", appsWithoutRoles));
                    return;
                }

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
            catch (Exception ex)
            {
                // Non-fatal — log and continue. A failed auto-assign should not block token issuance.
                _logger.LogError(ex,
                    "AutoAssignDefaultRoles failed for userId={UserId} tenantId={TenantId} companyId={CompanyId}",
                    userId, tenantId, companyId);
            }
        }

        private static void Fail(CustomTokenRequestValidationContext context, string error)
        {
            context.Result.IsError = true;
            context.Result.Error = error;
        }
    }
}
