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
    public class ClientCorsOriginsController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        public ClientCorsOriginsController(IdentityServerDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetClientCorsOriginsTableData(int clientid, string search, string sort, string order, int offset, int limit)
        {
            var query = _dbContext.ClientCorsOrigins.Where(o => o.ClientId == clientid);

            var data = query.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(corsorigin => new ClientCorsOriginDetailsViewModel
            {
                Id = corsorigin.Id,
                Origin = corsorigin.Origin,
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
            var record = _dbContext.ClientCorsOrigins.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientCorsOriginDetailsViewModel>(record);

            return View(viewmodel);
        }


        [HttpGet]
        public IActionResult Add(int id)
        {
            var viewmodel = new ClientCorsOriginAddViewModel
            {
                ClientId = id,
            };
            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(ClientCorsOriginAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<ClientCorsOrigin>(viewmodel);
                record.Id = 0;
                _dbContext.ClientCorsOrigins.Add(record);

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
            var record = _dbContext.ClientCorsOrigins.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientCorsOriginEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ClientCorsOriginEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ClientCorsOrigins.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ClientCorsOrigin>(viewmodel);

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
            var record = _dbContext.ClientCorsOrigins.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientCorsOriginEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(ClientCorsOriginEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ClientCorsOrigins.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ClientCorsOrigin>(viewmodel);
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
