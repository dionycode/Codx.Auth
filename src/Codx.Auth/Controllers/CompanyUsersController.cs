using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class CompanyUsersController : Controller
    {
        protected readonly UserManager<ApplicationUser> _userManager;
        private readonly UserDbContext _context;
        protected readonly IMapper _mapper;
        public CompanyUsersController(UserManager<ApplicationUser> userManager, UserDbContext context, IMapper mapper)
        {
            _userManager = userManager;
            _context = context;
            _mapper = mapper;
        }

        public IActionResult GetCompanyUsersTableData(Guid companyid, string search, string sort, string order, int offset, int limit)
        {
            var query = _context.UserCompanies.Include(o => o.User).Where(o => o.CompanyId == companyid);
            var data = query.OrderBy(o => o.User.UserName).Skip(offset).Take(limit).ToList();

            var viewModel = _mapper.Map<List<CompanyUserDetailsViewModel>>(data);

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }

        public async Task<IActionResult> Add(Guid companyid)
        {
            var company = await _context.Companies.FirstOrDefaultAsync(o => o.Id == companyid);

            var viewModel = new CompanyUserAddViewModel
            {
                CompanyId = company.Id,
            };
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Add(CompanyUserAddViewModel viewModel, string action)
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

                    if(findByEmail == null)
                    {
                        ModelState.AddModelError("", "User not found");
                        return View(viewModel);
                    }

                    var isUserCompanyExist = await _context.UserCompanies.AnyAsync(o => o.UserId == findByEmail.Id && o.CompanyId == viewModel.CompanyId);
                    if (isUserCompanyExist) 
                    {
                        ModelState.AddModelError("", "User already added to this company");
                        return View(viewModel);
                    }

                    var record = new UserCompany();
                    record.CompanyId = viewModel.CompanyId;
                    record.UserId = findByEmail.Id;

                    await _context.UserCompanies.AddAsync(record).ConfigureAwait(false);

                    if (!record.User.DefaultCompanyId.HasValue)
                    {
                        record.User.DefaultCompanyId = record.CompanyId;
                        _context.Users.Update(record.User);
                    }

                    var result = await _context.SaveChangesAsync().ConfigureAwait(false);                  

                    if (result > 0)
                    {
                        return RedirectToAction("Details", "Companies", new { id = viewModel.CompanyId });
                    }

                    ModelState.AddModelError("", "Failed");
                }
            }           

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid companyid, Guid userid)
        {
            var record = await _context.UserCompanies.Include(o => o.User).FirstOrDefaultAsync(o => o.CompanyId == companyid && o.UserId == userid);

            var viewModel = _mapper.Map<CompanyUserEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(CompanyUserEditViewModel viewModel)
        {
            var record = await _context.UserCompanies.Include(uc => uc.User).FirstOrDefaultAsync(o => o.CompanyId == viewModel.CompanyId && o.UserId == viewModel.UserId);
            if (ModelState.IsValid && record != null)
            {
                if(record.User.DefaultCompanyId == record.CompanyId)
                {
                    record.User.DefaultCompanyId = null;
                    _context.Users.Update(record.User);
                }

                _context.UserCompanies.Remove(record);
                var result = await _context.SaveChangesAsync().ConfigureAwait(false);
                if (result > 0)
                {
                    return RedirectToAction("Details", "Companies", new { id = viewModel.CompanyId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }
    }
}
