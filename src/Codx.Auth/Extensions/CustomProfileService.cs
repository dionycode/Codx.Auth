using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Services;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Codx.Auth.Extensions
{
    public class CustomProfileService : IProfileService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWorkspaceContextAccessor _workspaceContext;
        private readonly ITenantResolver _tenantResolver;
        private readonly IHttpContextAccessor _httpCtx;
        private readonly bool _workspaceContextEnabled;
        private readonly UserDbContext _userDb;
        private readonly ILogger<CustomProfileService> _logger;

        public CustomProfileService(
            UserManager<ApplicationUser> userManager,
            IWorkspaceContextAccessor workspaceContext,
            ITenantResolver tenantResolver,
            IHttpContextAccessor httpCtx,
            IConfiguration configuration,
            UserDbContext userDb,
            ILogger<CustomProfileService> logger)
        {
            _userManager = userManager;
            _workspaceContext = workspaceContext;
            _tenantResolver = tenantResolver;
            _httpCtx = httpCtx;
            _workspaceContextEnabled = configuration.GetValue<bool>("EnableWorkspaceContext");
            _userDb = userDb;
            _logger = logger;
        }

        public async Task GetProfileDataAsync(ProfileDataRequestContext context)
        {
            var user = await _userManager.GetUserAsync(context.Subject);
            if (user == null) return;

            var claims = new List<Claim>();

            bool isIdToken = context.Caller == "ClaimsProviderIdentityToken";
            bool isAccessToken = context.Caller == "ClaimsProviderAccessToken";

            if (isAccessToken)
            {
                // ----------------------------------------------------------------
                // ACCESS TOKEN BRANCH
                // ----------------------------------------------------------------
                if (_workspaceContextEnabled)
                {
                    // WorkspaceContextValidator populates IWorkspaceContextAccessor, but Duende
                    // IdentityServer resolves IProfileService from a different DI scope, so the
                    // in-memory accessor may be empty. When that happens, fall back to looking up
                    // the active WorkspaceSession from the database directly.
                    if (_workspaceContext.TenantId == Guid.Empty)
                    {
                        await ResolveWorkspaceContextFromDbAsync(user, context);
                    }

                    // If still empty after DB lookup, the token lacks workspace context
                    // (Phase 1 — initial login before workspace selection). Skip workspace claims.
                    if (_workspaceContext.TenantId == Guid.Empty)
                    {
                        if (!string.IsNullOrWhiteSpace(user.Email))
                            claims.Add(new Claim("email", user.Email));

                        context.IssuedClaims.AddRange(claims);
                        return;
                    }

                    claims.Add(new Claim("tenant_id", _workspaceContext.TenantId.ToString()));
                    claims.Add(new Claim("membership_id", _workspaceContext.MembershipId.ToString()));
                    claims.Add(new Claim("workspace_context_type", _workspaceContext.WorkspaceContextType ?? "tenant"));

                    if (_workspaceContext.CompanyId.HasValue)
                    {
                        claims.Add(new Claim("company_id", _workspaceContext.CompanyId.Value.ToString()));
                    }

                    if (_workspaceContext.WorkspaceSessionId != Guid.Empty)
                    {
                        claims.Add(new Claim("workspace_session_id", _workspaceContext.WorkspaceSessionId.ToString()));
                    }

                    foreach (var roleCode in _workspaceContext.WorkspaceRoleCodes)
                    {
                        claims.Add(new Claim("workspace_role", roleCode));
                    }

                    // Platform-level roles from AspNetUserRoles
                    var platformRoles = await _userManager.GetRolesAsync(user);
                    claims.AddRange(platformRoles.Select(r => new Claim("platform_role", r)));

                    // Application roles — resolved from UserApplicationRoles scoped to workspace context
                    var appRoles = await ResolveApplicationRolesAsync(user, context);
                    claims.AddRange(appRoles);
                }
                else
                {
                    // Legacy path: resolve tenant/company from DefaultCompanyId
                    var company = user.DefaultCompanyId.HasValue
                        ? _tenantResolver.ResolveCompany(user)
                        : _tenantResolver.ResolveFirstUserCompany(user);

                    if (company != null)
                    {
                        var tenant = company.Tenant;
                        claims.Add(new Claim("tenant_id", tenant?.Id.ToString() ?? string.Empty));
                        claims.Add(new Claim("tenant_name", tenant?.Name ?? string.Empty));
                        claims.Add(new Claim("company_id", company.Id.ToString()));
                        claims.Add(new Claim("company_name", company.Name));
                    }

                    var roles = await _userManager.GetRolesAsync(user);
                    claims.AddRange(roles.Select(r => new Claim("role", r)));
                }

                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    claims.Add(new Claim("email", user.Email));
                }
            }
            else if (isIdToken)
            {
                // ----------------------------------------------------------------
                // ID TOKEN BRANCH — identity/profile claims only
                // ----------------------------------------------------------------
                var subClaim = context.Subject.Claims.FirstOrDefault(c => c.Type == "sub");
                if (subClaim != null) claims.Add(subClaim);

                var userClaims = await _userManager.GetClaimsAsync(user);
                claims.AddRange(userClaims.Where(c => c.Type != "given_name" && c.Type != "family_name"));

                if (!string.IsNullOrWhiteSpace(user.GivenName))
                    claims.Add(new Claim("given_name", user.GivenName));

                if (!string.IsNullOrWhiteSpace(user.FamilyName))
                    claims.Add(new Claim("family_name", user.FamilyName));

                var existing = claims.Select(c => c.Type).ToHashSet();

                if (!string.IsNullOrWhiteSpace(user.Email) && !existing.Contains("email"))
                    claims.Add(new Claim("email", user.Email));

                if (!string.IsNullOrWhiteSpace(user.Email) && !existing.Contains("email_verified"))
                    claims.Add(new Claim("email_verified", "true"));

                if (!existing.Contains("name"))
                {
                    var fullName = BuildDisplayName(user);
                    claims.Add(new Claim("name", string.IsNullOrWhiteSpace(fullName) ? user.UserName : fullName));
                }

                if (!string.IsNullOrWhiteSpace(user.UserName) && !existing.Contains("preferred_username"))
                    claims.Add(new Claim("preferred_username", user.UserName));
            }
            else
            {
                // ----------------------------------------------------------------
                // OTHER CONTEXTS (UserInfo endpoint, etc.)
                // ----------------------------------------------------------------
                var userClaims = await _userManager.GetClaimsAsync(user);
                claims.AddRange(userClaims.Where(c => c.Type != "given_name" && c.Type != "family_name"));

                if (!string.IsNullOrWhiteSpace(user.GivenName))
                    claims.Add(new Claim("given_name", user.GivenName));

                if (!string.IsNullOrWhiteSpace(user.FamilyName))
                    claims.Add(new Claim("family_name", user.FamilyName));

                var existing = claims.Select(c => c.Type).ToHashSet();

                if (!string.IsNullOrWhiteSpace(user.Email) && !existing.Contains("email"))
                    claims.Add(new Claim("email", user.Email));

                if (!string.IsNullOrWhiteSpace(user.Email) && !existing.Contains("email_verified"))
                    claims.Add(new Claim("email_verified", "true"));

                if (!existing.Contains("name"))
                {
                    var fullName = BuildDisplayName(user);
                    claims.Add(new Claim("name", string.IsNullOrWhiteSpace(fullName) ? user.UserName : fullName));
                }

                if (!string.IsNullOrWhiteSpace(user.UserName) && !existing.Contains("preferred_username"))
                    claims.Add(new Claim("preferred_username", user.UserName));

                var roles = await _userManager.GetRolesAsync(user);
                claims.AddRange(roles.Select(r => new Claim("role", r)));
            }

            if (isIdToken || (_workspaceContextEnabled && isAccessToken))
            {
                // For ID tokens, always include all identity claims.
                // For workspace-enabled access tokens, the claims list is explicitly constructed
                // above and must not be filtered by RequestedClaimTypes — workspace infrastructure
                // claims (workspace_role, membership_id, etc.) are not registered as IS4 API
                // resource user claims but must always appear in the access token.
                context.IssuedClaims.AddRange(claims);
            }
            else if (context.RequestedClaimTypes.Any())
            {
                context.IssuedClaims.AddRange(claims.Where(c => context.RequestedClaimTypes.Contains(c.Type)));
            }
            else
            {
                context.IssuedClaims.AddRange(claims);
            }
        }

        public Task IsActiveAsync(IsActiveContext context)
        {
            context.IsActive = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Resolves application-level roles for the given user within the current workspace context.
        /// Requires company context (returns empty if no CompanyId on workspace).
        /// Finds which EnterpriseApplications are associated with the requested API scopes,
        /// then returns the active UserApplicationRole names for that user/tenant/company.
        /// </summary>
        private async Task<IEnumerable<Claim>> ResolveApplicationRolesAsync(
            ApplicationUser user,
            ProfileDataRequestContext context)
        {
            if (!_workspaceContext.CompanyId.HasValue)
                return Enumerable.Empty<Claim>();

            var tenantId = _workspaceContext.TenantId;
            var companyId = _workspaceContext.CompanyId.Value;
            var userId = user.Id;

            // Return all active application role names for this user in the current workspace.
            // We intentionally do not filter by requested scopes here:
            //   - ParsedScopes may be empty at the GetProfileDataAsync stage depending on caller.
            //   - The user should always see every app role they hold in this workspace.
            var roleNames = await _userDb.UserApplicationRoles
                .Where(uar =>
                    uar.UserId == userId &&
                    uar.TenantId == tenantId &&
                    uar.CompanyId == companyId &&
                    uar.Role.IsActive)
                .Select(uar => uar.Role.Name)
                .Distinct()
                .ToListAsync();

            return roleNames.Select(name => new Claim("application_role", name));
        }

        private static string BuildDisplayName(ApplicationUser user)
        {
            var parts = new[] { user.GivenName, user.MiddleName, user.FamilyName }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            return string.Join(" ", parts).Trim();
        }

        /// Populates IWorkspaceContextAccessor from the WorkspaceContextSnapshot that
        /// WorkspaceContextValidator stored in HttpContext.Items during this HTTP request.
        ///
        /// Why not IWorkspaceContextAccessor directly?
        ///   Duende IdentityServer resolves ICustomTokenRequestValidator and IProfileService
        ///   in separate child DI scopes (siblings, not parent-child). The scoped
        ///   IWorkspaceContextAccessor is therefore a DIFFERENT instance in each component.
        ///
        /// Why not a session-ID DB lookup?
        ///   The validator's DbContext (scope A) commits the new WorkspaceSession, but the
        ///   profile service's DbContext (scope B) may not see it yet due to EF identity
        ///   caching or connection-pool visibility gaps under load.
        ///
        /// The WorkspaceContextSnapshot stored in HttpContext.Items contains every field
        /// already resolved by the validator — no DB round-trip is needed.
        ///
        /// Safety net: if the snapshot is absent (e.g. Phase-1 token with no workspace
        /// context, or HttpContext unavailable in tests), fall back to the DB lookup with
        /// OrderByDescending(CreatedAt) so the newest active session always wins.
        /// </summary>
        private async Task ResolveWorkspaceContextFromDbAsync(
            ApplicationUser user,
            ProfileDataRequestContext context)
        {
            // Primary path: read the full snapshot written by WorkspaceContextValidator.
            if (_httpCtx.HttpContext?.Items.TryGetValue(WorkspaceContextKeys.Snapshot, out var snapshotObj) == true
                && snapshotObj is WorkspaceContextSnapshot snapshot)
            {
                _logger.LogDebug(
                    "WorkspaceContext [snapshot]: userId={UserId} tenantId={TenantId} companyId={CompanyId} sessionId={SessionId}",
                    snapshot.UserId, snapshot.TenantId, snapshot.CompanyId, snapshot.WorkspaceSessionId);

                _workspaceContext.UserId = snapshot.UserId;
                _workspaceContext.TenantId = snapshot.TenantId;
                _workspaceContext.CompanyId = snapshot.CompanyId;
                _workspaceContext.MembershipId = snapshot.MembershipId;
                _workspaceContext.WorkspaceContextType = snapshot.WorkspaceContextType;
                _workspaceContext.WorkspaceSessionId = snapshot.WorkspaceSessionId;
                _workspaceContext.WorkspaceRoleCodes = snapshot.WorkspaceRoleCodes;
                return;
            }

            // Fallback: snapshot absent — query the most-recently created active session.
            _logger.LogDebug(
                "WorkspaceContext [db-fallback]: no snapshot in HttpContext.Items (HttpContext={HasHttpCtx}), querying DB for userId={UserId}",
                _httpCtx.HttpContext != null, user.Id);

            var clientId = context.Client?.ClientId;
            if (string.IsNullOrWhiteSpace(clientId)) return;

            var session = await _userDb.WorkspaceSessions
                .AsNoTracking()
                .Where(s =>
                    s.UserId == user.Id &&
                    s.ClientId == clientId &&
                    s.Status == "Active" &&
                    s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (session == null)
            {
                _logger.LogDebug(
                    "WorkspaceContext [db-fallback]: no active session found for userId={UserId} clientId={ClientId}",
                    user.Id, clientId);
                return;
            }

            _logger.LogDebug(
                "WorkspaceContext [db-fallback]: found session {SessionId} tenantId={TenantId} companyId={CompanyId}",
                session.Id, session.TenantId, session.CompanyId);

            var membership = await _userDb.UserMemberships
                .AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.UserId == user.Id &&
                    m.TenantId == session.TenantId &&
                    m.CompanyId == session.CompanyId &&
                    m.Status == "Active");

            if (membership == null) return;

            var roleCodes = await _userDb.UserMembershipRoles
                .AsNoTracking()
                .Where(umr => umr.MembershipId == membership.Id && umr.Status == "Active")
                .Join(_userDb.WorkspaceRoleDefinitions.Where(wrd => wrd.IsActive),
                    umr => umr.RoleId,
                    wrd => wrd.Id,
                    (umr, wrd) => wrd.Code)
                .ToListAsync();

            _workspaceContext.UserId = user.Id;
            _workspaceContext.TenantId = session.TenantId;
            _workspaceContext.CompanyId = session.CompanyId;
            _workspaceContext.MembershipId = membership.Id;
            _workspaceContext.WorkspaceContextType = session.WorkspaceContextType;
            _workspaceContext.WorkspaceSessionId = session.Id;
            _workspaceContext.WorkspaceRoleCodes = roleCodes.AsReadOnly();
        }
    }
}
