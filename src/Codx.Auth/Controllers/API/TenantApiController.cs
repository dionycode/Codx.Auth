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
                contextType = m.CompanyId.HasValue ? "Company" : "Tenant",
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
    }
}

