using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class UserCompaniesController : Controller
    {
        protected readonly UserDbContext _userdbcontext;
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly IMapper _mapper;
        public UserCompaniesController(UserDbContext userdbcontext, UserManager<ApplicationUser> userManager, IMapper mapper)
        {
            _userdbcontext = userdbcontext;
            _userManager = userManager;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetUserCompaniesTableData(Guid userid, string search, string sort, string order, int offset, int limit)
        {
            var query = _userdbcontext.UserCompanies.Include(o => o.Company).ThenInclude(c => c.Tenant).Where(o => o.UserId == userid);

            var data = query.OrderBy(o => o.Company.Name).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(userCompany => new UserCompanyDetailsViewModel
            {
                UserId = userCompany.UserId,
                CompanyId = userCompany.CompanyId,
                CompanyName = userCompany.Company.Name,
                TenantId = userCompany.Company.TenantId,
                TenantName = userCompany.Company.Tenant.Name
            }).ToList();

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid companyid, Guid userid)
        {
            var record = await _userdbcontext.UserCompanies.Include(uc => uc.Company).ThenInclude(uc => uc.Tenant).FirstOrDefaultAsync(o => o.CompanyId == companyid && o.UserId == userid);

            var viewModel = _mapper.Map<UserCompanyEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(UserCompanyEditViewModel viewModel)
        {
            var record = await _userdbcontext.UserCompanies.Include(uc => uc.User).FirstOrDefaultAsync(o => o.CompanyId == viewModel.CompanyId && o.UserId == viewModel.UserId);
            if (ModelState.IsValid && record != null)
            {
                if (record.User.DefaultCompanyId == record.CompanyId)
                {
                    record.User.DefaultCompanyId = null;
                    _userdbcontext.Users.Update(record.User);
                }

                _userdbcontext.UserCompanies.Remove(record);
                var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);
                if (result > 0)
                {
                    return RedirectToAction("Details", "Users", new { id = viewModel.UserId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }
    }
}