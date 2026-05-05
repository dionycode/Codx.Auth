using Codx.Auth.Models;
using Codx.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers.API
{
    [ApiController]
    public class EmailTemplateApiController : ControllerBase
    {
        private readonly IEmailTemplateService _templateService;

        public EmailTemplateApiController(IEmailTemplateService templateService)
        {
            _templateService = templateService;
        }

        // ── Global endpoints ─────────────────────────────────────────────────

        [HttpGet("api/v1/email-templates/{type}")]
        [Authorize(Policy = "PlatformAdmin")]
        public async Task<IActionResult> GetGlobal(string type, CancellationToken ct)
        {
            if (!TryParseType(type, out var templateType))
                return BadRequest(new { error = $"Unrecognized template type '{type}'." });

            var template = await _templateService.GetGlobalTemplateAsync(templateType, ct);

            return Ok(new
            {
                templateType = templateType.ToString(),
                body         = template?.Body,
                isBuiltIn    = template is null,
                createdBy    = template?.CreatedBy,
                createdAt    = template?.CreatedAt,
                updatedBy    = template?.UpdatedBy,
                updatedAt    = template?.UpdatedAt
            });
        }

        [HttpPut("api/v1/email-templates/{type}")]
        [Authorize(Policy = "PlatformAdmin")]
        public async Task<IActionResult> UpsertGlobal(string type, [FromBody] UpsertEmailTemplateRequest request, CancellationToken ct)
        {
            if (!TryParseType(type, out var templateType))
                return BadRequest(new { error = $"Unrecognized template type '{type}'." });

            var errors = _templateService.ValidateTemplate(templateType, request.Body);
            if (errors.Count > 0)
                return ValidationProblem(BuildValidationProblem(errors));

            var actorId = GetActorId();
            if (actorId is null)
                return Unauthorized();

            var template = await _templateService.UpsertGlobalTemplateAsync(templateType, request.Body, actorId.Value, ct);

            return Ok(new
            {
                templateType = template.TemplateType,
                body         = template.Body,
                isBuiltIn    = false,
                createdBy    = template.CreatedBy,
                createdAt    = template.CreatedAt,
                updatedBy    = template.UpdatedBy,
                updatedAt    = template.UpdatedAt
            });
        }

        [HttpDelete("api/v1/email-templates/{type}")]
        [Authorize(Policy = "PlatformAdmin")]
        public async Task<IActionResult> DeleteGlobal(string type, CancellationToken ct)
        {
            if (!TryParseType(type, out var templateType))
                return BadRequest(new { error = $"Unrecognized template type '{type}'." });

            var template = await _templateService.GetGlobalTemplateAsync(templateType, ct);
            if (template is null)
                return NotFound(new { error = $"No global template exists for type '{type}'." });

            await _templateService.DeleteGlobalTemplateAsync(templateType, ct);
            return NoContent();
        }

        // ── Tenant endpoints ─────────────────────────────────────────────────

        [HttpGet("api/v1/tenants/{tenantId}/email-templates/{type}")]
        [Authorize(Policy = "TenantAdminRole")]
        public async Task<IActionResult> GetTenant(Guid tenantId, string type, CancellationToken ct)
        {
            if (!TryParseType(type, out var templateType))
                return BadRequest(new { error = $"Unrecognized template type '{type}'." });

            if (!IsPlatformAdmin() && !CallerOwnsTenant(tenantId))
                return Forbid();

            var template = await _templateService.GetTenantTemplateAsync(templateType, tenantId, ct);

            return Ok(new
            {
                tenantId     = tenantId,
                templateType = templateType.ToString(),
                body         = template?.Body,
                isBuiltIn    = template is null,
                createdBy    = template?.CreatedBy,
                createdAt    = template?.CreatedAt,
                updatedBy    = template?.UpdatedBy,
                updatedAt    = template?.UpdatedAt
            });
        }

        [HttpPut("api/v1/tenants/{tenantId}/email-templates/{type}")]
        [Authorize(Policy = "TenantAdminRole")]
        public async Task<IActionResult> UpsertTenant(Guid tenantId, string type, [FromBody] UpsertEmailTemplateRequest request, CancellationToken ct)
        {
            if (!TryParseType(type, out var templateType))
                return BadRequest(new { error = $"Unrecognized template type '{type}'." });

            if (!IsPlatformAdmin() && !CallerOwnsTenant(tenantId))
                return Forbid();

            var errors = _templateService.ValidateTemplate(templateType, request.Body);
            if (errors.Count > 0)
                return ValidationProblem(BuildValidationProblem(errors));

            var actorId = GetActorId();
            if (actorId is null)
                return Unauthorized();

            var template = await _templateService.UpsertTenantTemplateAsync(templateType, tenantId, request.Body, actorId.Value, ct);

            return Ok(new
            {
                tenantId     = tenantId,
                templateType = template.TemplateType,
                body         = template.Body,
                isBuiltIn    = false,
                createdBy    = template.CreatedBy,
                createdAt    = template.CreatedAt,
                updatedBy    = template.UpdatedBy,
                updatedAt    = template.UpdatedAt
            });
        }

        [HttpDelete("api/v1/tenants/{tenantId}/email-templates/{type}")]
        [Authorize(Policy = "TenantAdminRole")]
        public async Task<IActionResult> DeleteTenant(Guid tenantId, string type, CancellationToken ct)
        {
            if (!TryParseType(type, out var templateType))
                return BadRequest(new { error = $"Unrecognized template type '{type}'." });

            if (!IsPlatformAdmin() && !CallerOwnsTenant(tenantId))
                return Forbid();

            var template = await _templateService.GetTenantTemplateAsync(templateType, tenantId, ct);
            if (template is null)
                return NotFound(new { error = $"No tenant template found for type '{type}' and tenant '{tenantId}'." });

            await _templateService.DeleteTenantTemplateAsync(templateType, tenantId, ct);
            return NoContent();
        }

        // ── Preview ──────────────────────────────────────────────────────────

        [HttpPost("api/v1/email-templates/preview")]
        [Authorize(Policy = "EmailTemplateAccess")]
        public IActionResult Preview([FromBody] PreviewEmailTemplateRequest request)
        {
            if (!Enum.TryParse<EmailTemplateType>(request.TemplateType, ignoreCase: false, out var templateType))
                return BadRequest(new { error = $"Unrecognized templateType '{request.TemplateType}'." });

            var errors = _templateService.ValidateTemplate(templateType, request.Body);
            if (errors.Count > 0)
                return ValidationProblem(BuildValidationProblem(errors));

            var result = _templateService.RenderPreview(templateType, request.Body);

            return Ok(new { rendered = result.Rendered, warnings = result.UnrecognizedPlaceholders });
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool TryParseType(string slug, out EmailTemplateType result)
        {
            result = default;
            return slug switch
            {
                "email-verification" => (result = EmailTemplateType.EmailVerification) == EmailTemplateType.EmailVerification,
                "two-factor"         => (result = EmailTemplateType.TwoFactor) == EmailTemplateType.TwoFactor,
                "password-reset"     => (result = EmailTemplateType.PasswordReset) == EmailTemplateType.PasswordReset,
                "invitation"         => (result = EmailTemplateType.Invitation) == EmailTemplateType.Invitation,
                _                    => false
            };
        }

        private bool IsPlatformAdmin() =>
            User.IsInRole("PlatformAdministrator");

        private bool CallerOwnsTenant(Guid tenantId)
        {
            var claim = User.FindFirst("tenant_id")?.Value;
            return Guid.TryParse(claim, out var jwtTenantId) && jwtTenantId == tenantId;
        }

        private Guid? GetActorId()
        {
            var sub = User.FindFirst("sub")?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }

        private static Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary BuildValidationProblem(
            System.Collections.Generic.IReadOnlyList<string> errors)
        {
            var modelState = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary();
            foreach (var error in errors)
                modelState.AddModelError("body", error);
            return modelState;
        }

        private static System.Collections.Generic.List<string> DetectUnrecognizedPlaceholders(string body)
        {
            var known = new System.Collections.Generic.HashSet<string>
            {
                "{{VerificationLink}}", "{{TwoFactorCode}}",
                "{{PasswordResetLink}}", "{{InvitationLink}}", "{{InviterName}}",
                "{{UserName}}", "{{UserEmail}}", "{{TenantName}}", "{{CompanyName}}"
            };

            var warnings = new System.Collections.Generic.List<string>();
            var matches = System.Text.RegularExpressions.Regex.Matches(body, @"\{\{[^}]+\}\}");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (!known.Contains(match.Value))
                    warnings.Add(match.Value);
            }

            return warnings;
        }
    }

    public sealed class UpsertEmailTemplateRequest
    {
        public required string Body { get; init; }
    }

    public sealed class PreviewEmailTemplateRequest
    {
        public required string TemplateType { get; init; }
        public required string Body { get; init; }
    }
}
