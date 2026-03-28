using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public class MembershipsController : Controller
    {
        private readonly UserDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public MembershipsController(UserDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // GET /memberships?tenantId=...&companyId=...
        [HttpGet]
        public async Task<IActionResult> Index(Guid tenantId, Guid? companyId, int page = 1)
        {
            if (tenantId == Guid.Empty)
                return BadRequest("tenantId is required.");

            if (!IsAuthorizedForTenant(tenantId, companyId))
                return Forbid();

            const int pageSize = 20;
            var query = _db.UserMemberships
                .Where(m => m.TenantId == tenantId && (companyId == null || m.CompanyId == companyId))
                .Include(m => m.User)
                .Include(m => m.Tenant)
                .Include(m => m.Company)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition)
                .OrderByDescending(m => m.JoinedAt);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.TenantId = tenantId;
            ViewBag.CompanyId = companyId;
            ViewBag.Page = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);

            return View(items);
        }

        // GET /memberships/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> Details(Guid id)
        {
            var membership = await _db.UserMemberships
                .Include(m => m.User)
                .Include(m => m.Tenant)
                .Include(m => m.Company)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (membership == null) return NotFound();

            if (!IsAuthorizedForTenant(membership.TenantId, membership.CompanyId))
                return Forbid();

            var availableRoles = await _db.WorkspaceRoleDefinitions
                .Where(r => r.IsActive)
                .ToListAsync();

            ViewBag.AvailableRoles = availableRoles;
            return View(membership);
        }

        // POST /memberships/{id}/deactivate
        [HttpPost("{id}/deactivate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(Guid id, Guid tenantId, Guid? companyId)
        {
            var membership = await _db.UserMemberships.FindAsync(id);
            if (membership == null) return NotFound();

            if (!IsAuthorizedForTenant(membership.TenantId, membership.CompanyId))
                return Forbid();

            membership.Status = "Inactive";
            await _db.SaveChangesAsync();

            TempData["Success"] = "Membership deactivated.";
            return RedirectToAction("Details", new { id });
        }

        // POST /memberships/{id}/reactivate
        [HttpPost("{id}/reactivate")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reactivate(Guid id)
        {
            var membership = await _db.UserMemberships
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (membership == null) return NotFound();

            if (!IsAuthorizedForTenant(membership.TenantId, membership.CompanyId))
                return Forbid();

            // TenantOwners cannot reactivate a TENANT_OWNER membership.
            bool isPlatformAdmin = User.IsInRole("PlatformAdministrator");
            if (!isPlatformAdmin)
            {
                bool hasTenantOwnerRole = membership.MembershipRoles
                    .Any(r => r.RoleDefinition?.Code == "TENANT_OWNER");
                if (hasTenantOwnerRole)
                    return Forbid();
            }

            membership.Status = "Active";
            await _db.SaveChangesAsync();

            TempData["Success"] = "Membership reactivated.";
            return RedirectToAction("Details", new { id });
        }

        // POST /memberships/{id}/roles/add
        [HttpPost("{id}/roles/add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddRole(Guid id, int roleId)
        {
            var membership = await _db.UserMemberships.FindAsync(id);
            if (membership == null) return NotFound();

            if (!IsAuthorizedForTenant(membership.TenantId, membership.CompanyId))
                return Forbid();

            var roleDef = await _db.WorkspaceRoleDefinitions.FindAsync(roleId);
            if (roleDef == null) return NotFound();

            // Validate scope type matches membership scope
            bool isMembershipCompanyScoped = membership.CompanyId.HasValue;
            if (roleDef.ScopeType == "Company" && !isMembershipCompanyScoped)
                return BadRequest("Cannot assign a Company-scoped role to a Tenant-scoped membership.");
            if (roleDef.ScopeType == "Tenant" && isMembershipCompanyScoped)
                return BadRequest("Cannot assign a Tenant-scoped role to a Company-scoped membership.");

            // Avoid duplicates
            var exists = await _db.UserMembershipRoles
                .AnyAsync(r => r.MembershipId == id && r.RoleId == roleId);

            if (!exists)
            {
                var actorIdStr = User.FindFirst("sub")?.Value;
                Guid.TryParse(actorIdStr, out var actorId);
                _db.UserMembershipRoles.Add(new Data.Entities.Enterprise.UserMembershipRole
                {
                    Id = Guid.NewGuid(),
                    MembershipId = id,
                    RoleId = roleId,
                    Status = "Active",
                    AssignedAt = DateTime.UtcNow,
                    AssignedByUserId = actorId
                });
                await _db.SaveChangesAsync();
            }

            return RedirectToAction("Details", new { id });
        }

        // POST /memberships/{id}/roles/{roleId}/revoke
        [HttpPost("{id}/roles/{roleId}/revoke")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeRole(Guid id, Guid roleId)
        {
            var role = await _db.UserMembershipRoles
                .FirstOrDefaultAsync(r => r.Id == roleId && r.MembershipId == id);

            if (role == null) return NotFound();

            var membership = await _db.UserMemberships.FindAsync(role.MembershipId);
            if (membership != null && !IsAuthorizedForTenant(membership.TenantId, membership.CompanyId))
                return Forbid();

            _db.UserMembershipRoles.Remove(role);
            await _db.SaveChangesAsync();

            return RedirectToAction("Details", new { id });
        }

        // GET /memberships/GetTenantMembershipsTableData?tenantId=...
        [HttpGet("GetTenantMembershipsTableData")]
        public IActionResult GetTenantMembershipsTableData(Guid tenantId, string search, string sort, string order, int offset, int limit)
        {
            if (tenantId == Guid.Empty)
                return BadRequest("tenantId is required.");

            if (!IsAuthorizedForTenant(tenantId, null))
                return Forbid();

            var query = _db.UserMemberships
                .Where(m => m.TenantId == tenantId && m.CompanyId == null && m.Status == "Active")
                .Include(m => m.User)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition);

            var total = query.Count();
            var data = query.OrderBy(m => m.User.Email).Skip(offset).Take(limit).ToList();
            var rows = data.Select(m => new
            {
                membershipId = m.Id,
                userEmail = m.User.Email,
                userName = m.User.UserName,
                roles = string.Join(", ", m.MembershipRoles
                    .Where(r => r.Status == "Active")
                    .Select(r => r.RoleDefinition.DisplayName)),
                joinedAt = m.JoinedAt
            }).ToList();

            return Json(new { total, rows });
        }

        // GET /memberships/GetCompanyMembershipsTableData?tenantId=...&companyId=...
        [HttpGet("GetCompanyMembershipsTableData")]
        public IActionResult GetCompanyMembershipsTableData(Guid tenantId, Guid companyId, string search, string sort, string order, int offset, int limit)
        {
            if (tenantId == Guid.Empty)
                return BadRequest("tenantId is required.");
            if (companyId == Guid.Empty)
                return BadRequest("companyId is required.");

            if (!IsAuthorizedForTenant(tenantId, companyId))
                return Forbid();

            var query = _db.UserMemberships
                .Where(m => m.TenantId == tenantId && m.CompanyId == companyId && m.Status == "Active")
                .Include(m => m.User)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition);

            var total = query.Count();
            var data = query.OrderBy(m => m.User.Email).Skip(offset).Take(limit).ToList();
            var rows = data.Select(m => new
            {
                membershipId = m.Id,
                userEmail = m.User.Email,
                userName = m.User.UserName,
                roles = string.Join(", ", m.MembershipRoles
                    .Where(r => r.Status == "Active")
                    .Select(r => r.RoleDefinition.DisplayName)),
                joinedAt = m.JoinedAt
            }).ToList();

            return Json(new { total, rows });
        }

        // GET /memberships/GetTenantOwnersTableData?tenantId=...
        [HttpGet("GetTenantOwnersTableData")]
        public IActionResult GetTenantOwnersTableData(Guid tenantId, string search, string sort, string order, int offset, int limit)
        {
            if (tenantId == Guid.Empty)
                return BadRequest("tenantId is required.");

            if (!IsAuthorizedForTenant(tenantId, null))
                return Forbid();

            const string ownerCode = "TENANT_OWNER";
            var query = _db.UserMemberships
                .Where(m => m.TenantId == tenantId
                            && m.CompanyId == null
                            && m.Status == "Active"
                            && m.MembershipRoles.Any(r => r.RoleDefinition.Code == ownerCode && r.Status == "Active"))
                .Include(m => m.User);

            var total = query.Count();
            var rows = query
                .OrderBy(m => m.User.Email)
                .Skip(offset)
                .Take(limit)
                .Select(m => new
                {
                    memberId = m.Id,
                    userEmail = m.User.Email,
                    userId = m.UserId,
                    joinedAt = m.JoinedAt
                })
                .ToList();

            return Json(new { total, rows });
        }

        // GET /memberships/create?tenantId=...&companyId=...&tenantScopeOnly=true
        [HttpGet("create")]
        public async Task<IActionResult> Create(Guid tenantId, Guid? companyId, bool tenantScopeOnly = false)
        {
            if (tenantId == Guid.Empty)
                return BadRequest("tenantId is required.");

            bool isPlatformAdmin = User.IsInRole("PlatformAdministrator");
            bool isTenantOwner = !isPlatformAdmin && IsTenantOwnerForTenant(tenantId);

            if (!isPlatformAdmin && !isTenantOwner && !IsAuthorizedForTenant(tenantId, companyId))
                return Forbid();

            // TenantOwners may only grant tenant-scoped roles; always force the flag for them.
            if (isTenantOwner && !companyId.HasValue) tenantScopeOnly = true;

            var rolesQuery = _db.WorkspaceRoleDefinitions.Where(r => r.IsActive);
            if (companyId.HasValue)
                rolesQuery = rolesQuery.Where(r => r.ScopeType == "Company");
            else if (isTenantOwner)
                rolesQuery = rolesQuery.Where(r => r.Code == "TENANT_ADMIN" || r.Code == "TENANT_MANAGER");
            else if (tenantScopeOnly)
                rolesQuery = rolesQuery.Where(r => r.ScopeType == "Tenant");

            var availableRoles = await rolesQuery
                .OrderBy(r => r.DisplayName)
                .ToListAsync();

            ViewBag.AvailableRoles = availableRoles;

            return View(new MembershipCreateViewModel { TenantId = tenantId, CompanyId = companyId, TenantScopeOnly = tenantScopeOnly });
        }

        // POST /memberships/create
        [HttpPost("create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MembershipCreateViewModel viewModel, string action)
        {
            bool isPlatformAdmin = User.IsInRole("PlatformAdministrator");
            bool isTenantOwner = !isPlatformAdmin && IsTenantOwnerForTenant(viewModel.TenantId);

            if (!isPlatformAdmin && !isTenantOwner && !IsAuthorizedForTenant(viewModel.TenantId, viewModel.CompanyId))
                return Forbid();

            if (isTenantOwner && !viewModel.CompanyId.HasValue) viewModel.TenantScopeOnly = true;

            var rolesQuery = _db.WorkspaceRoleDefinitions.Where(r => r.IsActive);
            if (viewModel.CompanyId.HasValue)
                rolesQuery = rolesQuery.Where(r => r.ScopeType == "Company");
            else if (isTenantOwner)
                rolesQuery = rolesQuery.Where(r => r.Code == "TENANT_ADMIN" || r.Code == "TENANT_MANAGER");
            else if (viewModel.TenantScopeOnly)
                rolesQuery = rolesQuery.Where(r => r.ScopeType == "Tenant");

            var availableRoles = await rolesQuery
                .OrderBy(r => r.DisplayName)
                .ToListAsync();
            ViewBag.AvailableRoles = availableRoles;

            if (action == "Search")
            {
                if (!string.IsNullOrWhiteSpace(viewModel.UserEmail))
                {
                    var found = await _userManager.FindByEmailAsync(viewModel.UserEmail);
                    if (found != null)
                    {
                        viewModel.UserId = found.Id;
                        viewModel.UserEmail = found.Email;
                        viewModel.UserName = found.UserName;
                        // Remove from ModelState so asp-for renders the updated model values,
                        // not the stale empty values captured during this POST.
                        ModelState.Remove(nameof(viewModel.UserId));
                        ModelState.Remove(nameof(viewModel.UserEmail));
                        ModelState.Remove(nameof(viewModel.UserName));
                    }
                    else
                    {
                        ModelState.AddModelError("UserEmail", "No user found with that email address.");
                    }
                }
                return View(viewModel);
            }

            if (ModelState.IsValid)
            {
                if (viewModel.UserId == null || viewModel.UserId == Guid.Empty)
                {
                    ModelState.AddModelError("UserEmail", "Please search for and confirm a user before saving.");
                    return View(viewModel);
                }

                if (viewModel.SelectedRoleIds == null || !viewModel.SelectedRoleIds.Any())
                {
                    ModelState.AddModelError("SelectedRoleIds", "At least one role must be selected.");
                    return View(viewModel);
                }

                // Prevent privilege escalation / scope violation: validate submitted roles
                // are within the set that was offered to this caller.
                if (isTenantOwner || viewModel.TenantScopeOnly)
                {
                    var allowedRoleIds = availableRoles.Select(r => r.Id).ToHashSet();
                    if (viewModel.SelectedRoleIds.Any(rid => !allowedRoleIds.Contains(rid)))
                    {
                        ModelState.AddModelError("", "You are not authorized to assign the selected role(s).");
                        return View(viewModel);
                    }
                }

                // When tenant-scope only, enforce that CompanyId is not set.
                if (viewModel.TenantScopeOnly && viewModel.CompanyId.HasValue)
                {
                    ModelState.AddModelError("", "A tenant-scoped membership cannot have a company.");
                    return View(viewModel);
                }

                var alreadyActive = await _db.UserMemberships.AnyAsync(m =>
                    m.UserId == viewModel.UserId.Value
                    && m.TenantId == viewModel.TenantId
                    && m.CompanyId == viewModel.CompanyId
                    && m.Status == "Active");

                if (alreadyActive)
                {
                    ModelState.AddModelError("", "An active membership for this user and scope already exists.");
                    return View(viewModel);
                }

                // If an inactive membership exists, reactivate it and replace its roles.
                var inactiveMembership = await _db.UserMemberships
                    .Include(m => m.MembershipRoles)
                        .ThenInclude(r => r.RoleDefinition)
                    .FirstOrDefaultAsync(m =>
                        m.UserId == viewModel.UserId.Value
                        && m.TenantId == viewModel.TenantId
                        && m.CompanyId == viewModel.CompanyId
                        && m.Status == "Inactive");

                if (inactiveMembership != null)
                {
                    // TenantOwners cannot reactivate a TENANT_OWNER membership.
                    if (!isPlatformAdmin && inactiveMembership.MembershipRoles
                            .Any(r => r.RoleDefinition?.Code == "TENANT_OWNER"))
                    {
                        ModelState.AddModelError("", "You are not authorized to reactivate this membership.");
                        return View(viewModel);
                    }

                    var actorIdStrR = User.FindFirst("sub")?.Value;
                    Guid.TryParse(actorIdStrR, out var actorIdR);

                    inactiveMembership.Status = "Active";
                    _db.UserMembershipRoles.RemoveRange(inactiveMembership.MembershipRoles);
                    foreach (var roleId in viewModel.SelectedRoleIds)
                    {
                        _db.UserMembershipRoles.Add(new UserMembershipRole
                        {
                            Id = Guid.NewGuid(),
                            MembershipId = inactiveMembership.Id,
                            RoleId = roleId,
                            Status = "Active",
                            AssignedAt = DateTime.UtcNow,
                            AssignedByUserId = actorIdR
                        });
                    }
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "Inactive membership has been reactivated with the selected roles.";
                    if (isPlatformAdmin)
                        return RedirectToAction("Details", new { id = inactiveMembership.Id });
                    if (viewModel.CompanyId.HasValue)
                        return RedirectToAction("ManageTenantCompanyDetails", "MyProfile", new { id = viewModel.CompanyId });
                    return RedirectToAction("ManageTenant", "MyProfile", new { id = viewModel.TenantId });
                }

                var actorIdStr = User.FindFirst("sub")?.Value;
                Guid.TryParse(actorIdStr, out var actorId);

                var membership = new UserMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = viewModel.UserId.Value,
                    TenantId = viewModel.TenantId,
                    CompanyId = viewModel.CompanyId,
                    Status = "Active",
                    JoinedAt = DateTime.UtcNow
                };
                _db.UserMemberships.Add(membership);

                foreach (var roleId in viewModel.SelectedRoleIds)
                {
                    _db.UserMembershipRoles.Add(new UserMembershipRole
                    {
                        Id = Guid.NewGuid(),
                        MembershipId = membership.Id,
                        RoleId = roleId,
                        Status = "Active",
                        AssignedAt = DateTime.UtcNow,
                        AssignedByUserId = actorId
                    });
                }

                await _db.SaveChangesAsync();
                TempData["Success"] = "Membership created successfully.";
                if (isPlatformAdmin)
                    return RedirectToAction("Index", new { tenantId = viewModel.TenantId, companyId = viewModel.CompanyId });
                if (viewModel.CompanyId.HasValue)
                    return RedirectToAction("ManageTenantCompanyDetails", "MyProfile", new { id = viewModel.CompanyId });
                return RedirectToAction("ManageTenant", "MyProfile", new { id = viewModel.TenantId });
            }

            return View(viewModel);
        }

        // Returns true when the current user holds an active TENANT_OWNER membership for the tenant.
        private bool IsTenantOwnerForTenant(Guid tenantId)
        {
            var userIdStr = User.FindFirst("sub")?.Value;
            if (!Guid.TryParse(userIdStr, out var userId)) return false;
            return _db.UserMemberships.Any(m =>
                m.UserId == userId
                && m.TenantId == tenantId
                && m.CompanyId == null
                && m.Status == "Active"
                && m.MembershipRoles.Any(r => r.Status == "Active" && r.RoleDefinition.Code == "TENANT_OWNER"));
        }

        // Returns true when the current user is allowed to manage the given tenant/company scope.
        private bool IsAuthorizedForTenant(Guid tenantId, Guid? companyId)
        {
            if (User.IsInRole("PlatformAdministrator")) return true;

            if (IsTenantOwnerForTenant(tenantId)) return true;

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
