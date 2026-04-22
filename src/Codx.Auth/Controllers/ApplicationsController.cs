using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Services;
using Codx.Auth.ViewModels.Applications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    [Route("[controller]")]
    public class ApplicationsController : Controller
    {
        private readonly UserDbContext _db;
        private readonly IdentityServerDbContext _isDb;
        private readonly IAuditService _audit;

        public ApplicationsController(UserDbContext db, IdentityServerDbContext isDb, IAuditService audit)
        {
            _db = db;
            _isDb = isDb;
            _audit = audit;
        }

        // GET /applications
        [HttpGet]
        public async Task<IActionResult> Index(int page = 1)
        {
            const int pageSize = 20;
            var query = _db.EnterpriseApplications.OrderBy(a => a.DisplayName);
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.Page = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);
            return View(items);
        }

        // GET /applications/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> Details(string id)
        {
            var app = await _db.EnterpriseApplications
                .Include(a => a.Roles)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (app == null) return NotFound();

            // Find linked IS4 clients via ClientProperty "application_id"
            var linkedClientIds = await _isDb.ClientProperties
                .Where(p => p.Key == "application_id" && p.Value == id)
                .Select(p => p.Client.ClientId)
                .ToListAsync();

            var allTenants = await _db.Tenants
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .ToListAsync();

            var vm = new ApplicationDetailsViewModel
            {
                Application = app,
                LinkedClientIds = linkedClientIds,
                AllTenants = allTenants
            };

            return View(vm);
        }

        // GET /applications/add
        [HttpGet("add")]
        public IActionResult Add() => View(new ApplicationAddViewModel());

        // POST /applications/add
        [HttpPost("add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(ApplicationAddViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // Validate Id is URL-safe
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.Id, @"^[a-z0-9\-]+$"))
            {
                ModelState.AddModelError(nameof(model.Id), "Application ID must be lowercase letters, numbers, and hyphens only.");
                return View(model);
            }

            if (await _db.EnterpriseApplications.AnyAsync(a => a.Id == model.Id))
            {
                ModelState.AddModelError(nameof(model.Id), "An application with this ID already exists.");
                return View(model);
            }

            var actorIdStr = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(actorIdStr, out var actorId))
                return Forbid();

            var app = new EnterpriseApplication
            {
                Id = model.Id,
                DisplayName = model.DisplayName,
                Description = model.Description,
                AllowSelfRegistration = model.AllowSelfRegistration,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = actorId
            };

            _db.EnterpriseApplications.Add(app);
            await _db.SaveChangesAsync();

            return RedirectToAction("Details", new { id = app.Id });
        }

        // GET /applications/{id}/roles/add
        [HttpGet("{id}/roles/add")]
        public async Task<IActionResult> AddRole(string id)
        {
            var app = await _db.EnterpriseApplications.FindAsync(id);
            if (app == null) return NotFound();

            return View(new ApplicationRoleAddViewModel { ApplicationId = id, ApplicationName = app.DisplayName });
        }

        // POST /applications/{id}/roles/add
        [HttpPost("{id}/roles/add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddRole(string id, ApplicationRoleAddViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var role = new EnterpriseApplicationRole
            {
                Id = Guid.NewGuid(),
                ApplicationId = id,
                Name = model.Name,
                Description = model.Description,
                IsActive = true,
                IsDefault = model.IsDefault,
                CreatedAt = DateTime.UtcNow
            };

            _db.EnterpriseApplicationRoles.Add(role);
            await _db.SaveChangesAsync();

            return RedirectToAction("Details", new { id });
        }

        // POST /applications/{id}/roles/{roleId}/deactivate
        [HttpPost("{id}/roles/{roleId}/deactivate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateRole(string id, Guid roleId)
        {
            var role = await _db.EnterpriseApplicationRoles
                .FirstOrDefaultAsync(r => r.Id == roleId && r.ApplicationId == id);

            if (role == null) return NotFound();

            role.IsActive = false;
            await _db.SaveChangesAsync();

            return RedirectToAction("Details", new { id });
        }

        // GET /applications/{appId}/roles/{roleId}/edit
        [HttpGet("{appId}/roles/{roleId}/edit")]
        public async Task<IActionResult> EditRole(string appId, Guid roleId)
        {
            var role = await _db.EnterpriseApplicationRoles
                .Include(r => r.Application)
                .FirstOrDefaultAsync(r => r.Id == roleId && r.ApplicationId == appId);

            if (role == null) return NotFound();

            return View(new ApplicationRoleEditViewModel
            {
                Id = role.Id,
                ApplicationId = appId,
                ApplicationName = role.Application?.DisplayName,
                Name = role.Name,
                Description = role.Description,
                IsDefault = role.IsDefault,
                IsActive = role.IsActive
            });
        }

        // POST /applications/{appId}/roles/{roleId}/edit
        [HttpPost("{appId}/roles/{roleId}/edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRole(string appId, Guid roleId, ApplicationRoleEditViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var role = await _db.EnterpriseApplicationRoles
                .FirstOrDefaultAsync(r => r.Id == roleId && r.ApplicationId == appId);

            if (role == null) return NotFound();

            // Prevent duplicate name within the same application (excluding self)
            if (await _db.EnterpriseApplicationRoles.AnyAsync(r =>
                    r.ApplicationId == appId &&
                    r.Id != roleId &&
                    r.Name == model.Name))
            {
                ModelState.AddModelError(nameof(model.Name), "A role with this name already exists for this application.");
                return View(model);
            }

            role.Name = model.Name;
            role.Description = model.Description;
            role.IsDefault = model.IsDefault;
            role.IsActive = model.IsActive;

            await _db.SaveChangesAsync();

            return RedirectToAction("Details", new { id = appId });
        }

        // ── User Assignment Tab ──────────────────────────────────────────────────────

        // GET /applications/{appId}/company-members?tenantId=&companyId=
        // Returns users with an active company-scoped membership — used by the admin UI user picker.
        // Cookie-auth (PlatformAdmin) so the browser AJAX call does not need a Bearer token.
        [HttpGet("{appId}/company-members")]
        public async Task<IActionResult> GetCompanyMembers(string appId, Guid tenantId, Guid companyId)
        {
            var companyExists = await _db.Companies
                .AnyAsync(c => c.Id == companyId && c.TenantId == tenantId);
            if (!companyExists)
                return NotFound();

            var members = await _db.UserMemberships
                .Where(m =>
                    m.CompanyId == companyId &&
                    m.TenantId == tenantId &&
                    m.Status == "Active")
                .Include(m => m.User)
                .AsNoTracking()
                .Select(m => new
                {
                    userId = m.UserId,
                    email = m.User.Email,
                    displayName = m.User.UserName ?? m.User.Email
                })
                .Distinct()
                .OrderBy(m => m.email)
                .ToListAsync();

            return Json(members);
        }

        // GET /applications/{appId}/user-assignments?tenantId=&companyId=
        [HttpGet("{appId}/user-assignments")]
        public async Task<IActionResult> UserAssignments(string appId, Guid tenantId, Guid companyId)
        {
            if (!await _db.EnterpriseApplications.AnyAsync(a => a.Id == appId))
                return NotFound();

            var rows = await _db.UserApplicationRoles
                .Where(uar =>
                    uar.ApplicationId == appId &&
                    uar.TenantId == tenantId &&
                    uar.CompanyId == companyId)
                .Include(uar => uar.Role)
                .AsNoTracking()
                .ToListAsync();

            // Collect all referenced user IDs to do a single lookup
            var userIds = rows.Select(r => r.UserId)
                .Union(rows.Select(r => r.AssignedByUserId))
                .Distinct()
                .ToList();

            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email, u.UserName })
                .AsNoTracking()
                .ToDictionaryAsync(u => u.Id);

            var result = rows.Select(r => new UserAssignmentRow
            {
                Id = r.Id,
                UserId = r.UserId,
                UserEmail = users.TryGetValue(r.UserId, out var u) ? u.Email : r.UserId.ToString(),
                UserDisplayName = users.TryGetValue(r.UserId, out var u2) ? (u2.UserName ?? u2.Email) : r.UserId.ToString(),
                RoleId = r.RoleId,
                RoleName = r.Role?.Name,
                AssignedAt = r.AssignedAt,
                AssignedByUserId = r.AssignedByUserId,
                AssignedByEmail = users.TryGetValue(r.AssignedByUserId, out var a) ? a.Email : r.AssignedByUserId.ToString()
            }).ToList();

            return Json(result);
        }

        // POST /applications/{appId}/user-assignments/assign
        [HttpPost("{appId}/user-assignments/assign")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(string appId, AssignUserRoleViewModel model)
        {
            model.AppId = appId;
            if (!ModelState.IsValid)
            {
                TempData["AssignError"] = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return RedirectToAction("Details", new { id = appId });
            }

            // Validate app exists
            if (!await _db.EnterpriseApplications.AnyAsync(a => a.Id == appId))
                return NotFound();

            // Validate role belongs to this app and is active
            var roleExists = await _db.EnterpriseApplicationRoles
                .AnyAsync(r => r.Id == model.RoleId && r.ApplicationId == appId && r.IsActive);
            if (!roleExists)
            {
                TempData["AssignError"] = "Selected role not found or inactive.";
                return RedirectToAction("Details", new { id = appId });
            }

            // Validate company belongs to tenant
            var companyBelongsToTenant = await _db.Companies
                .AnyAsync(c => c.Id == model.CompanyId && c.TenantId == model.TenantId);
            if (!companyBelongsToTenant)
            {
                TempData["AssignError"] = "Company does not belong to the selected tenant.";
                return RedirectToAction("Details", new { id = appId });
            }

            // Check for duplicate assignment
            var duplicate = await _db.UserApplicationRoles.AnyAsync(uar =>
                uar.UserId == model.UserId &&
                uar.TenantId == model.TenantId &&
                uar.CompanyId == model.CompanyId &&
                uar.ApplicationId == appId &&
                uar.RoleId == model.RoleId);
            if (duplicate)
            {
                TempData["AssignError"] = "This role is already assigned to the selected user.";
                return RedirectToAction("Details", new { id = appId });
            }

            var actorIdStr = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(actorIdStr, out var actorId))
                return Forbid();

            var assignment = new UserApplicationRole
            {
                Id = Guid.NewGuid(),
                UserId = model.UserId,
                TenantId = model.TenantId,
                CompanyId = model.CompanyId,
                ApplicationId = appId,
                RoleId = model.RoleId,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = actorId
            };

            _db.UserApplicationRoles.Add(assignment);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                "ApplicationRoleAssigned",
                userId: model.UserId,
                actorUserId: actorId,
                tenantId: model.TenantId,
                companyId: model.CompanyId,
                resourceType: "UserApplicationRole",
                resourceId: assignment.Id.ToString());

            TempData["AssignSuccess"] = "Role assigned successfully.";
            return RedirectToAction("Details", new { id = appId });
        }

        // POST /applications/{appId}/user-assignments/{id}/remove
        [HttpPost("{appId}/user-assignments/{id}/remove")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAssignment(string appId, Guid id)
        {
            var assignment = await _db.UserApplicationRoles
                .FirstOrDefaultAsync(uar => uar.Id == id && uar.ApplicationId == appId);

            if (assignment == null) return NotFound();

            var actorIdStr = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid.TryParse(actorIdStr, out var actorId);

            _db.UserApplicationRoles.Remove(assignment);
            await _db.SaveChangesAsync();

            await _audit.LogAsync(
                "ApplicationRoleRevoked",
                userId: assignment.UserId,
                actorUserId: actorId,
                tenantId: assignment.TenantId,
                companyId: assignment.CompanyId,
                resourceType: "UserApplicationRole",
                resourceId: id.ToString());

            TempData["AssignSuccess"] = "Role assignment removed.";
            return RedirectToAction("Details", new { id = appId });
        }
    }
}
