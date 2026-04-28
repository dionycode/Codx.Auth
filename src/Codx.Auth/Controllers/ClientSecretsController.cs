using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.ViewModels;
using Duende.IdentityServer.EntityFramework.Entities;
using IdentityModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class ClientSecretsController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        public ClientSecretsController(IdentityServerDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetClientSecretsTableData(int clientid, string search, string sort, string order, int offset, int limit)
        {
            var query = _dbContext.ClientSecrets.Where(o => o.ClientId == clientid);

            var data = query.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(secret => new ClientSecretDetailsViewModel
            {
                Id = secret.Id,
                Type = secret.Type,
                Description = secret.Description,
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
            var record = _dbContext.ClientSecrets.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientSecretDetailsViewModel>(record);

            return View(viewmodel);
        }


        [HttpGet]
        public IActionResult Add(int id)
        {
            var viewmodel = new ClientSecretAddViewModel
            {
                ClientId = id,
                Type = "SharedSecret"
            };
            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(ClientSecretAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<ClientSecret>(viewmodel);
                record.Id = 0;
                record.Value = new Duende.IdentityServer.Models.Secret(viewmodel.NewSecret.ToSha256()).Value;
                _dbContext.ClientSecrets.Add(record);

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
            var record = _dbContext.ClientSecrets.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientSecretEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ClientSecretEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ClientSecrets.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ClientSecret>(viewmodel);
                if (!string.IsNullOrEmpty(viewmodel.NewSecret) && !string.IsNullOrWhiteSpace(viewmodel.NewSecret))
                {
                    record.Value = new Duende.IdentityServer.Models.Secret(viewmodel.NewSecret.ToSha256()).Value;
                }

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
            var record = _dbContext.ClientSecrets.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientSecretEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(ClientSecretEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.ClientSecrets.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ClientSecret>(viewmodel);
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
