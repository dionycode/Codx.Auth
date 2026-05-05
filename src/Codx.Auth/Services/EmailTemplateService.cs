using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public class EmailTemplateService : IEmailTemplateService
    {
        private const int MaxBodyLength = 65_536;

        private static readonly Dictionary<EmailTemplateType, string> RequiredPlaceholders = new()
        {
            [EmailTemplateType.EmailVerification] = "{{VerificationLink}}",
            [EmailTemplateType.TwoFactor]         = "{{TwoFactorCode}}",
            [EmailTemplateType.PasswordReset]     = "{{PasswordResetLink}}",
            [EmailTemplateType.Invitation]        = "{{InvitationLink}}"
        };

        private readonly UserDbContext _context;
        private readonly BuiltInEmailTemplateProvider _builtIn;

        public EmailTemplateService(UserDbContext context, BuiltInEmailTemplateProvider builtIn)
        {
            _context = context;
            _builtIn = builtIn;
        }

        // ── Resolution + Rendering ────────────────────────────────────────────

        public async Task<string> GetResolvedBodyAsync(
            EmailTemplateType type,
            Guid? tenantId,
            EmailTemplateRenderContext context,
            CancellationToken ct = default)
        {
            var typeString = type.ToString();

            // 1. Tenant override
            if (tenantId.HasValue)
            {
                var tenantTemplate = await _context.EmailTemplates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TemplateType == typeString, ct);

                if (tenantTemplate is not null)
                    return ApplyPlaceholders(tenantTemplate.Body, context);
            }

            // 2. Global default
            var globalTemplate = await _context.EmailTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TenantId == null && t.TemplateType == typeString, ct);

            if (globalTemplate is not null)
                return ApplyPlaceholders(globalTemplate.Body, context);

            // 3. Built-in hardcoded default
            return _builtIn.GetBody(type, context);
        }

        // ── Global CRUD ───────────────────────────────────────────────────────

        public async Task<EmailTemplate?> GetGlobalTemplateAsync(EmailTemplateType type, CancellationToken ct = default)
        {
            var typeString = type.ToString();
            return await _context.EmailTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TenantId == null && t.TemplateType == typeString, ct);
        }

        public async Task<EmailTemplate> UpsertGlobalTemplateAsync(
            EmailTemplateType type, string body, Guid actorUserId, CancellationToken ct = default)
        {
            AssertValid(type, body);

            var typeString = type.ToString();
            var existing = await _context.EmailTemplates
                .FirstOrDefaultAsync(t => t.TenantId == null && t.TemplateType == typeString, ct);

            if (existing is null)
            {
                existing = new EmailTemplate
                {
                    Id          = Guid.NewGuid(),
                    TenantId    = null,
                    TemplateType = typeString,
                    Body        = body,
                    CreatedBy   = actorUserId,
                    CreatedAt   = DateTime.UtcNow
                };
                _context.EmailTemplates.Add(existing);
            }
            else
            {
                existing.Body      = body;
                existing.UpdatedBy = actorUserId;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(ct);
            return existing;
        }

        public async Task DeleteGlobalTemplateAsync(EmailTemplateType type, CancellationToken ct = default)
        {
            var typeString = type.ToString();
            var template = await _context.EmailTemplates
                .FirstOrDefaultAsync(t => t.TenantId == null && t.TemplateType == typeString, ct);

            if (template is null)
                throw new InvalidOperationException($"No global template found for type '{typeString}'.");

            _context.EmailTemplates.Remove(template);
            await _context.SaveChangesAsync(ct);
        }

        // ── Tenant CRUD ───────────────────────────────────────────────────────

        public async Task<EmailTemplate?> GetTenantTemplateAsync(
            EmailTemplateType type, Guid tenantId, CancellationToken ct = default)
        {
            var typeString = type.ToString();
            return await _context.EmailTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TemplateType == typeString, ct);
        }

        public async Task<EmailTemplate> UpsertTenantTemplateAsync(
            EmailTemplateType type, Guid tenantId, string body, Guid actorUserId, CancellationToken ct = default)
        {
            AssertValid(type, body);

            var typeString = type.ToString();
            var existing = await _context.EmailTemplates
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TemplateType == typeString, ct);

            if (existing is null)
            {
                existing = new EmailTemplate
                {
                    Id           = Guid.NewGuid(),
                    TenantId     = tenantId,
                    TemplateType = typeString,
                    Body         = body,
                    CreatedBy    = actorUserId,
                    CreatedAt    = DateTime.UtcNow
                };
                _context.EmailTemplates.Add(existing);
            }
            else
            {
                existing.Body      = body;
                existing.UpdatedBy = actorUserId;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(ct);
            return existing;
        }

        public async Task DeleteTenantTemplateAsync(
            EmailTemplateType type, Guid tenantId, CancellationToken ct = default)
        {
            var typeString = type.ToString();
            var template = await _context.EmailTemplates
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TemplateType == typeString, ct);

            if (template is null)
                throw new InvalidOperationException($"No tenant template found for type '{typeString}' and tenant '{tenantId}'.");

            _context.EmailTemplates.Remove(template);
            await _context.SaveChangesAsync(ct);
        }

        // ── Preview ───────────────────────────────────────────────────────────

        public PreviewResult RenderPreview(EmailTemplateType type, string body)
        {
            AssertValid(type, body);

            var sampleContext = new EmailTemplateRenderContext(
                UserName:         "John Doe",
                UserEmail:        "john.doe@example.com",
                TenantName:       "Acme Corporation",
                CompanyName:      "Acme Corp \u2014 HQ",
                VerificationLink: "https://auth.example.com/verify?token=sample-token-abc123",
                TwoFactorCode:    "482910",
                PasswordResetLink: "https://auth.example.com/reset-password?token=sample-reset",
                InvitationLink:   "https://auth.example.com/invite/sample-invite-token",
                InviterName:      "Jane Smith"
            );

            var rendered = ApplyPlaceholders(body, sampleContext);

            var warnings = Regex.Matches(rendered, @"\{\{\w+\}\}")
                .Select(m => m.Value)
                .Distinct()
                .ToList();

            return new PreviewResult(rendered, warnings);
        }

        // ── Validation ────────────────────────────────────────────────────────

        public IReadOnlyList<string> ValidateTemplate(EmailTemplateType type, string body)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(body))
            {
                errors.Add("Template body is required.");
                return errors;
            }

            if (body.Length > MaxBodyLength)
                errors.Add($"Template body must not exceed {MaxBodyLength:N0} characters.");

            if (RequiredPlaceholders.TryGetValue(type, out var required) && !body.Contains(required))
                errors.Add($"Template for '{type}' must contain the placeholder {required}.");

            return errors;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ApplyPlaceholders(string template, EmailTemplateRenderContext ctx)
        {
            return template
                .Replace("{{VerificationLink}}",  ctx.VerificationLink  ?? string.Empty)
                .Replace("{{TwoFactorCode}}",      ctx.TwoFactorCode     ?? string.Empty)
                .Replace("{{PasswordResetLink}}",  ctx.PasswordResetLink ?? string.Empty)
                .Replace("{{InvitationLink}}",     ctx.InvitationLink    ?? string.Empty)
                .Replace("{{InviterName}}",        ctx.InviterName       ?? string.Empty)
                .Replace("{{UserName}}",           ctx.UserName          ?? string.Empty)
                .Replace("{{UserEmail}}",          ctx.UserEmail         ?? string.Empty)
                .Replace("{{TenantName}}",         ctx.TenantName        ?? string.Empty)
                .Replace("{{CompanyName}}",        ctx.CompanyName       ?? string.Empty);
        }

        private void AssertValid(EmailTemplateType type, string body)
        {
            var errors = ValidateTemplate(type, body);
            if (errors.Count > 0)
                throw new ArgumentException(string.Join(" ", errors), nameof(body));
        }
    }
}
