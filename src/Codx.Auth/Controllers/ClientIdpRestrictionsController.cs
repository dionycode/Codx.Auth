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
    public class ClientIdpRestrictionsController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        public ClientIdpRestrictionsController(IdentityServerDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetClientIdpRestrictionsTableData(int clientid, string search, string sort, string order, int offset, int limit)
        {
            var query = _dbContext.ClientIdPRestrictions.Where(o => o.ClientId == clientid);

            var data = query.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(idp => new ClientIdpRestrictionDetailsViewModel
            {
                Id = idp.Id,
                Provider = idp.Provider,
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
            var record = _dbContext.ClientIdPRestrictions.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientIdpRestrictionDetailsViewModel>(record);

            return View(viewmodel);
        }


        [HttpGet]
        public IActionResult Add(int id)
        {
            var viewmodel = new ClientIdpRestrictionAddViewModel
            {
                ClientId = id,
            };
            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(ClientIdpRestrictionAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<ClientIdPRestriction>(viewmodel);
                record.Id = 0;
                _dbContext.ClientIdPRestrictions.Add(record);

                var result = await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction("Details", "Clients", new { id = viewmodel.ClientId });
                }

                ModelState.AddModelError("", "Failed");

            }

            return View(viewmodel);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var record = _dbContext.ClientIdPRestrictions.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientIdpRestrictionEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ClientIdpRestrictionEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ClientIdPRestrictions.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ClientIdPRestriction>(viewmodel);

                _dbContext.Update(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "Clients", new { id = viewmodel.ClientId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var record = _dbContext.ClientIdPRestrictions.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientIdpRestrictionEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(ClientIdpRestrictionEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ClientIdPRestrictions.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ClientIdPRestriction>(viewmodel);
                _dbContext.Remove(record);
                var result = await _dbContext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "Clients", new { id = viewmodel.ClientId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }
    }
}
