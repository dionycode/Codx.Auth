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
    public class ApiResourcePropertiesController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        public ApiResourcePropertiesController(IdentityServerDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetApiResourcePropertiesTableData(int apiresourceid, string search, string sort, string order, int offset, int limit)
        {
            var query = _dbContext.ApiResourceProperties.Where(o => o.ApiResourceId == apiresourceid);

            var data = query.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(apires => new ApiResourcePropertyDetailsViewModel
            {
                Id = apires.Id,
                Key = apires.Key,
                Value = apires.Value,
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
            var record = _dbContext.ApiResourceProperties.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ApiResourcePropertyDetailsViewModel>(record);

            return View(viewmodel);
        }


        [HttpGet]
        public IActionResult Add(int id)
        {
            var viewmodel = new ApiResourcePropertyAddViewModel
            {
                ApiResourceId = id,
            };
            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(ApiResourcePropertyAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<ApiResourceProperty>(viewmodel);
                _dbContext.ApiResourceProperties.Add(record);

                var result = await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction("Details", "ApiResources", new { id = viewmodel.ApiResourceId });
                }

                ModelState.AddModelError("", "Failed");

            }

            return View(viewmodel);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var record = _dbContext.ApiResourceProperties.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ApiResourcePropertyEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ApiResourcePropertyEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ApiResourceProperties.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ApiResourceProperty>(viewmodel);

                _dbContext.Update(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "ApiResources", new { id = viewmodel.ApiResourceId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var record = _dbContext.ApiResourceProperties.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ApiResourcePropertyEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(ApiResourcePropertyEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ApiResourceProperties.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ApiResourceProperty>(viewmodel);
                _dbContext.Remove(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "ApiResources", new { id = viewmodel.ApiResourceId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }
    }
}
