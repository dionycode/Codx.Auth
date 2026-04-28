using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class UsersController : Controller
    {
        protected readonly UserDbContext _userdbcontext;
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly IMapper _mapper;

        public UsersController(UserManager<ApplicationUser> userManager, UserDbContext userdbcontext, IMapper mapper)
        {
            _userManager = userManager;
            _userdbcontext = userdbcontext;
            _mapper = mapper;
        }

        // List all Users
        public IActionResult Index()
        {           
            return View();
        }

        [HttpGet]
        public JsonResult GetUsersTableData(string search, string sort, string order, int offset, int limit)
        {
            var roles = _userManager.Users;
            var userroles = roles.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = userroles.Select(user => new UserDetailsViewModel
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email
            }).ToList();

            return Json(new
            {
                total = roles.Count(),
                rows = viewModel
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(Guid id)
        {
            var record = _userManager.Users.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<UserDetailsViewModel>(record);

            if(record.DefaultCompanyId.HasValue)
            {
                var company = await _userdbcontext.Companies.Include(c => c.Tenant).FirstOrDefaultAsync(o => o.Id == record.DefaultCompanyId);
                viewmodel.CompanyName = company.Name;
                viewmodel.TenantName = company.Tenant.Name;
            }

            return View(viewmodel);
        }

        // Add User
        [HttpGet]
        public IActionResult Add()
        {
            return View();    
        }

        [HttpPost]
        public async Task<IActionResult> Add(UserAddViewModel model)
        {
            if (string.IsNullOrEmpty(model.UserName))
            {
                ModelState.AddModelError("Username", "Username is required.");
                return View(model);
            }

            if (string.IsNullOrEmpty(model.Password))
            {
                ModelState.AddModelError("Password", "Password is required.");
                return View(model);
            }

            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("Password", "Password did not match.");
                return View(model);
            }

            var existingEmailUser = await _userManager.FindByEmailAsync(model.Email).ConfigureAwait(false);
            if (existingEmailUser != null)
            {
                ModelState.AddModelError("Email", "Email already exists.");
                return View(model);
            }

            var record = new ApplicationUser
            {
                UserName = model.UserName,
                Email = model.Email,
                GivenName = model.FirstName,
                MiddleName = model.MiddleName,
                FamilyName = model.LastName
            };

            var result = await _userManager.CreateAsync(record, model.Password).ConfigureAwait(false);

            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Index));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.ToString());
            }

            return View(model);
        }


        // Edit User
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var record = await _userManager.FindByIdAsync(id);
            var companySelectOptions = await _userdbcontext.UserCompanies.Include(uc => uc.Company).Where(uc => uc.UserId == record.Id).Select(o => new SelectListItem
            {
                Value = o.CompanyId.ToString(),
                Text = o.Company.Name
            }).ToListAsync();

            var viewmodel = new UserEditViewModel
            {
                Id = record.Id,
                UserName = record.UserName,
                Email = record.Email,
                DefaultCompanyId = record.DefaultCompanyId,
                FirstName = record.GivenName,
                MiddleName = record.MiddleName,
                LastName = record.FamilyName,
                CompanySelectOptions = companySelectOptions
            };

            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(UserEditViewModel viewmodel)
        {
            var record = await _userManager.FindByIdAsync(viewmodel.Id.ToString());

            if (ModelState.IsValid)
            {
                record.Email = viewmodel.Email;
                record.DefaultCompanyId = viewmodel.DefaultCompanyId;
                record.GivenName = viewmodel.FirstName;
                record.MiddleName = viewmodel.MiddleName;
                record.FamilyName = viewmodel.LastName;
                var result = await _userManager.UpdateAsync(record);

                if (result.Succeeded)
                {
                    return RedirectToAction(nameof(Index));
                }
            }

            var companySelectOptions = await _userdbcontext.UserCompanies.Include(uc => uc.Company).Where(uc => uc.UserId == record.Id).Select(o => new SelectListItem
            {
                Value = o.CompanyId.ToString(),
                Text = o.Company.Name
            }).ToListAsync();

            viewmodel.CompanySelectOptions = companySelectOptions;

            return View(viewmodel);
        }

        // Delete User
        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            var record = await _userManager.FindByIdAsync(id);

            var viewmodel = new UserEditViewModel
            {
                Id = record.Id,
                UserName = record.UserName,
                Email = record.Email
            };

            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(UserEditViewModel viewmodel)
        {
            var record = await _userManager.FindByIdAsync(viewmodel.Id.ToString());
            
            var result = await _userManager.DeleteAsync(record);

            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Index));
            }
        
            return View(viewmodel);
        }

        // Reset Password
        [HttpGet]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var record = await _userManager.FindByIdAsync(id);

            if (record == null)
            {
                return NotFound();
            }

            var viewmodel = new UserResetPasswordViewModel
            {
                Id = record.Id,
                UserName = record.UserName
            };

            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(UserResetPasswordViewModel viewmodel)
        {
            if (!ModelState.IsValid)
            {
                return View(viewmodel);
            }

            var record = await _userManager.FindByIdAsync(viewmodel.Id.ToString());

            if (record == null)
            {
                return NotFound();
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(record);
            var result = await _userManager.ResetPasswordAsync(record, token, viewmodel.NewPassword);

            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Details), new { id = viewmodel.Id });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(viewmodel);
        }

    }
}