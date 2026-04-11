using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using static Duende.IdentityServer.IdentityServerConstants;

namespace Codx.Auth.Controllers.API
{
    [Route("api/v1/applications/{appId}/user-roles")]
    [Authorize(LocalApi.PolicyName)]
    [ApiController]
    public class ApplicationRoleApiController : ControllerBase
    {
        private readonly UserDbContext _db;

        public ApplicationRoleApiController(UserDbContext db)
        {
            _db = db;
        }

        // GET /api/v1/applications/{appId}/roles — list available role definitions (for building assignment UI)
        [HttpGet("/api/v1/applications/{appId}/roles")]
        public async Task<IActionResult> ListRoleDefinitions(string appId)
        {
            if (!await _db.EnterpriseApplications.AnyAsync(a => a.Id == appId))
                return Problem(detail: $"Application '{appId}' not found.", statusCode: 404);

            var roles = await _db.EnterpriseApplicationRoles
                .Where(r => r.ApplicationId == appId && r.IsActive)
                .Select(r => new { r.Id, r.Name, r.Description, r.IsDefault })
                .ToListAsync();

            return Ok(roles);
        }

        // GET /api/v1/applications/{appId}/user-roles
        [HttpGet]
        public async Task<IActionResult> List(
            string appId,
            [FromQuery] Guid tenantId,
            [FromQuery] Guid? companyId)
        {
            if (!await _db.EnterpriseApplications.AnyAsync(a => a.Id == appId))
                return Problem(detail: $"Application '{appId}' not found.", statusCode: 404);

            var query = _db.UserApplicationRoles
                .Where(uar => uar.ApplicationId == appId && uar.TenantId == tenantId)
                .AsQueryable();

            if (companyId.HasValue)
                query = query.Where(uar => uar.CompanyId == companyId.Value);

            var results = await query
                .Include(uar => uar.Role)
                .Select(uar => new
                {
                    uar.Id,
                    uar.UserId,
                    uar.TenantId,
                    uar.CompanyId,
                    uar.ApplicationId,
                    roleId = uar.RoleId,
                    roleName = uar.Role.Name,
                    uar.AssignedAt
                })
                .ToListAsync();

            return Ok(results);
        }

        // POST /api/v1/applications/{appId}/user-roles
        [HttpPost]
        public async Task<IActionResult> Assign(string appId, [FromBody] AssignUserRoleRequest request)
        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            // Only workspace admins may assign roles to other users
            if (!CallerIsWorkspaceAdmin())
                return Problem(detail: "Only CompanyAdmin or TenantAdmin may assign application roles.", statusCode: 403);

            // Validate appId exists
            if (!await _db.EnterpriseApplications.AnyAsync(a => a.Id == appId))
                return Problem(detail: $"Application '{appId}' not found.", statusCode: 404);

            // Validate roleId belongs to this app and is active
            var role = await _db.EnterpriseApplicationRoles
                .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.ApplicationId == appId && r.IsActive);
            if (role == null)
                return Problem(detail: "Role not found or inactive.", statusCode: 404);

            // Validate companyId belongs to tenantId
            var companyBelongsToTenant = await _db.Companies
                .AnyAsync(c => c.Id == request.CompanyId && c.TenantId == request.TenantId);
            if (!companyBelongsToTenant)
                return Problem(detail: "CompanyId does not belong to the specified TenantId.", statusCode: 400);

            // Validate caller's tenant_id claim matches request tenantId (prevents cross-tenant writes)
            var callerTenantIdStr = User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(callerTenantIdStr, out var callerTenantId) || callerTenantId != request.TenantId)
                return Problem(detail: "Caller's tenant context does not match the requested tenantId.", statusCode: 403);

            var assignerIdStr = User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(assignerIdStr, out var assignerId))
                return Unauthorized();

            var assignment = new UserApplicationRole
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                TenantId = request.TenantId,
                CompanyId = request.CompanyId,
                ApplicationId = appId,
                RoleId = request.RoleId,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = assignerId
            };

            _db.UserApplicationRoles.Add(assignment);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(List), new { appId, tenantId = request.TenantId, companyId = request.CompanyId },
                new { assignment.Id });
        }

        // DELETE /api/v1/applications/{appId}/user-roles/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Revoke(string appId, Guid id)
        {
            // Only workspace admins may revoke roles
            if (!CallerIsWorkspaceAdmin())
                return Problem(detail: "Only CompanyAdmin or TenantAdmin may revoke application roles.", statusCode: 403);

            var assignment = await _db.UserApplicationRoles
                .FirstOrDefaultAsync(uar => uar.Id == id && uar.ApplicationId == appId);

            if (assignment == null)
                return Problem(detail: "Assignment not found.", statusCode: 404);

            // Validate caller's tenant matches the assignment (prevents cross-tenant deletes)
            var callerTenantIdStr = User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(callerTenantIdStr, out var callerTenantId) || callerTenantId != assignment.TenantId)
                return Problem(detail: "Not authorized to modify this assignment.", statusCode: 403);

            _db.UserApplicationRoles.Remove(assignment);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private static readonly HashSet<string> _adminRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "CompanyAdmin", "TenantAdmin", "PlatformAdministrator"
        };

        // Returns true when the caller has at least one qualifying workspace role claim.
        private bool CallerIsWorkspaceAdmin() =>
            User.Claims
                .Where(c => c.Type == "workspace_role")
                .Any(c => _adminRoles.Contains(c.Value));
    }

    public class AssignUserRoleRequest
    {
        [Required]
        public Guid UserId { get; set; }
        [Required]
        public Guid TenantId { get; set; }
        [Required]
        public Guid CompanyId { get; set; }
        [Required]
        public Guid RoleId { get; set; }
    }
}
