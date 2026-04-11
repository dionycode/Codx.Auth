using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Services;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        private readonly bool _workspaceContextEnabled;
        private readonly UserDbContext _userDb;
        private readonly IdentityServerDbContext _isDb;

        public CustomProfileService(
            UserManager<ApplicationUser> userManager,
            IWorkspaceContextAccessor workspaceContext,
            ITenantResolver tenantResolver,
            IConfiguration configuration,
            UserDbContext userDb,
            IdentityServerDbContext isDb)
        {
            _userManager = userManager;
            _workspaceContext = workspaceContext;
            _tenantResolver = tenantResolver;
            _workspaceContextEnabled = configuration.GetValue<bool>("EnableWorkspaceContext");
            _userDb = userDb;
            _isDb = isDb;
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

            if (isIdToken)
            {
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

            // Get the scope names from the current request
            var requestedScopeNames = context.RequestedResources.ParsedScopes
                .Select(s => s.ParsedName)
                .ToList();

            if (!requestedScopeNames.Any())
                return Enumerable.Empty<Claim>();

            // Find ApiResource names whose scopes overlap with requested scopes.
            // ApiResource.Name is used as the EnterpriseApplication.Id.
            var appIds = await _isDb.ApiResourceScopes
                .Where(rs => requestedScopeNames.Contains(rs.Scope))
                .Select(rs => rs.ApiResource.Name)
                .Distinct()
                .ToListAsync();

            if (!appIds.Any())
                return Enumerable.Empty<Claim>();

            var userId = user.Id; // Guid

            var roleNames = await _userDb.UserApplicationRoles
                .Where(uar =>
                    uar.UserId == userId &&
                    uar.TenantId == tenantId &&
                    uar.CompanyId == companyId &&
                    appIds.Contains(uar.ApplicationId) &&
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

        /// <summary>
        /// Populates IWorkspaceContextAccessor from the active WorkspaceSession in the DB.
        /// Used as a fallback when the in-memory accessor is empty because Duende IdentityServer
        /// resolves IProfileService in a separate DI scope from ICustomTokenRequestValidator.
        /// </summary>
        private async Task ResolveWorkspaceContextFromDbAsync(
            ApplicationUser user,
            ProfileDataRequestContext context)
        {
            var clientId = context.Client?.ClientId;
            if (string.IsNullOrWhiteSpace(clientId)) return;

            var session = await _userDb.WorkspaceSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s =>
                    s.UserId == user.Id &&
                    s.ClientId == clientId &&
                    s.Status == "Active" &&
                    s.ExpiresAt > DateTime.UtcNow);

            if (session == null) return;

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
