using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.ViewModels;
using Duende.IdentityServer.EntityFramework.DbContexts;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class IdentityResourcesController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        public IdentityResourcesController(IdentityServerDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public JsonResult GetIdentityResourcesTableData(string search, string sort, string order, int offset, int limit)
        {
            var idResources = _dbContext.IdentityResources.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = idResources.Select(idres => new IdentityResourceDetailsViewModel
            {
                Id = idres.Id,
                Name = idres.Name,
                DisplayName = idres.DisplayName,
            }).ToList();

            return Json(new
            {
                total = _dbContext.Clients.Count(),
                rows = viewModel
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var record = _dbContext.IdentityResources.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<IdentityResourceDetailsViewModel>(record);

            return View(viewmodel);
        }



        [HttpGet]
        public IActionResult Add()
        {
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(IdentityResourceAddViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<IdentityResource>(viewModel);

                record.Enabled = true;
                record.ShowInDiscoveryDocument = true;
                record.Created = DateTime.UtcNow;

                await _dbContext.AddAsync(record).ConfigureAwait(false);
                var result = await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError("", "Failed");

            }

            return View(viewModel);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var record = _dbContext.IdentityResources.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<IdentityResourceEditViewModel>(record);
           
            return View(viewmodel);
        }


        [HttpPost]
        public async Task<IActionResult> Edit(IdentityResourceEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.IdentityResources.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<IdentityResource>(viewmodel);
                record.Updated = DateTime.UtcNow;

                _dbContext.Update(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var record = _dbContext.IdentityResources.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<IdentityResourceEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(IdentityResourceEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.IdentityResources.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<IdentityResource>(viewmodel);
                _dbContext.Remove(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }


    }
}
