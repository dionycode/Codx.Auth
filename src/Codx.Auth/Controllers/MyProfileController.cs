using AutoMapper;
using Codx.Auth.Configuration;
using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Extensions;
using Codx.Auth.ViewModels;
using Duende.IdentityServer;
using IdentityModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize]
    public class MyProfileController : Controller
    {
        protected readonly UserDbContext _userdbcontext;
        private readonly SignInManager<ApplicationUser> _signInManager;
        protected readonly UserManager<ApplicationUser> _userManager;
        protected readonly IMapper _mapper;
        private readonly IConfiguration _configuration;

        public MyProfileController(
            UserManager<ApplicationUser> userManager, 
            SignInManager<ApplicationUser> signInManager, 
            UserDbContext userdbcontext, 
            IMapper mapper,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _userdbcontext = userdbcontext;
            _mapper = mapper;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            var user = _userManager.Users.FirstOrDefault(o => o.Id == User.GetUserId());

            var viewModel = _mapper.Map<MyProfileViewModel>(user);

            if(user.DefaultCompanyId.HasValue)
            {
                var userDefaultCompany = _userdbcontext.Companies.Include(c => c.Tenant).FirstOrDefault(c => c.Id == user.DefaultCompanyId);
                viewModel.CompanyName = userDefaultCompany?.Name;
                viewModel.TenantName = userDefaultCompany?.Tenant?.Name;
            }

            // Check if external authentication providers are enabled
            var externalAuthConfig = new ExternalAuthConfiguration();
            _configuration.GetSection("Authentication").Bind(externalAuthConfig);
            
            // Google Authentication
            viewModel.IsGoogleAuthEnabled = externalAuthConfig.Google.IsConfigured;
            if (viewModel.IsGoogleAuthEnabled)
            {
                var googleLogins = await _userManager.GetLoginsAsync(user);
                viewModel.HasGoogleAccount = googleLogins.Any(l => l.LoginProvider == "Google");
            }

            // Microsoft Authentication
            viewModel.IsMicrosoftAuthEnabled = externalAuthConfig.Microsoft.IsConfigured;
            if (viewModel.IsMicrosoftAuthEnabled)
            {
                var microsoftLogins = await _userManager.GetLoginsAsync(user);
                viewModel.HasMicrosoftAccount = microsoftLogins.Any(l => l.LoginProvider == "Microsoft");
            }

            // Two-Factor Authentication
            viewModel.TwoFactorEnabled = user.TwoFactorEnabled;
            var hasPassword = await _userManager.HasPasswordAsync(user);
            var userLogins = await _userManager.GetLoginsAsync(user);
            // Can use 2FA if user has a password (not external-only accounts)
            viewModel.CanUseTwoFactor = hasPassword;

            return View(viewModel);
        }

        [HttpGet]
        public JsonResult GetMyClaimsTableData(string search, string sort, string order, int offset, int limit)
        {
            var userId = User.GetUserId();
            var query = _userdbcontext.UserClaims.Where(o => o.UserId == userId);

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
        public JsonResult GetMyRolesTableData(string search, string sort, string order, int offset, int limit)
        {
            var userId = User.GetUserId();
            var query = _userdbcontext.UserRoles.Include(o => o.Role).Where(o => o.UserId == userId);

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
        public JsonResult GetMyTenantsTableData(string search, string sort, string order, int offset, int limit)
        {
            var userId = User.GetUserId();
            var tenantAdminRoles = new[] { "TENANT_OWNER", "TENANT_ADMIN", "TENANT_MANAGER" };

            // A tenant is "manageable" when the user holds a tenant-scoped membership
            // (CompanyId == null) with at least one active role whose code is a tenant admin role.
            var query = _userdbcontext.UserMemberships
                .Where(m => m.UserId == userId
                            && m.CompanyId == null
                            && m.Status == "Active"
                            && m.MembershipRoles.Any(r => r.Status == "Active"
                            && tenantAdminRoles.Contains(r.RoleDefinition.Code)));

            var total = query.Count();
            var data = query
                .Include(m => m.Tenant)
                .OrderBy(m => m.Tenant.Name)
                .Skip(offset)
                .Take(limit)
                .ToList();

            var rows = data.Select(m => new
            {
                tenantId = m.TenantId,
                tenantName = m.Tenant.Name
            }).ToList();

            return Json(new { total, rows });
        }

        [HttpGet]
        public JsonResult GetMyTenantMembershipsTableData(string search, string sort, string order, int offset, int limit)
        {
            var userId = User.GetUserId();
            var query = _userdbcontext.UserMemberships
                .Where(m => m.UserId == userId && m.CompanyId == null)
                .Include(m => m.Tenant)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition);

            var total = query.Count();
            var rows = query.OrderBy(m => m.Tenant.Name).Skip(offset).Take(limit).ToList()
                .Select(m => new
                {
                    membershipId = m.Id,
                    tenantId = m.TenantId,
                    tenantName = m.Tenant.Name,
                    roles = string.Join(", ", m.MembershipRoles
                        .Where(r => r.Status == "Active")
                        .Select(r => r.RoleDefinition.DisplayName)),
                    status = m.Status,
                    joinedAt = m.JoinedAt
                }).ToList();

            return Json(new { total, rows });
        }

        [HttpGet]
        public JsonResult GetMyCompanyMembershipsTableData(string search, string sort, string order, int offset, int limit)
        {
            var userId = User.GetUserId();
            var query = _userdbcontext.UserMemberships
                .Where(m => m.UserId == userId && m.CompanyId != null)
                .Include(m => m.Tenant)
                .Include(m => m.Company)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition);

            var total = query.Count();
            var rows = query.OrderBy(m => m.Tenant.Name).ThenBy(m => m.Company.Name).Skip(offset).Take(limit).ToList()
                .Select(m => new
                {
                    membershipId = m.Id,
                    tenantId = m.TenantId,
                    tenantName = m.Tenant.Name,
                    companyId = m.CompanyId,
                    companyName = m.Company.Name,
                    roles = string.Join(", ", m.MembershipRoles
                        .Where(r => r.Status == "Active")
                        .Select(r => r.RoleDefinition.DisplayName)),
                    status = m.Status,
                    joinedAt = m.JoinedAt
                }).ToList();

            return Json(new { total, rows });
        }

        [HttpGet]
        public IActionResult ManageTenant(Guid id)
        {
            var userId = User.GetUserId();
            var tenantAdminRoles = new[] { "TENANT_OWNER", "TENANT_ADMIN", "TENANT_MANAGER" };
            var hasMembership = _userdbcontext.UserMemberships
                .Any(m => m.UserId == userId
                          && m.TenantId == id
                          && m.CompanyId == null
                          && m.Status == "Active"
                          && m.MembershipRoles.Any(r => r.Status == "Active"
                              && tenantAdminRoles.Contains(r.RoleDefinition.Code)));

            if (!hasMembership)
            {
                return RedirectToAction("Index");
            }

            var record = _userdbcontext.Tenants.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<TenantDetailsViewModel>(record);
            viewModel.IsOwner = HasTenantRole(id, "TENANT_OWNER");

            return View(viewModel);
        }

        public async Task<IActionResult> ManageTenantEdit(Guid id)
        {
            if (!HasTenantRole(id, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            var record = _userdbcontext.Tenants.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<TenantEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ManageTenantEdit(TenantEditViewModel viewModel)
        {
            if (!HasTenantRole(viewModel.Id, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            var isRecordFound = await _userdbcontext.Tenants.AsNoTracking().AnyAsync(u => u.Id == viewModel.Id && !u.IsDeleted);

            if (ModelState.IsValid && isRecordFound)
            {
                var userId = User.GetUserId();

                var record = _mapper.Map<Tenant>(viewModel);
                record.UpdatedAt = DateTime.Now;
                record.UpdatedBy = userId;

                _userdbcontext.Tenants.Update(record);
                var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction(nameof(ManageTenant), new { id = record.Id });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }
        [HttpGet]
        public IActionResult ManageTenantDelete(Guid id)
        {
            if (!HasTenantRole(id, "TENANT_OWNER"))
                return Forbid();

            var record = _userdbcontext.Tenants.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            var viewModel = _mapper.Map<TenantEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManageTenantDelete(TenantEditViewModel viewModel)
        {
            if (!HasTenantRole(viewModel.Id, "TENANT_OWNER"))
                return Forbid();

            var record = _userdbcontext.Tenants.FirstOrDefault(o => o.Id == viewModel.Id && !o.IsDeleted);

            if (record == null) return NotFound();

            if (ModelState.IsValid)
            {
                var userId = User.GetUserId();
                var now = DateTime.Now;

                // Soft-delete the tenant
                record.IsDeleted = true;
                record.IsActive = false;
                record.Status = "Cancelled";
                record.UpdatedAt = now;
                record.UpdatedBy = userId;
                _userdbcontext.Tenants.Update(record);

                // Soft-delete all companies under this tenant
                var companies = await _userdbcontext.Companies
                    .Where(c => c.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                foreach (var company in companies)
                {
                    company.IsDeleted = true;
                    company.IsActive = false;
                    company.Status = "Cancelled";
                    company.UpdatedAt = now;
                    company.UpdatedBy = userId;
                }

                // Remove tenant manager assignments
                var tenantManagers = await _userdbcontext.TenantManagers
                    .Where(tm => tm.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                _userdbcontext.TenantManagers.RemoveRange(tenantManagers);

                // Deactivate all memberships and their roles
                var memberships = await _userdbcontext.UserMemberships
                    .Include(m => m.MembershipRoles)
                    .Where(m => m.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                foreach (var membership in memberships)
                {
                    membership.Status = "Inactive";
                    foreach (var role in membership.MembershipRoles)
                    {
                        role.Status = "Inactive";
                    }
                }

                // Revoke pending invitations
                var pendingInvitations = await _userdbcontext.Invitations
                    .Where(i => i.TenantId == viewModel.Id && i.Status == "Pending")
                    .ToListAsync().ConfigureAwait(false);
                foreach (var invitation in pendingInvitations)
                {
                    invitation.Status = "Revoked";
                }

                // Revoke active workspace sessions
                var activeSessions = await _userdbcontext.WorkspaceSessions
                    .Where(s => s.TenantId == viewModel.Id && s.Status == "Active")
                    .ToListAsync().ConfigureAwait(false);
                foreach (var session in activeSessions)
                {
                    session.Status = "Revoked";
                }

                // Remove application role assignments
                var appRoles = await _userdbcontext.UserApplicationRoles
                    .Where(r => r.TenantId == viewModel.Id)
                    .ToListAsync().ConfigureAwait(false);
                _userdbcontext.UserApplicationRoles.RemoveRange(appRoles);

                var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);
                if (result > 0)
                {
                    return RedirectToAction(nameof(Index));
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }

        public IActionResult GetManageTenantCompaniesTableData(Guid tenantid, string search, string sort, string order, int offset, int limit)
        {
            if (!HasTenantRole(tenantid, "TENANT_OWNER", "TENANT_ADMIN", "TENANT_MANAGER"))
                return Forbid();

            var query = _userdbcontext.Companies.Where(o => !o.IsDeleted && o.TenantId == tenantid);
            var data = query.OrderBy(o => o.Name).Skip(offset).Take(limit).ToList();

            var viewModel = _mapper.Map<List<CompanyDetailsViewModel>>(data);

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }

        public IActionResult ManageTenantCompanyDetails(Guid id)
        {
            var record = _userdbcontext.Companies.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            if (!HasTenantRole(record.TenantId, "TENANT_OWNER", "TENANT_ADMIN", "TENANT_MANAGER")
                && !HasCompanyRole(record.Id, "COMPANY_ADMIN", "COMPANY_MANAGER", "MEMBER"))
                return Forbid();

            var viewModel = _mapper.Map<CompanyDetailsViewModel>(record);

            return View(viewModel);
        }

        public async Task<IActionResult> ManageTenantCompanyAdd(Guid tenantid)
        {
            var tenant = await _userdbcontext.Tenants.FirstOrDefaultAsync(o => o.Id == tenantid && !o.IsDeleted);

            if (tenant == null) return NotFound();

            if (!HasTenantRole(tenantid, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            var viewModel = new CompanyAddViewModel
            {
                TenantId = tenant.Id,
            };
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ManageTenantCompanyAdd(CompanyAddViewModel viewModel)
        {
            if (!HasTenantRole(viewModel.TenantId, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            if (ModelState.IsValid)
            {
                var userId = User.GetUserId();

                var record = _mapper.Map<Company>(viewModel);
                record.IsActive = true;
                record.IsDeleted = false;
                record.CreatedBy = userId;
                record.CreatedAt = DateTime.Now;

                await _userdbcontext.Companies.AddAsync(record).ConfigureAwait(false);
                var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction(nameof(ManageTenantCompanyDetails), new { id = record.Id });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);

        }

        public async Task<IActionResult> ManageTenantCompanyEdit(Guid id)
        {
            var record = _userdbcontext.Companies.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            if (!HasTenantRole(record.TenantId, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            var viewModel = _mapper.Map<CompanyEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ManageTenantCompanyEdit(CompanyEditViewModel viewModel)
        {
            var existingRecord = await _userdbcontext.Companies.AsNoTracking().FirstOrDefaultAsync(u => u.Id == viewModel.Id && !u.IsDeleted);
            var isRecordFound = existingRecord != null;

            if (isRecordFound && !HasTenantRole(existingRecord.TenantId, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            if (ModelState.IsValid && isRecordFound)
            {
                var userId = User.GetUserId();

                var record = _mapper.Map<Company>(viewModel);
                record.UpdatedAt = DateTime.Now;
                record.UpdatedBy = userId;

                _userdbcontext.Companies.Update(record);
                var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);

                if (result > 0)
                {
                    return RedirectToAction(nameof(ManageTenantCompanyDetails), new { id = record.Id });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> ManageTenantCompanyDelete(Guid id)
        {
            var record = _userdbcontext.Companies.FirstOrDefault(o => o.Id == id && !o.IsDeleted);

            if (record == null) return NotFound();

            if (!HasTenantRole(record.TenantId, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            var viewModel = _mapper.Map<CompanyEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ManageTenantCompanyDelete(CompanyEditViewModel viewModel)
        {
            var record = _userdbcontext.Companies.FirstOrDefault(o => o.Id == viewModel.Id && !o.IsDeleted);
            var isRecordFound = record != null;

            if (isRecordFound && !HasTenantRole(record.TenantId, "TENANT_OWNER", "TENANT_ADMIN"))
                return Forbid();

            if (ModelState.IsValid && isRecordFound)
            {
                record.IsDeleted = true;
                record.IsActive = false;
                record.Status = "Cancelled";
                record.UpdatedAt = DateTime.Now;
                record.UpdatedBy = User.GetUserId();

                _userdbcontext.Companies.Update(record);

                var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);
                if (result > 0)
                {
                    return RedirectToAction(nameof(ManageTenant), new { id = viewModel.TenantId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }

        public IActionResult GetManageTenantCompanyUsersTableData(Guid companyid, string search, string sort, string order, int offset, int limit)
        {
            var company = _userdbcontext.Companies.AsNoTracking().FirstOrDefault(o => o.Id == companyid && !o.IsDeleted);
            if (company == null) return NotFound();

            if (!HasTenantRole(company.TenantId, "TENANT_OWNER", "TENANT_ADMIN", "TENANT_MANAGER")
                && !HasCompanyRole(companyid, "COMPANY_ADMIN", "COMPANY_MANAGER"))
                return Forbid();

            var query = _userdbcontext.UserCompanies.Include(o => o.User).Where(o => o.CompanyId == companyid);
            var data = query.OrderBy(o => o.User.UserName).Skip(offset).Take(limit).ToList();

            var viewModel = _mapper.Map<List<CompanyUserDetailsViewModel>>(data);

            return Json(new
            {
                total = query.Count(),
                rows = viewModel
            });
        }

        public async Task<IActionResult> ManageTenantCompanyUserAdd(Guid companyid)
        {
            var company = await _userdbcontext.Companies.FirstOrDefaultAsync(o => o.Id == companyid && !o.IsDeleted);

            if (company == null) return NotFound();

            if (!HasTenantRole(company.TenantId, "TENANT_OWNER", "TENANT_ADMIN")
                && !HasCompanyRole(companyid, "COMPANY_ADMIN"))
                return Forbid();

            var viewModel = new CompanyUserAddViewModel
            {
                CompanyId = company.Id,
            };
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ManageTenantCompanyUserAdd(CompanyUserAddViewModel viewModel, string action)
        {
            var company = await _userdbcontext.Companies.AsNoTracking().FirstOrDefaultAsync(o => o.Id == viewModel.CompanyId && !o.IsDeleted);
            if (company == null) return NotFound();

            if (!HasTenantRole(company.TenantId, "TENANT_OWNER", "TENANT_ADMIN")
                && !HasCompanyRole(viewModel.CompanyId, "COMPANY_ADMIN"))
                return Forbid();

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

                    var isUserCompanyExist = await _userdbcontext.UserCompanies.AnyAsync(o => o.UserId == findByEmail.Id && o.CompanyId == viewModel.CompanyId);
                    if (isUserCompanyExist)
                    {
                        ModelState.AddModelError("", "User already added to this company");
                        return View(viewModel);
                    }

                    var record = new UserCompany();
                    record.CompanyId = viewModel.CompanyId;
                    record.UserId = findByEmail.Id;

                    await _userdbcontext.UserCompanies.AddAsync(record).ConfigureAwait(false);

                    if (!record.User.DefaultCompanyId.HasValue)
                    {
                        record.User.DefaultCompanyId = record.CompanyId;
                        _userdbcontext.Users.Update(record.User);
                    }

                    var result = await _userdbcontext.SaveChangesAsync().ConfigureAwait(false);

                    if (result > 0)
                    {
                        return RedirectToAction("ManageTenantCompanyDetails", "MyProfile", new { id = viewModel.CompanyId });
                    }

                    ModelState.AddModelError("", "Failed");
                }
            }

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> ManageTenantCompanyUserDelete(Guid companyid, Guid userid)
        {
            var company = await _userdbcontext.Companies.AsNoTracking().FirstOrDefaultAsync(o => o.Id == companyid && !o.IsDeleted);
            if (company == null) return NotFound();

            if (!HasTenantRole(company.TenantId, "TENANT_OWNER", "TENANT_ADMIN")
                && !HasCompanyRole(companyid, "COMPANY_ADMIN"))
                return Forbid();

            var record = await _userdbcontext.UserCompanies.Include(o => o.User).FirstOrDefaultAsync(o => o.CompanyId == companyid && o.UserId == userid);

            var viewModel = _mapper.Map<CompanyUserEditViewModel>(record);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ManageTenantCompanyUserDelete(CompanyUserEditViewModel viewModel)
        {
            var company = await _userdbcontext.Companies.AsNoTracking().FirstOrDefaultAsync(o => o.Id == viewModel.CompanyId && !o.IsDeleted);
            if (company == null) return NotFound();

            if (!HasTenantRole(company.TenantId, "TENANT_OWNER", "TENANT_ADMIN")
                && !HasCompanyRole(viewModel.CompanyId, "COMPANY_ADMIN"))
                return Forbid();

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
                    return RedirectToAction("ManageTenantCompanyDetails", "MyProfile", new { id = viewModel.CompanyId });
                }

                ModelState.AddModelError("", "Failed");
            }

            return View(viewModel);
        }

        [HttpGet]
        public JsonResult GetManageTenantMembersTableData(Guid tenantid, string search, string sort, string order, int offset, int limit)
        {
            var tenantAdminRoles = new[] { "TENANT_OWNER", "TENANT_ADMIN", "TENANT_MANAGER" };
            var query = _userdbcontext.UserMemberships
                .Where(m => m.TenantId == tenantid
                            && m.CompanyId == null
                            && m.Status == "Active"
                            && m.MembershipRoles.Any(r => r.Status == "Active"
                                && tenantAdminRoles.Contains(r.RoleDefinition.Code)))
                .Include(m => m.User)
                .Include(m => m.MembershipRoles)
                    .ThenInclude(r => r.RoleDefinition);

            var total = query.Count();
            var data = query.OrderBy(m => m.User.UserName).Skip(offset).Take(limit).ToList();

            var rows = data.Select(m => new
            {
                membershipId = m.Id,
                userId = m.UserId,
                userEmail = m.User.Email,
                userName = m.User.UserName,
                roles = string.Join(", ", m.MembershipRoles
                    .Where(r => r.Status == "Active")
                    .Select(r => r.RoleDefinition.DisplayName)),
                joinedAt = m.JoinedAt
            }).ToList();

            return Json(new { total, rows });
        }


        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if(model.NewPassword != model.ConfirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Password and Confirm Password do not match");
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Index", "Home");
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);
                return RedirectToAction("ChangePasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult ChangePasswordConfirmation()
        {
            return View();
        }

        /// <summary>
        /// Enable Two-Factor Authentication for the current user
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> EnableTwoFactor()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            // Check if user has external logins only
            var userLogins = await _userManager.GetLoginsAsync(user);
            var hasPassword = await _userManager.HasPasswordAsync(user);

            if (!hasPassword && userLogins.Any())
            {
                TempData["ErrorMessage"] = "Two-Factor Authentication is not available for external login accounts. Your external provider (Google, Microsoft, etc.) handles authentication security.";
                return RedirectToAction("Index");
            }

            if (!hasPassword)
            {
                TempData["ErrorMessage"] = "You must set a password before enabling Two-Factor Authentication.";
                return RedirectToAction("Index");
            }

            var result = await _userManager.SetTwoFactorEnabledAsync(user, true);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Two-Factor Authentication has been enabled for your account. You will receive a verification code via email on your next login.";
            }
            else
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                TempData["ErrorMessage"] = $"Failed to enable Two-Factor Authentication: {errors}";
            }

            return RedirectToAction("Index");
        }

        /// <summary>
        /// Disable Two-Factor Authentication for the current user
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DisableTwoFactor()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Two-Factor Authentication has been disabled for your account.";
            }
            else
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                TempData["ErrorMessage"] = $"Failed to disable Two-Factor Authentication: {errors}";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult ConnectGoogle()
        {
            // Check if Google authentication is enabled
            var externalAuthConfig = new ExternalAuthConfiguration();
            _configuration.GetSection("Authentication").Bind(externalAuthConfig);

            if (!externalAuthConfig.Google.IsConfigured)
            {
                TempData["ErrorMessage"] = "Google authentication is not configured.";
                return RedirectToAction("Index");
            }

            // Create direct challenge for account linking (bypass ExternalController)
            var returnUrl = Url.Action("ConnectGoogleCallback", "MyProfile");
            var props = new AuthenticationProperties
            {
                RedirectUri = returnUrl,
                Items =
                {
                    { "returnUrl", returnUrl },
                    { "scheme", "Google" },
                    { "action", "link" } // Add marker to distinguish from login
                }
            };

            return Challenge(props, "Google");
        }

        [HttpGet]
        public async Task<IActionResult> ConnectGoogleCallback()
        {
            try
            {
                // Read external identity from the temporary cookie (same as ExternalController)
                var result = await HttpContext.AuthenticateAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                if (result?.Succeeded != true)
                {
                    TempData["ErrorMessage"] = "Error loading external login information during account linking.";
                    return RedirectToAction("Index");
                }

                // Extract provider info
                var externalUser = result.Principal;
                var userIdClaim = externalUser.FindFirst(JwtClaimTypes.Subject) ??
                                  externalUser.FindFirst(ClaimTypes.NameIdentifier);

                if (userIdClaim == null)
                {
                    TempData["ErrorMessage"] = "Unable to retrieve external user information.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                var provider = result.Properties.Items["scheme"];
                var providerUserId = userIdClaim.Value;

                // Get current authenticated user
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    TempData["ErrorMessage"] = "You must be logged in to link accounts.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                // Check if this Google account is already linked to another user
                var existingUser = await _userManager.FindByLoginAsync(provider, providerUserId);
                if (existingUser != null && existingUser.Id != user.Id)
                {
                    TempData["ErrorMessage"] = "This Google account is already linked to another user account.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                // Check if already linked to current user
                if (existingUser != null && existingUser.Id == user.Id)
                {
                    TempData["ErrorMessage"] = "This Google account is already linked to your account.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                // Add the external login to current user
                var loginInfo = new UserLoginInfo(provider, providerUserId, provider);
                var addLoginResult = await _userManager.AddLoginAsync(user, loginInfo);

                // Clean up the external authentication cookie
                await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);

                if (addLoginResult.Succeeded)
                {
                    TempData["SuccessMessage"] = "Google account has been successfully linked to your account.";
                }
                else
                {
                    var errors = string.Join(", ", addLoginResult.Errors.Select(e => e.Description));
                    TempData["ErrorMessage"] = $"Failed to link Google account: {errors}";
                }
            }
            catch (Exception ex)
            {
                // Clean up the external authentication cookie in case of any error
                try
                {
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                TempData["ErrorMessage"] = "An error occurred while linking your Google account. Please try again.";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> DisconnectGoogle()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            // Get user logins
            var userLogins = await _userManager.GetLoginsAsync(user);
            var googleLogin = userLogins.FirstOrDefault(l => l.LoginProvider == "Google");

            if (googleLogin == null)
            {
                TempData["ErrorMessage"] = "No Google account is linked to your profile.";
                return RedirectToAction("Index");
            }

            // Ensure user has a password or another way to login
            var hasPassword = await _userManager.HasPasswordAsync(user);
            var otherLogins = userLogins.Where(l => l.LoginProvider != "Google").Count();

            if (!hasPassword && otherLogins == 0)
            {
                TempData["ErrorMessage"] = "Cannot disconnect Google account. You must set a password or link another external account first.";
                return RedirectToAction("Index");
            }

            // Remove the Google login
            var result = await _userManager.RemoveLoginAsync(user, googleLogin.LoginProvider, googleLogin.ProviderKey);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Google account has been successfully disconnected from your account.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to disconnect Google account. Please try again.";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult ConnectMicrosoft()
        {
            // Check if Microsoft authentication is enabled
            var externalAuthConfig = new ExternalAuthConfiguration();
            _configuration.GetSection("Authentication").Bind(externalAuthConfig);

            if (!externalAuthConfig.Microsoft.IsConfigured)
            {
                TempData["ErrorMessage"] = "Microsoft authentication is not configured.";
                return RedirectToAction("Index");
            }

            // Create direct challenge for account linking (bypass ExternalController)
            var returnUrl = Url.Action("ConnectMicrosoftCallback", "MyProfile");
            var props = new AuthenticationProperties
            {
                RedirectUri = returnUrl,
                Items =
                {
                    { "returnUrl", returnUrl },
                    { "scheme", "Microsoft" },
                    { "action", "link" } // Add marker to distinguish from login
                }
            };

            return Challenge(props, "Microsoft");
        }

        [HttpGet]
        public async Task<IActionResult> ConnectMicrosoftCallback()
        {
            try
            {
                // Read external identity from the temporary cookie (same as ExternalController)
                var result = await HttpContext.AuthenticateAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                if (result?.Succeeded != true)
                {
                    TempData["ErrorMessage"] = "Error loading external login information during account linking.";
                    return RedirectToAction("Index");
                }

                // Extract provider info
                var externalUser = result.Principal;
                var userIdClaim = externalUser.FindFirst(JwtClaimTypes.Subject) ??
                                  externalUser.FindFirst(ClaimTypes.NameIdentifier);

                if (userIdClaim == null)
                {
                    TempData["ErrorMessage"] = "Unable to retrieve external user information.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                var provider = result.Properties.Items["scheme"];
                var providerUserId = userIdClaim.Value;

                // Get current authenticated user
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    TempData["ErrorMessage"] = "You must be logged in to link accounts.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                // Check if this Microsoft account is already linked to another user
                var existingUser = await _userManager.FindByLoginAsync(provider, providerUserId);
                if (existingUser != null && existingUser.Id != user.Id)
                {
                    TempData["ErrorMessage"] = "This Microsoft account is already linked to another user account.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                // Check if already linked to current user
                if (existingUser != null && existingUser.Id == user.Id)
                {
                    TempData["ErrorMessage"] = "This Microsoft account is already linked to your account.";
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                    return RedirectToAction("Index");
                }

                // Add the external login to current user
                var loginInfo = new UserLoginInfo(provider, providerUserId, provider);
                var addLoginResult = await _userManager.AddLoginAsync(user, loginInfo);

                // Clean up the external authentication cookie
                await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);

                if (addLoginResult.Succeeded)
                {
                    TempData["SuccessMessage"] = "Microsoft account has been successfully linked to your account.";
                }
                else
                {
                    var errors = string.Join(", ", addLoginResult.Errors.Select(e => e.Description));
                    TempData["ErrorMessage"] = $"Failed to link Microsoft account: {errors}";
                }
            }
            catch (Exception ex)
            {
                // Clean up the external authentication cookie in case of any error
                try
                {
                    await HttpContext.SignOutAsync(IdentityServerConstants.ExternalCookieAuthenticationScheme);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                TempData["ErrorMessage"] = "An error occurred while linking your Microsoft account. Please try again.";
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> DisconnectMicrosoft()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Index");
            }

            // Get user logins
            var userLogins = await _userManager.GetLoginsAsync(user);
            var microsoftLogin = userLogins.FirstOrDefault(l => l.LoginProvider == "Microsoft");

            if (microsoftLogin == null)
            {
                TempData["ErrorMessage"] = "No Microsoft account is linked to your profile.";
                return RedirectToAction("Index");
            }

            // Ensure user has a password or another way to login
            var hasPassword = await _userManager.HasPasswordAsync(user);
            var otherLogins = userLogins.Where(l => l.LoginProvider != "Microsoft").Count();

            if (!hasPassword && otherLogins == 0)
            {
                TempData["ErrorMessage"] = "Cannot disconnect Microsoft account. You must set a password or link another external account first.";
                return RedirectToAction("Index");
            }

            // Remove the Microsoft login
            var result = await _userManager.RemoveLoginAsync(user, microsoftLogin.LoginProvider, microsoftLogin.ProviderKey);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Microsoft account has been successfully disconnected from your account.";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to disconnect Microsoft account. Please try again.";
            }

            return RedirectToAction("Index");
        }

        private bool HasTenantRole(Guid tenantId, params string[] roleCodes)
        {
            var userId = User.GetUserId();
            return _userdbcontext.UserMemberships.Any(m =>
                m.UserId == userId
                && m.TenantId == tenantId
                && m.CompanyId == null
                && m.Status == "Active"
                && m.MembershipRoles.Any(r => r.Status == "Active" && roleCodes.Contains(r.RoleDefinition.Code)));
        }

        private bool HasCompanyRole(Guid companyId, params string[] roleCodes)
        {
            var userId = User.GetUserId();
            return _userdbcontext.UserMemberships.Any(m =>
                m.UserId == userId
                && m.CompanyId == companyId
                && m.Status == "Active"
                && m.MembershipRoles.Any(r => r.Status == "Active" && roleCodes.Contains(r.RoleDefinition.Code)));
        }
    }
}
