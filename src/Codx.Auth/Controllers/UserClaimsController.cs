using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class UserClaimsController : Controller
    {
        protected readonly UserDbContext _userdbcontext;
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly IMapper _mapper;
        public UserClaimsController(UserDbContext userdbcontext, UserManager<ApplicationUser> userManager, IMapper mapper)
        {
            _userdbcontext = userdbcontext;
            _userManager = userManager;
            _mapper = mapper;
        }

        [HttpGet]
        public JsonResult GetUserClaimsTableData(Guid userid, string search, string sort, string order, int offset, int limit)
        {
            var query = _userdbcontext.UserClaims.Where(o => o.UserId == userid);

            var data = query.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = data.Select(claim => new UserClaimDetailsViewModel
            {
                Id = claim.Id,
                ClaimType = claim.ClaimType,
                ClaimValue = claim.ClaimValue,
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
            var record = _userdbcontext.UserClaims.FirstOrDefault(o => o.Id == id);

            var viewmodel = _mapper.Map<UserClaimDetailsViewModel>(record);

            return View(viewmodel);
        }

        [HttpGet]
        public async Task<IActionResult> Add(Guid userid)
        {
            var user = await _userdbcontext.Users.Include(i => i.UserClaims).FirstOrDefaultAsync(u => u.Id == userid);

            var viewmodel = new UserClaimAddViewModel();

            viewmodel.UserId = user.Id;
                      
            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> Add(UserClaimAddViewModel viewmodel)
        {
            if (ModelState.IsValid)
            {
                var user = await _userdbcontext.Users.Include(i => i.UserClaims).FirstOrDefaultAsync(u => u.Id == viewmodel.UserId);
                               
                var result = await _userManager.AddClaimAsync(user, new Claim(viewmodel.ClaimType, viewmodel.ClaimValue)).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    return RedirectToAction("Details", "Users", new { id = viewmodel.UserId });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.ToString());
                }
            }            

            return View(viewmodel);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var record = await _userdbcontext.UserClaims.FirstOrDefaultAsync(u => u.Id == id);

            var viewmodel = _mapper.Map<UserClaimEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserClaimEditViewModel viewmodel)
        {
            var isRecordFound = _userdbcontext.UserClaims.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ApplicationUserClaim>(viewmodel);

                _userdbcontext.Update(record);
                var result = await _userdbcontext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "Users", new { id = viewmodel.UserId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }


        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var record = await _userdbcontext.UserClaims.FirstOrDefaultAsync(u => u.Id == id);

            var viewmodel = _mapper.Map<UserClaimEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(UserClaimEditViewModel viewmodel)
        {
            var isRecordFound = _userdbcontext.UserClaims.Any(o => o.Id == viewmodel.Id);

            if (ModelState.IsValid && isRecordFound)
            {
                var record = _mapper.Map<ApplicationUserClaim>(viewmodel);
                _userdbcontext.Remove(record);
                var result = await _userdbcontext.SaveChangesAsync();

                if (result > 0)
                {
                    return RedirectToAction("Details", "Users", new { id = viewmodel.UserId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewmodel);
        }
    }
}