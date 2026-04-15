using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static Duende.IdentityServer.IdentityServerConstants;

namespace Codx.Auth.Controllers.API
{
    [Route("api/v1/tenants")]
    [Authorize(LocalApi.PolicyName)]
    [ApiController]
    public class TenantApiController : ControllerBase
    {
        private readonly UserDbContext _db;

        public TenantApiController(UserDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Returns all active workspace memberships for the authenticated user.
        /// Source of truth for determining which workspaces/tenants a user may access.
        /// Called after initial login (no workspace context required) so the SPA can
        /// present a workspace selector to the user.
        /// </summary>
        [HttpGet("/api/v1/memberships")]
        public async Task<IActionResult> GetMemberships()
        {
            var subClaim = User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(subClaim, out var userId))
                return Unauthorized();

            var memberships = await _db.UserMemberships
                .Where(m => m.UserId == userId && m.Status == "Active")
                .Include(m => m.Tenant)
                .Include(m => m.Company)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition)
                .AsNoTracking()
                .ToListAsync();

            var result = memberships.Select(m => new
            {
                membershipId = m.Id,
                tenantId = m.TenantId,
                tenantName = m.Tenant?.Name,
                companyId = m.CompanyId,
                companyName = m.Company?.Name,
                contextType = m.CompanyId.HasValue ? "company" : "tenant",
                joinedAt = m.JoinedAt,
                roles = m.MembershipRoles
                    .Where(r => r.RoleDefinition != null)
                    .Select(r => new
                    {
                        code = r.RoleDefinition.Code,
                        displayName = r.RoleDefinition.DisplayName,
                        scopeType = r.RoleDefinition.ScopeType
                    })
            });

            return Ok(result);
        }

        /// <summary>
        /// Returns the workspace context embedded in the current bearer token.
        /// The SPA calls this after a page refresh to restore workspace state without
        /// re-parsing JWT claims directly in JavaScript.
        ///
        /// Returns 204 when the token carries no workspace context (Phase-1 / initial
        /// login token) so the SPA knows to redirect to the workspace selection page.
        /// </summary>
        [HttpGet("/api/v1/workspaces/active")]
        public IActionResult GetActiveWorkspace()
        {
            var tenantIdStr = User.FindFirst("tenant_id")?.Value;
            var contextType = User.FindFirst("workspace_context_type")?.Value;

            // No workspace context in this token — caller should redirect to workspace selection.
            if (string.IsNullOrEmpty(tenantIdStr) || string.IsNullOrEmpty(contextType))
                return NoContent();

            if (!Guid.TryParse(tenantIdStr, out var tenantId))
                return BadRequest(new { error = "malformed_token", detail = "tenant_id claim is not a valid GUID" });

            Guid? companyId = null;
            var companyIdStr = User.FindFirst("company_id")?.Value;
            if (!string.IsNullOrEmpty(companyIdStr) && Guid.TryParse(companyIdStr, out var parsedCompanyId))
                companyId = parsedCompanyId;

            var membershipIdStr = User.FindFirst("membership_id")?.Value;
            Guid.TryParse(membershipIdStr, out var membershipId);

            var sessionIdStr = User.FindFirst("workspace_session_id")?.Value;
            Guid.TryParse(sessionIdStr, out var sessionId);

            var workspaceRoles = User.FindAll("workspace_role")
                .Select(c => c.Value)
                .ToList();

            return Ok(new
            {
                tenantId,
                companyId,
                contextType,
                membershipId,
                sessionId,
                roles = workspaceRoles
            });
        }
    }
}

