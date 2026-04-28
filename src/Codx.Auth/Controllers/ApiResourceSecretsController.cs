using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.ViewModels;
using IdentityModel;
using Duende.IdentityServer.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.AspNetCore.Authorization;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class ApiResourceSecretsController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        public ApiResourceSecretsController(IdentityServerDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetApiResourceSecretsTableData(int apiresourceid, string search, string sort, string order, int offset, int limit)
        {
            var query = _dbContext.ApiResourceSecrets.Where(o => o.ApiResourceId == apiresourceid);
            var data = query.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(apires => new ApiResourceSecretDetailsViewModel
            {
                Id = apires.Id,
                Type = apires.Type,
                Description = apires.Description,
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
            var record = _dbContext.ApiResourceSecrets.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ApiResourceSecretDetailsViewModel>(record);

            return View(viewmodel);
        }


        [HttpGet]
        public IActionResult Add(int id)
        {
            var viewmodel = new ApiResourceSecretAddViewModel
            {
                ApiResourceId = id,
                Type = "SharedSecret"
            };
            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(ApiResourceSecretAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<ApiResourceSecret>(viewmodel);
                record.Value = new Duende.IdentityServer.Models.Secret(viewmodel.NewSecret.ToSha256()).Value;
                record.Created = DateTime.UtcNow;

                _dbContext.ApiResourceSecrets.Add(record);
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
            var record = _dbContext.ApiResourceSecrets.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ApiResourceSecretEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ApiResourceSecretEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ApiResourceSecrets.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ApiResourceSecret>(viewmodel);

                if (!string.IsNullOrEmpty(viewmodel.NewSecret) && !string.IsNullOrWhiteSpace(viewmodel.NewSecret))         
                { 
                    record.Value = new Duende.IdentityServer.Models.Secret(viewmodel.NewSecret.ToSha256()).Value;
                }

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
            var record = _dbContext.ApiResourceSecrets.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ApiResourceSecretEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(ApiResourceSecretEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ApiResourceSecrets.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ApiResourceSecret>(viewmodel);
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
