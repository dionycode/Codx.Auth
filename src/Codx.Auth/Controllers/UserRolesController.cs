using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class UserRolesController : Controller
    {

        protected readonly UserDbContext _userdbcontext;
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly RoleManager<ApplicationRole> _roleManager;
        protected readonly IMapper _mapper;
        public UserRolesController(UserDbContext userdbcontext, UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager, IMapper mapper)
        {
            _userdbcontext = userdbcontext;
            _userManager = userManager;
            _roleManager = roleManager;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetUserRolesTableData(Guid userid, string search, string sort, string order, int offset, int limit)
        {
            var query = _userdbcontext.UserRoles.Include(o => o.Role).Where(o => o.UserId == userid);

            var data = query.Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(userrole => new UserRoleDetailsViewModel
            {
                RoleId = userrole.RoleId,
                Role = userrole.Role.Name,
            }).ToList();

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }


        [HttpGet]
        public async Task<IActionResult> Add(Guid id)
        {
            var user = await _userdbcontext.Users.Include(i => i.UserRoles).FirstOrDefaultAsync(u => u.Id == id);

            var viewmodel = new UserRoleAddViewModel();

            viewmodel.UserId = user.Id;

            var roles = await _userdbcontext.Roles.ToListAsync().ConfigureAwait(false);
                        
            foreach (var role in roles)
            {
                viewmodel.Roles.Add(new SelectListItem
                {
                    Value = role.Id.ToString(),
                    Text = role.Name,
                });
            }

            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> Add(UserRoleAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var user = await _userdbcontext.Users.Include(i => i.UserRoles).FirstOrDefaultAsync(u => u.Id == viewmodel.UserId);

                var role = await _userdbcontext.Roles.FirstOrDefaultAsync(r => r.Id == viewmodel.RoleId).ConfigureAwait(false);

                var result = await _userManager.AddToRoleAsync(user, role.Name).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    return RedirectToAction("Details", "Users", new { id = viewmodel.UserId });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.ToString());
                }
            }

            var roles = await _userdbcontext.Roles.ToListAsync().ConfigureAwait(false);

            foreach (var role in roles)
            {
                viewmodel.Roles.Add(new SelectListItem
                {
                    Value = role.Id.ToString(),
                    Text = role.Name,
                });
            }

            return View(viewmodel);
        }


        [HttpGet]
        public async Task<IActionResult> Delete(Guid userid, Guid roleid)
        {
            var userrole = await _userdbcontext.UserRoles.Include(o => o.Role).FirstOrDefaultAsync(u => u.UserId == userid && u.RoleId == roleid);

            var viewmodel = new UserRoleEditViewModel
            {
                UserId = userrole.UserId,
                RoleId = userrole.RoleId,
                Role = userrole.Role.Name,
            };

            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(UserRoleEditViewModel viewmodel)
        {
            var user = await _userdbcontext.Users.FirstOrDefaultAsync(u => u.Id == viewmodel.UserId);

            var result = await _userManager.RemoveFromRoleAsync(user, viewmodel.Role).ConfigureAwait(false);

            if (result.Succeeded)
            {
                return RedirectToAction("Details", "Users", new { id = viewmodel.UserId });
            }

            return View(viewmodel);
        }


    }
}