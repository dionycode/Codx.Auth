using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
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

        public ApplicationsController(UserDbContext db, IdentityServerDbContext isDb)
        {
            _db = db;
            _isDb = isDb;
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

            var vm = new ApplicationDetailsViewModel
            {
                Application = app,
                LinkedClientIds = linkedClientIds
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
    }
}
