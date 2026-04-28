using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Codx.Auth.Controllers
{
    [Authorize(Policy = "PlatformAdmin")]
    public class RolesController : Controller
    {

        protected readonly RoleManager<ApplicationRole> _roleManager;
        protected readonly IMapper _mapper;

        public RolesController(RoleManager<ApplicationRole> roleManager, IMapper mapper)
        {
            _roleManager = roleManager;
            _mapper = mapper;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public JsonResult GetRolesTableData(string search, string sort, string order, int offset, int limit)
        {
            var roles = _roleManager.Roles;
            var userroles = roles.OrderBy(o => o.Id).Skip(offset).Take(limit).ToList();
            var viewModel = userroles.Select(role => new RoleDetailsViewModel
            {
                Id = role.Id,
                Name = role.Name,
            }).ToList();

            return Json(new
            {
                total = roles.Count(),
                rows = viewModel
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            var record = _roleManager.Roles.FirstOrDefault(o => o.Id.ToString() == id);

            var viewmodel = _mapper.Map<RoleDetailsViewModel>(record);

            return View(viewmodel);
        }


        // Add Role
        [HttpGet]
        public IActionResult Add()
        {
            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(RoleAddViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var record = _mapper.Map<ApplicationRole>(viewModel);

                var result = await _roleManager.CreateAsync(record).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    return RedirectToAction(nameof(Index));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.ToString());
                }

            }

            return View(viewModel);
        }

        // Edit Role
        public async Task<IActionResult> Edit(string id)
        {
            var record = await _roleManager.FindByIdAsync(id);

            var viewmodel = _mapper.Map<RoleEditViewModel>(record);

            return View(viewmodel);
        }


        [HttpPost]
        public async Task<IActionResult> Edit(RoleEditViewModel viewmodel)
        {
            var record = await _roleManager.FindByIdAsync(viewmodel.Id.ToString());

            if (ModelState.IsValid)
            {
                record.Name = viewmodel.Name;

                var result = await _roleManager.UpdateAsync(record);

                if (result.Succeeded)
                {
                    return RedirectToAction(nameof(Index));
                }

                foreach(var error in result.Errors)
                {
                    ModelState.AddModelError("",error.ToString());
                }                
            }

            return View(viewmodel);
        }


        // Delete Role
        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            var record = await _roleManager.FindByIdAsync(id);

            var viewmodel = _mapper.Map<RoleEditViewModel>(record);

            return View(viewmodel);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(RoleEditViewModel viewmodel)
        {
            var record = await _roleManager.FindByIdAsync(viewmodel.Id.ToString());

            var result = await _roleManager.DeleteAsync(record);

            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Index));
            }

            return View(viewmodel);
        }


    }
}
