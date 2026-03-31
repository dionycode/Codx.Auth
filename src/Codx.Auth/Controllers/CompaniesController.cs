using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Extensions;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class CompaniesController : Controller
    {
        private readonly UserDbContext _context;
        protected readonly IMapper _mapper;
        public CompaniesController(UserDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public IActionResult GetCompaniesTableData(Guid tenantid, string search, string sort, string order, int offset, int limit)
        {
            var query = _context.Companies.Where(o => !o.IsDeleted && o.TenantId == tenantid);
            var data = query.OrderBy(o => o.Name).Skip(offset).Take(limit).ToList();

            var viewModel = _mapper.Map<List<CompanyDetailsViewModel>>(data);

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }

        public IActionResult Details(Guid id) 
        {
            var record = _context.Companies.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<CompanyDetailsViewModel>(record);

            return View(viewModel);
        }

        public async Task<IActionResult> Add(Guid tenantid)
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(o => o.Id == tenantid && !o.IsDeleted);

            if (tenant == null) return NotFound();

            var viewModel = new CompanyAddViewModel
            {
                TenantId = tenant.Id,
            };
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Add(CompanyAddViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var userId = User.GetUserId();

                var record = _mapper.Map<Company>(viewModel);
                record.IsActive = true;
                record.IsDeleted = false;
                record.CreatedBy = userId;
                record.CreatedAt = DateTime.Now;
                      
                await _context.Companies.AddAsync(record).ConfigureAwait(false);
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
            var record = _context.Companies.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<CompanyEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(CompanyEditViewModel viewModel)
        {
            var isRecordFound = await _context.Companies.AsNoTracking().AnyAsync(u => u.Id == viewModel.Id && !u.IsDeleted);

            if (ModelState.IsValid && isRecordFound)
            {
                var userId = User.GetUserId();

                var record = _mapper.Map<Company>(viewModel);
                record.UpdatedAt = DateTime.Now;
                record.UpdatedBy = userId;

                _context.Companies.Update(record);
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
            var record = _context.Companies.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<CompanyEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(CompanyEditViewModel viewModel)
        {
            var isRecordFound = _context.Companies.Any(o => o.Id == viewModel.Id);
            if (ModelState.IsValid && isRecordFound)
            {
                var record = _context.Companies.FirstOrDefault(o => o.Id == viewModel.Id);
                record.IsDeleted = true;
                record.IsActive = false;
                record.UpdatedAt = DateTime.Now;
                record.UpdatedBy = User.GetUserId();

                _context.Companies.Update(record);

                var result = await _context.SaveChangesAsync().ConfigureAwait(false);
                if (result > 0)
                {
                    return RedirectToAction("Details", "Tenants", new { id = viewModel.TenantId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }
    }
}
