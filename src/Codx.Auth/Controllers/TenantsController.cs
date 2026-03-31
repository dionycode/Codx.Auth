using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Extensions;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class TenantsController : Controller
    {
        private readonly UserDbContext _context;
        protected readonly IMapper _mapper;
        public TenantsController(UserDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult GetTenantsTableData(string search, string sort, string order, int offset, int limit)
        {
            var tenants = _context.Tenants.Where(o => !o.IsDeleted);
            var tenantList = tenants.OrderBy(o => o.Name).Skip(offset).Take(limit).ToList();
            return Json(new
            {
                total = tenants.Count(),
                rows = tenantList
            });
        }

        [HttpGet]
        public IActionResult Details(Guid id) 
        {
            var record = _context.Tenants.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<TenantDetailsViewModel>(record);

            return View(viewModel);
        }

        [HttpGet]
        public IActionResult Add()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(TenantAddViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var userId = User.GetUserId();

                var record = _mapper.Map<Tenant>(viewModel);
                record.IsActive = true;
                record.IsDeleted = false;
                record.CreatedBy = userId;
                record.CreatedAt = DateTime.Now;
                      
                await _context.Tenants.AddAsync(record).ConfigureAwait(false);
                var result = await _context.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction(nameof(Details), new { id = record.Id });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);

        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var record = _context.Tenants.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<TenantEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TenantEditViewModel viewModel)
        {
            var isRecordFound = await _context.Tenants.AsNoTracking().AnyAsync(u => u.Id == viewModel.Id && !u.IsDeleted);

            if (ModelState.IsValid && isRecordFound)
            {
                var userId = User.GetUserId();

                var record = _mapper.Map<Tenant>(viewModel);
                record.UpdatedAt = DateTime.Now;
                record.UpdatedBy = userId;

                _context.Tenants.Update(record);
                var result = await _context.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction(nameof(Details), new { id = record.Id });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid id)
        {
            var record = _context.Tenants.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<TenantEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(TenantEditViewModel viewModel)
        {
            var isRecordFound = _context.Tenants.Any(o => o.Id == viewModel.Id && !o.IsDeleted);
            if (ModelState.IsValid && isRecordFound)
            {
                var userId = User.GetUserId();
                var now = DateTime.Now;

                // Soft-delete the tenant
                var record = _context.Tenants.FirstOrDefault(o => o.Id == viewModel.Id && !o.IsDeleted);
                record.IsDeleted = true;
                record.IsActive = false;
                record.Status = "Cancelled";
                record.UpdatedAt = now;
                record.UpdatedBy = userId;
                _context.Tenants.Update(record);

                // Soft-delete all companies under this tenant
                var companies = await _context.Companies
                    .Where(c => c.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                foreach (var company in companies)
                {
                    company.IsDeleted = true;
                    company.IsActive = false;
                    company.Status = "Cancelled";
                    company.UpdatedAt = now;
                    company.UpdatedBy = userId;
                }

                // Remove tenant manager assignments (no soft-delete fields)
                var tenantManagers = await _context.TenantManagers
                    .Where(tm => tm.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                _context.TenantManagers.RemoveRange(tenantManagers);

                // Deactivate all memberships and their roles
                var memberships = await _context.UserMemberships
                    .Include(m => m.MembershipRoles)
                    .Where(m => m.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                foreach (var membership in memberships)
                {
                    membership.Status = "Inactive";
                    foreach (var role in membership.MembershipRoles)
                    {
                        role.Status = "Inactive";
                    }
                }

                // Revoke pending invitations
                var pendingInvitations = await _context.Invitations
                    .Where(i => i.TenantId == viewModel.Id && i.Status == "Pending")
                    .ToListAsync().ConfigureAwait(false);
                foreach (var invitation in pendingInvitations)
                {
                    invitation.Status = "Revoked";
                }

                // Revoke active workspace sessions
                var activeSessions = await _context.WorkspaceSessions
                    .Where(s => s.TenantId == viewModel.Id && s.Status == "Active")
                    .ToListAsync().ConfigureAwait(false);
                foreach (var session in activeSessions)
                {
                    session.Status = "Revoked";
                }

                // Remove application role assignments (no soft-delete fields)
                var appRoles = await _context.UserApplicationRoles
                    .Where(r => r.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                _context.UserApplicationRoles.RemoveRange(appRoles);

                var result = await _context.SaveChangesAsync().ConfigureAwait(false);
                if (result > 0)
                {
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }
    }
}
