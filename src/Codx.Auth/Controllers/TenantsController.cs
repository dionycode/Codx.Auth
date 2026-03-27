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

        public IActionResult Details(Guid id) 
        {
            var record = _context.Tenants.FirstOrDefault(o => o.Id == id);

            var viewModel = _mapper.Map<TenantDetailsViewModel>(record);

            return View(viewModel);
        }

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

        public async Task<IActionResult> Edit(Guid id)
        {
            var record = _context.Tenants.FirstOrDefault(o => o.Id == id);

            var viewModel = _mapper.Map<TenantEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(TenantEditViewModel viewModel)
        {
            var isRecordFound = await _context.Tenants.AsNoTracking().AnyAsync(u => u.Id == viewModel.Id);

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
            var record = _context.Tenants.FirstOrDefault(o => o.Id == id);

            var viewModel = _mapper.Map<TenantEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(TenantEditViewModel viewModel)
        {
            var isRecordFound = _context.Tenants.Any(o => o.Id == viewModel.Id);
            if (ModelState.IsValid && isRecordFound)
            {
                var record = _context.Tenants.FirstOrDefault(o => o.Id == viewModel.Id);
                record.IsDeleted = true;
                record.IsActive = false;
                record.UpdatedAt = DateTime.Now;
                record.UpdatedBy = User.GetUserId();

                _context.Tenants.Update(record);

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
