using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Services;
using Codx.Auth.Services.Interfaces;
using Codx.Auth.ViewModels.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "TenantOrCompanyAdmin")]
    [Route("[controller]")]
    public class InvitationsController : Controller
    {
        private readonly IInvitationService _invitationService;
        private readonly UserDbContext _db;

        public InvitationsController(IInvitationService invitationService, UserDbContext db)
        {
            _invitationService = invitationService;
            _db = db;
        }

        // GET /invitations?tenantId=...&companyId=...&page=1
        [HttpGet]
        public async Task<IActionResult> Index(Guid tenantId, Guid? companyId, int page = 1)
        {
            if (tenantId == Guid.Empty)
                return BadRequest("tenantId is required.");

            if (!IsAuthorizedForTenant(tenantId, companyId))
                return Forbid();

            const int pageSize = 20;
            var query = _db.Invitations
                .Where(i => i.TenantId == tenantId && (companyId == null || i.CompanyId == companyId))
                .OrderByDescending(i => i.CreatedAt);

            var total = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var vm = new InvitationsIndexViewModel
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Invitations = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            };

            return View(vm);
        }

        // GET /invitations/create?tenantId=...&companyId=...
        [HttpGet("create")]
        public async Task<IActionResult> Create(Guid tenantId, Guid? companyId)
        {
            if (tenantId == Guid.Empty)
                return BadRequest("tenantId is required.");

            if (!IsAuthorizedForTenant(tenantId, companyId))
                return Forbid();

            var roles = await _db.WorkspaceRoleDefinitions
                .Where(r => r.IsActive)
                .ToListAsync();

            var vm = new CreateInvitationViewModel
            {
                TenantId = tenantId,
                CompanyId = companyId,
                AvailableRoles = roles
            };

            return View(vm);
        }

        // POST /invitations/create
        [HttpPost("create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateInvitationViewModel model)
        {
            if (!IsAuthorizedForTenant(model.TenantId, model.CompanyId))
                return Forbid();

            if (!ModelState.IsValid)
            {
                model.AvailableRoles = await _db.WorkspaceRoleDefinitions
                    .Where(r => r.IsActive)
                    .ToListAsync();
                return View(model);
            }

            // Derive invitedByUserId from authenticated user
            var invitedByUserIdStr = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(invitedByUserIdStr, out var invitedByUserId))
                return Forbid();

            var (success, error) = await _invitationService.CreateInvitationAsync(
                email: model.Email,
                tenantId: model.TenantId,
                companyId: model.CompanyId,
                roleIds: model.SelectedRoleIds,
                invitedByUserId: invitedByUserId);

            if (!success)
            {
                ModelState.AddModelError(string.Empty, error);
                model.AvailableRoles = await _db.WorkspaceRoleDefinitions
                    .Where(r => r.IsActive)
                    .ToListAsync();
                return View(model);
            }

            TempData["Success"] = $"Invitation sent to {model.Email}.";
            return RedirectToAction("Index", new { tenantId = model.TenantId, companyId = model.CompanyId });
        }

        // POST /invitations/{id}/revoke
        [HttpPost("{id}/revoke")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Revoke(Guid id, Guid tenantId, Guid? companyId)
        {
            if (!IsAuthorizedForTenant(tenantId, companyId))
                return Forbid();

            var revokerIdStr = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(revokerIdStr, out var revokerUserId))
                return Forbid();

            var (success, error) = await _invitationService.RevokeInvitationAsync(id, revokerUserId);
            if (!success)
                TempData["Error"] = error;
            else
                TempData["Success"] = "Invitation revoked.";

            return RedirectToAction("Index", new { tenantId, companyId });
        }

        // Returns true when the current user is allowed to manage the given tenant/company scope.
        private bool IsAuthorizedForTenant(Guid tenantId, Guid? companyId)
        {
            if (User.IsInRole("PlatformAdministrator")) return true;

            var workspaceRole = User.FindFirst("workspace_role")?.Value;

            if (workspaceRole == "TenantAdmin")
            {
                var claimTenantId = User.FindFirst("tenant_id")?.Value;
                return Guid.TryParse(claimTenantId, out var tid) && tid == tenantId;
            }

            if (workspaceRole == "CompanyAdmin" && companyId.HasValue)
            {
                var claimCompanyId = User.FindFirst("company_id")?.Value;
                return Guid.TryParse(claimCompanyId, out var cid) && cid == companyId.Value;
            }

            return false;
        }
    }
}
