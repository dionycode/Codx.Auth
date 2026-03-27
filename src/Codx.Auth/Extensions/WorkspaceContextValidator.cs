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
    /// On every token request (authorization_code, refresh_token) the validator:
    ///   1. Reads tenant_id / company_id / workspace_context_type from the raw request.
    ///   2. Validates membership and access rights in UserMemberships.
    ///   3. Populates IWorkspaceContextAccessor so CustomProfileService can emit claims.
    ///   4. Creates/revokes WorkspaceSession rows for session exclusivity.
    ///
    /// Controlled by the "EnableWorkspaceContext" feature flag. When false the validator
    /// is a no-op and the legacy path in CustomProfileService takes over.
    /// </summary>
    public class WorkspaceContextValidator : ICustomTokenRequestValidator
    {
        private readonly UserDbContext _db;
        private readonly IWorkspaceContextAccessor _accessor;
        private readonly IWorkspaceSessionStore _sessionStore;
        private readonly IAuditService _audit;
        private readonly ILogger<WorkspaceContextValidator> _logger;
        private readonly bool _enabled;

        public WorkspaceContextValidator(
            UserDbContext db,
            IWorkspaceContextAccessor accessor,
            IWorkspaceSessionStore sessionStore,
            IAuditService audit,
            IConfiguration configuration,
            ILogger<WorkspaceContextValidator> logger)
        {
            _db = db;
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

            // tenant_id is mandatory
            if (string.IsNullOrWhiteSpace(tenantIdStr) || !Guid.TryParse(tenantIdStr, out var tenantId))
            {
                Fail(context, "tenant_id is required and must be a valid GUID.");
                return;
            }

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

            // --- Validate tenant exists and is Active ---
            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId && t.IsActive);

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

        private static void Fail(CustomTokenRequestValidationContext context, string error)
        {
            context.Result.IsError = true;
            context.Result.Error = error;
        }
    }
}
