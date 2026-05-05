using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Models;
using Codx.Auth.Models.Email;
using Codx.Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    /// <summary>
    /// Service for handling Two-Factor Authentication operations
    /// </summary>
    public class TwoFactorService : ITwoFactorService
    {
        private readonly UserDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _emailService;
        private readonly IEmailTemplateService _templateService;
        private readonly ILogger<TwoFactorService> _logger;

        public TwoFactorService(
            UserDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailService emailService,
            IEmailTemplateService templateService,
            ILogger<TwoFactorService> logger)
        {
            _context = context;
            _userManager = userManager;
            _emailService = emailService;
            _templateService = templateService;
            _logger = logger;
        }

        public string GenerateVerificationCode()
        {
            // Generate a 6-digit numeric code
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var randomNumber = BitConverter.ToUInt32(bytes, 0);
            return (randomNumber % 1000000).ToString("D6");
        }

        public async Task<(bool success, string code, string message)> SendVerificationCodeAsync(Guid userId, string email, string userName = null, Guid? tenantId = null, CancellationToken ct = default)
        {
            try
            {
                var code = GenerateVerificationCode();
                await StoreVerificationCodeAsync(userId, code);

                var displayName = !string.IsNullOrEmpty(userName) ? userName : email;
                var subject = "Your Two-Factor Authentication Code";

                var renderContext = new EmailTemplateRenderContext(
                    UserName:      displayName,
                    UserEmail:     email,
                    TenantName:    string.Empty,
                    CompanyName:   string.Empty,
                    TwoFactorCode: code
                );

                var body = await _templateService.GetResolvedBodyAsync(
                    EmailTemplateType.TwoFactor, tenantId, renderContext, ct);

                var emailMessage = new EmailMessage
                {
                    To      = email,
                    Subject = subject,
                    Body    = body,
                    IsHtml  = true
                };

                var emailResult = await _emailService.SendEmailAsync(emailMessage);

                if (emailResult.Success)
                {
                    _logger.LogInformation("2FA verification code sent successfully to user {UserId} at {Email}", userId, email);
                    return (true, code, "Verification code sent successfully");
                }
                else
                {
                    _logger.LogError("Failed to send 2FA verification code to user {UserId} at {Email}: {Error}",
                        userId, email, emailResult.Message);

                    await ClearVerificationCodeAsync(userId);
                    return (false, string.Empty, $"Failed to send verification code: {emailResult.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while sending 2FA verification code to user {UserId}", userId);
                return (false, string.Empty, "An error occurred while sending the verification code");
            }
        }

        public async Task<bool> ValidateVerificationCodeAsync(Guid userId, string providedCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(providedCode))
                {
                    return false;
                }

                // Clean the provided code (remove spaces, etc.)
                var cleanCode = providedCode.Trim().Replace(" ", "");

                var tfcs = await _context.TwoFactorCodes.ToListAsync();

                // Find valid code for user
                var storedCode = await _context.TwoFactorCodes
                    .Where(c => c.UserId == userId && 
                               c.Code == cleanCode && 
                               !c.IsUsed && 
                               c.ExpiresAt > DateTime.UtcNow)
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync();

                if (storedCode == null)
                {
                    _logger.LogWarning("Invalid or expired 2FA code provided for user {UserId}", userId);
                    return false;
                }

                // Mark code as used
                storedCode.IsUsed = true;
                storedCode.UsedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("2FA code validated successfully for user {UserId}", userId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while validating 2FA code for user {UserId}", userId);
                return false;
            }
        }

        public async Task StoreVerificationCodeAsync(Guid userId, string code, int expirationMinutes = 10)
        {
            try
            {
                // Clear any existing unused codes for this user
                await ClearVerificationCodeAsync(userId);

                // Store new code
                var twoFactorCode = new TwoFactorCode
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Code = code,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes),
                    IsUsed = false
                };

                await _context.TwoFactorCodes.AddAsync(twoFactorCode);
                await _context.SaveChangesAsync();

                _logger.LogInformation("2FA verification code stored for user {UserId}, expires at {ExpiresAt}", 
                    userId, twoFactorCode.ExpiresAt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while storing 2FA code for user {UserId}", userId);
                throw;
            }
        }

        public async Task ClearVerificationCodeAsync(Guid userId)
        {
            try
            {
                var existingCodes = await _context.TwoFactorCodes
                    .Where(c => c.UserId == userId && !c.IsUsed)
                    .ToListAsync();

                if (existingCodes.Any())
                {
                    _context.TwoFactorCodes.RemoveRange(existingCodes);
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation("Cleared {Count} existing 2FA codes for user {UserId}", 
                        existingCodes.Count, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while clearing 2FA codes for user {UserId}", userId);
                throw;
            }
        }
    }
}