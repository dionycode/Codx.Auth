using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Collections.Generic;
using Codx.Auth.Data.Entities.Enterprise;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class TenantManagersController : Controller
    {
        protected readonly UserDbContext _userdbcontext;
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly IMapper _mapper;
        public TenantManagersController(UserDbContext userdbcontext, UserManager<ApplicationUser> userManager, IMapper mapper)
        {
            _userdbcontext = userdbcontext;
            _userManager = userManager;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetTenantManagersTableData(Guid tenantid, string search, string sort, string order, int offset, int limit)
        {
            var query = _userdbcontext.TenantManagers.Include(o => o.Manager).Where(o => o.TenantId == tenantid);

            var data = query.OrderBy(o => o.Manager.UserName).Skip(offset).Take(limit).ToList();
            var viewModel = _mapper.Map<List<TenantManagerDetailsViewModel>>(data);

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }

        public async Task<IActionResult> Add(Guid tenantid)
        {
            var tenant = await _userdbcontext.Tenants.FirstOrDefaultAsync(o => o.Id == tenantid);

            var viewModel = new TenantManagerAddViewModel
            {
                TenantId = tenant.Id,
            };
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Add(TenantManagerAddViewModel viewModel, string action)
        {
            if (action == "Search")
            {
                var findByEmail = await _userManager.FindByEmailAsync(viewModel.UserEmail);

                if (findByEmail != null)
                {
                    viewModel.UserId = findByEmail.Id;
                    viewModel.UserEmail = findByEmail.Email;
                    viewModel.UserName = findByEmail.UserName;
                }
                else
                {
                    ModelState.AddModelError("", "User not found");
                }
            }
            else
            {
                if (ModelState.IsValid)
                {
                    var findByEmail = await _userManager.FindByEmailAsync(viewModel.UserEmail);

                    if (findByEmail == null)
                    {
                        ModelState.AddModelError("", "User not found");
                        return View(viewModel);
                    }

                    var isTenantManagerExist = await _userdbcontext.TenantManagers.AnyAsync(o => o.UserId == findByEmail.Id && o.TenantId == viewModel.TenantId);
                    if (isTenantManagerExist)
                    {
                        ModelState.AddModelError("", "Manager already added to this tenant");
                        return View(viewModel);
                    }

                    var record = new TenantManager();
                    record.TenantId = viewModel.TenantId;
                    record.UserId = findByEmail.Id;

                    await _userdbcontext.TenantManagers.AddAsync(record).ConfigureAwait(false);                                       

                    var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);

                    if (result > 0)
                    {
                        return RedirectToAction("Details", "Tenants", new { id = viewModel.TenantId });
                    }

                    ModelState.AddModelError("", "Failed");
                }
            }

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid tenantid, Guid userid)
        {
            var record = await _userdbcontext.TenantManagers.Include(tm => tm.Manager).FirstOrDefaultAsync(o => o.TenantId == tenantid && o.UserId == userid);

            var viewModel = _mapper.Map<TenantManagerEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(TenantManagerEditViewModel viewModel)
        {
            var record = await _userdbcontext.TenantManagers.Include(tm => tm.Manager).FirstOrDefaultAsync(o => o.TenantId == viewModel.TenantId && o.UserId == viewModel.UserId);
            if (ModelState.IsValid && record != null)
            {   
                _userdbcontext.TenantManagers.Remove(record);
                var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);
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
