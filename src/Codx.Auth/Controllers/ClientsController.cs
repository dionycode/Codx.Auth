using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.ViewModels;
using Duende.IdentityServer.EntityFramework.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class ClientsController : Controller
    {
        protected readonly IdentityServerDbContext _dbContext;
        protected readonly IMapper _mapper;
        protected readonly UserDbContext _userDb;

        public ClientsController(IdentityServerDbContext dbContext, IMapper mapper, UserDbContext userDb)
        {
            _dbContext = dbContext;
            _mapper = mapper;
            _userDb = userDb;
        }
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public JsonResult GetClientsTableData(string search, string sort, string order, int offset, int limit)
        {
            var apiResources = _dbContext.Clients.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = apiResources.Select(apires => new ClientDetailsViewModel
            {
                Id = apires.Id,
                ClientId = apires.ClientId,
                ClientName = apires.ClientName,
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
            var record = _dbContext.Clients.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientDetailsViewModel>(record);

            return View(viewmodel);
        }

        [HttpGet]
        public async Task<IActionResult> Add()
        {
            var apps = await _userDb.EnterpriseApplications
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayName)
                .Select(a => new { a.Id, a.DisplayName })
                .ToListAsync();
            ViewBag.Applications = apps;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(ClientAddViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var record = new Client
                {
                    ClientId = viewModel.ClientId,
                    ClientName = viewModel.ClientName,
                    Description = viewModel.Description,
                    Enabled = true,
                    Created = DateTime.UtcNow,
                };

                await _dbContext.AddAsync(record).ConfigureAwait(false);
                var result = await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    if (!string.IsNullOrEmpty(viewModel.ApplicationId))
                    {
                        _dbContext.ClientProperties.Add(new ClientProperty
                        {
                            ClientId = record.Id,
                            Key = "application_id",
                            Value = viewModel.ApplicationId
                        });
                    }
                    _dbContext.ClientProperties.Add(new ClientProperty
                    {
                        ClientId = record.Id,
                        Key = "allow_self_registration",
                        Value = viewModel.AllowSelfRegistration ? "true" : "false"
                    });
                    await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                    return RedirectToAction(nameof(Details), new { id = record.Id });
                }

                ModelState.AddModelError("", "Failed");
            }

            var apps = await _userDb.EnterpriseApplications
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayName)
                .Select(a => new { a.Id, a.DisplayName })
                .ToListAsync();
            ViewBag.Applications = apps;
            return View(viewModel);
        }

        // Edit Client
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var record = await _dbContext.Clients.FirstOrDefaultAsync(u => u.Id == id);

            var viewmodel = _mapper.Map<ClientEditViewModel>(record);

            // Load existing ClientProperties
            var props = await _dbContext.ClientProperties
                .Where(p => p.ClientId == id)
                .ToListAsync();
            viewmodel.ApplicationId = props.FirstOrDefault(p => p.Key == "application_id")?.Value;
            viewmodel.AllowSelfRegistration = props.FirstOrDefault(p => p.Key == "allow_self_registration")?.Value == "true";

            var apps = await _userDb.EnterpriseApplications
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayName)
                .Select(a => new { a.Id, a.DisplayName })
                .ToListAsync();
            ViewBag.Applications = apps;

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ClientEditViewModel viewmodel)
        {
            var isRecordFound = await _dbContext.Clients.AnyAsync(u => u.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {                
                var record = _mapper.Map<Client>(viewmodel);

                _dbContext.Update(record);
                var result = await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    // Upsert application_id property
                    var existingAppId = await _dbContext.ClientProperties
                        .FirstOrDefaultAsync(p => p.ClientId == viewmodel.Id && p.Key == "application_id");
                    if (!string.IsNullOrEmpty(viewmodel.ApplicationId))
                    {
                        if (existingAppId == null)
                            _dbContext.ClientProperties.Add(new ClientProperty { ClientId = viewmodel.Id, Key = "application_id", Value = viewmodel.ApplicationId });
                        else
                            existingAppId.Value = viewmodel.ApplicationId;
                    }
                    else if (existingAppId != null)
                    {
                        _dbContext.ClientProperties.Remove(existingAppId);
                    }

                    // Upsert allow_self_registration property
                    var existingSelfReg = await _dbContext.ClientProperties
                        .FirstOrDefaultAsync(p => p.ClientId == viewmodel.Id && p.Key == "allow_self_registration");
                    if (existingSelfReg == null)
                        _dbContext.ClientProperties.Add(new ClientProperty { ClientId = viewmodel.Id, Key = "allow_self_registration", Value = viewmodel.AllowSelfRegistration ? "true" : "false" });
                    else
                        existingSelfReg.Value = viewmodel.AllowSelfRegistration ? "true" : "false";

                    await _dbContext.SaveChangesAsync().ConfigureAwait(false);

                    return RedirectToAction(nameof(Details), new { id = record.Id });
                }

                ModelState.AddModelError("", "Failed");
            }

            var apps = await _userDb.EnterpriseApplications
                .Where(a => a.IsActive)
                .OrderBy(a => a.DisplayName)
                .Select(a => new { a.Id, a.DisplayName })
                .ToListAsync();
            ViewBag.Applications = apps;

            return View(viewmodel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var record = _dbContext.Clients.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<ClientEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(ClientEditViewModel viewmodel)
        {
            var isRecordFound = _dbContext.Clients.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<Client>(viewmodel);
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
