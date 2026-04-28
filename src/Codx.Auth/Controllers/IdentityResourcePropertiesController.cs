using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.ViewModels;
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
    public class IdentityResourcePropertiesController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        public IdentityResourcePropertiesController(IdentityServerDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetIdentityResourcePropertiesTableData(int identityresourceid, string search, string sort, string order, int offset, int limit)
        {
            var query = _dbContext.IdentityResourceProperties.Where(o => o.IdentityResourceId == identityresourceid);

            var data = query.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(identityres => new IdentityResourcePropertyDetailsViewModel
            {
                Id = identityres.Id,
                Key = identityres.Key,
                Value = identityres.Value,
            }).ToList();

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }


        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var record = _dbContext.IdentityResourceProperties.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<IdentityResourcePropertyDetailsViewModel>(record);

            return View(viewmodel);
        }


        [HttpGet]
        public IActionResult Add(int id)
        {
            var viewmodel = new IdentityResourcePropertyAddViewModel
            {
                IdentityResourceId = id,
            };
            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(IdentityResourcePropertyAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<IdentityResourceProperty>(viewmodel);
                _dbContext.IdentityResourceProperties.Add(record);

                var result = await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction("Details", "IdentityResources", new { id = viewmodel.IdentityResourceId });
                }

                ModelState.AddModelError("", "Failed");

            }

            return View(viewmodel);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var record = _dbContext.IdentityResourceProperties.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<IdentityResourcePropertyEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(IdentityResourcePropertyEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.IdentityResourceProperties.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<IdentityResourceProperty>(viewmodel);

                _dbContext.Update(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "IdentityResources", new { id = viewmodel.IdentityResourceId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var record = _dbContext.IdentityResourceProperties.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<IdentityResourcePropertyEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(IdentityResourcePropertyEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.IdentityResourceProperties.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<IdentityResourceProperty>(viewmodel);
                _dbContext.Remove(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "IdentityResources", new { id = viewmodel.IdentityResourceId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }
    }
}
