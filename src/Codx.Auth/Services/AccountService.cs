using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Extensions;
using Microsoft.EntityFrameworkCore;
using Codx.Auth.Models;
using Codx.Auth.Models.Email;
using Codx.Auth.Services.Interfaces;
using Codx.Auth.ViewModels.Account;
using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public interface IAccountService
    {
        Task<(RegisterResponse result, ApplicationUser user)> RegisterAsync(RegisterRequest request);
        Task<(RegisterResponse result, ApplicationUser user)> RegisterExternalUserAsync(string email, string firstName, string middleName, string lastName, string provider, string providerUserId);
        Task<(bool success, string message)> SendEmailVerificationAsync(ApplicationUser user, string callbackUrl, Guid? tenantId = null, CancellationToken ct = default);
    }

    public class AccountService : IAccountService
    {
        private readonly UserDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _emailService;
        private readonly IAuditService _auditService;
        private readonly IEmailTemplateService _templateService;
        
        public AccountService(
            UserDbContext context, 
            UserManager<ApplicationUser> userManager,
            IEmailService emailService,
            IAuditService auditService,
            IEmailTemplateService templateService)
        {
            _context = context;
            _userManager = userManager;
            _emailService = emailService;
            _auditService = auditService;
            _templateService = templateService;
        }

        public async Task<(RegisterResponse result, ApplicationUser user)> RegisterAsync(RegisterRequest request)
        {
            var user = new ApplicationUser { UserName = request.Email, Email = request.Email, GivenName = request.FirstName, MiddleName = request.MiddleName, FamilyName = request.LastName };
            var result = await _userManager.CreateAsync(user, request.Password);

            if (result.Succeeded)
            {               
                await InitializeNewUserAsync(user);
                return (new RegisterResponse { Success = true }, user);
            }

            var errors = new List<string>();
            foreach (var error in result.Errors)
            {
                errors.Add(error.Description);
            }

            return (new RegisterResponse { Success = false, Errors = errors }, null);
        }

        public async Task<(RegisterResponse result, ApplicationUser user)> RegisterExternalUserAsync(string email, string firstName, string middleName, string lastName, string provider, string providerUserId)
        {
            // Create user with a unique username since email might not be available from external provider
            var userName = !string.IsNullOrEmpty(email) ? email : Guid.NewGuid().ToString();
            var user = new ApplicationUser 
            { 
                UserName = userName, 
                Email = email,
                GivenName = firstName,
                MiddleName = middleName,
                FamilyName = lastName
            };

            var result = await _userManager.CreateAsync(user);

            if (result.Succeeded)
            {
                // Do NOT enable 2FA for external users - they use their provider's 2FA
                await _userManager.SetTwoFactorEnabledAsync(user, false);
                
                // Add external login
                var addLoginResult = await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerUserId, provider));
                if (!addLoginResult.Succeeded)
                {
                    // If adding external login fails, clean up the user
                    await _userManager.DeleteAsync(user);
                    var loginErrors = addLoginResult.Errors.Select(e => e.Description).ToList();
                    return (new RegisterResponse { Success = false, Errors = loginErrors }, null);
                }

                // Initialize user with default tenant, company, roles, and claims
                await InitializeNewUserAsync(user);
                
                // External users' emails are already verified by the provider
                if (!string.IsNullOrEmpty(email))
                {
                    var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    await _userManager.ConfirmEmailAsync(user, token);
                }
                
                return (new RegisterResponse { Success = true }, user);
            }

            var errors = result.Errors.Select(e => e.Description).ToList();
            return (new RegisterResponse { Success = false, Errors = errors }, null);
        }

        public async Task<(bool success, string message)> SendEmailVerificationAsync(ApplicationUser user, string callbackUrl, Guid? tenantId = null, CancellationToken ct = default)
        {
            try
            {
                var displayName = user.GetDisplayName();
                var subject = "Confirm Your Email Address";

                var renderContext = new Models.EmailTemplateRenderContext(
                    UserName:         displayName,
                    UserEmail:        user.Email ?? string.Empty,
                    TenantName:       string.Empty,
                    CompanyName:      string.Empty,
                    VerificationLink: callbackUrl
                );

                var body = await _templateService.GetResolvedBodyAsync(
                    Models.EmailTemplateType.EmailVerification, tenantId, renderContext, ct);

                var emailMessage = new EmailMessage
                {
                    To      = user.Email,
                    Subject = subject,
                    Body    = body,
                    IsHtml  = true
                };

                var result = await _emailService.SendEmailAsync(emailMessage);
                
                return result.Success
                    ? (true, "Verification email sent successfully")
                    : (false, $"Failed to send verification email: {result.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Error sending verification email: {ex.Message}");
            }
        }

        private async Task InitializeNewUserAsync(ApplicationUser user)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Derive workspace name from user display name or email prefix
                var firstName = user.GivenName?.Trim();
                var lastName = user.FamilyName?.Trim();
                var displayName = (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName))
                    ? $"{firstName} {lastName}".Trim()
                    : null;
                var baseName = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : user.Email?.Split('@')[0] ?? "user";

                var tenantEmail = !string.IsNullOrEmpty(user.Email) ? user.Email : $"user-{user.Id}@external.com";
                var tenantId = Guid.NewGuid();
                var companyId = Guid.NewGuid();
                var slug = await GenerateUniqueSlugAsync(baseName);

                var defaultTenant = new Tenant
                {
                    Id = tenantId,
                    Name = baseName,
                    Description = $"{baseName}'s workspace",
                    Email = tenantEmail,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = user.Id,
                    IsDeleted = false,
                    IsActive = true,
                    Slug = slug,
                    Status = "Active",
                    Companies = new List<Company>
                    {
                        new Company
                        {
                            Id = companyId,
                            Name = baseName,
                            Description = $"{baseName}'s company",
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = user.Id,
                            IsDeleted = false,
                            IsActive = true,
                            Status = "Active",
                        }
                    }
                };

                await _context.Tenants.AddAsync(defaultTenant);
                await _context.SaveChangesAsync();

                // Create tenant-scoped membership (TenantOwner)
                var tenantMembership = new UserMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TenantId = tenantId,
                    CompanyId = null,
                    Status = "Active",
                    JoinedAt = DateTime.UtcNow,
                    MembershipRoles = new List<UserMembershipRole>
                    {
                        new UserMembershipRole
                        {
                            Id = Guid.NewGuid(),
                            RoleId = 1, // TENANT_OWNER
                            Status = "Active",
                            AssignedAt = DateTime.UtcNow,
                            AssignedByUserId = user.Id
                        }
                    }
                };

                // Create company-scoped membership (CompanyAdmin)
                var companyMembership = new UserMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TenantId = tenantId,
                    CompanyId = companyId,
                    Status = "Active",
                    JoinedAt = DateTime.UtcNow,
                    MembershipRoles = new List<UserMembershipRole>
                    {
                        new UserMembershipRole
                        {
                            Id = Guid.NewGuid(),
                            RoleId = 4, // COMPANY_ADMIN
                            Status = "Active",
                            AssignedAt = DateTime.UtcNow,
                            AssignedByUserId = user.Id
                        }
                    }
                };

                await _context.UserMemberships.AddRangeAsync(tenantMembership, companyMembership);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                await _auditService.LogAsync("RegistrationCompleted",
                    userId: user.Id, actorUserId: user.Id,
                    tenantId: tenantId, companyId: companyId,
                    resourceType: "UserMembership", resourceId: tenantMembership.Id.ToString());
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        private static string ToKebabCase(string name)
        {
            var s = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9\s]", "").Trim();
            s = Regex.Replace(s, @"\s+", "-");
            s = Regex.Replace(s, @"-{2,}", "-");
            return s.Length > 0 ? s : "user";
        }

        private async Task<string> GenerateUniqueSlugAsync(string name)
        {
            var baseSlug = ToKebabCase(name);
            if (!await _context.Tenants.AnyAsync(t => t.Slug == baseSlug))
                return baseSlug;
            for (int i = 2; ; i++)
            {
                var candidate = $"{baseSlug}-{i}";
                if (!await _context.Tenants.AnyAsync(t => t.Slug == candidate))
                    return candidate;
            }
        }
    }
}
