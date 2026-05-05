using Codx.Auth.Data.Contexts;
using Codx.Auth.Extensions;
using Codx.Auth.Models;
using Codx.Auth.Services;
using Codx.Auth.ViewModels.EmailTemplates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers
{
    [Authorize]
    [Route("[controller]")]
    public class EmailTemplatesController : Controller
    {
        private readonly IEmailTemplateService _templateService;
        private readonly UserDbContext _db;

        private static readonly Dictionary<string, string> TypeLabels = new()
        {
            ["EmailVerification"] = "Verification Email",
            ["TwoFactor"]         = "Two-Factor Email"
        };

        public EmailTemplatesController(IEmailTemplateService templateService, UserDbContext db)
        {
            _templateService = templateService;
            _db = db;
        }

        // GET /EmailTemplates           — Platform Admin (global)
        // GET /EmailTemplates?tenantId= — Tenant Admin (tenant override)
        [HttpGet]
        public async Task<IActionResult> Index(Guid? tenantId, CancellationToken ct)
        {
            if (!await IsAuthorizedAsync(tenantId))
                return Forbid();

            var rows = new List<EmailTemplateRowViewModel>();

            foreach (var type in new[] { EmailTemplateType.EmailVerification, EmailTemplateType.TwoFactor })
            {
                var typeString = type.ToString();
                string? body;
                string sourceLabel;
                bool canReset;

                if (tenantId.HasValue)
                {
                    var tenantTpl = await _templateService.GetTenantTemplateAsync(type, tenantId.Value, ct);
                    if (tenantTpl is not null)
                    {
                        body = tenantTpl.Body;
                        sourceLabel = "Custom";
                        canReset = true;
                    }
                    else
                    {
                        var globalTpl = await _templateService.GetGlobalTemplateAsync(type, ct);
                        body = globalTpl?.Body;
                        sourceLabel = globalTpl is not null ? "Global Default" : "Built-in";
                        canReset = false;
                    }
                }
                else
                {
                    var globalTpl = await _templateService.GetGlobalTemplateAsync(type, ct);
                    body = globalTpl?.Body;
                    sourceLabel = globalTpl is not null ? "Custom" : "Built-in";
                    canReset = globalTpl is not null;
                }

                rows.Add(new EmailTemplateRowViewModel
                {
                    TemplateType      = typeString,
                    TemplateTypeLabel = TypeLabels[typeString],
                    Body              = body,
                    SourceLabel       = sourceLabel,
                    CanReset          = canReset
                });
            }

            string? tenantName = null;
            if (tenantId.HasValue)
                tenantName = (await _db.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId.Value && !t.IsDeleted, ct))?.Name;

            return View(new EmailTemplateIndexViewModel
            {
                TenantId   = tenantId,
                TenantName = tenantName,
                Rows       = rows
            });
        }

        // GET /EmailTemplates/Edit?type=EmailVerification&tenantId=
        [HttpGet("Edit")]
        public async Task<IActionResult> Edit(string type, Guid? tenantId, CancellationToken ct)
        {
            if (!await IsAuthorizedAsync(tenantId))
                return Forbid();

            if (!TryParseType(type, out var templateType))
                return BadRequest("Unrecognized template type.");

            string body;
            string sourceLabel;

            if (tenantId.HasValue)
            {
                var tenantTpl = await _templateService.GetTenantTemplateAsync(templateType, tenantId.Value, ct);
                if (tenantTpl is not null)
                {
                    body = tenantTpl.Body;
                    sourceLabel = "Custom";
                }
                else
                {
                    var globalTpl = await _templateService.GetGlobalTemplateAsync(templateType, ct);
                    body = globalTpl?.Body ?? string.Empty;
                    sourceLabel = globalTpl is not null ? "Global Default" : "Built-in";
                }
            }
            else
            {
                var globalTpl = await _templateService.GetGlobalTemplateAsync(templateType, ct);
                body = globalTpl?.Body ?? string.Empty;
                sourceLabel = globalTpl is not null ? "Custom" : "Built-in";
            }

            string? tenantName = null;
            if (tenantId.HasValue)
                tenantName = (await _db.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId.Value && !t.IsDeleted, ct))?.Name ?? string.Empty;

            return View(new EmailTemplateEditViewModel
            {
                TenantId          = tenantId,
                TenantName        = tenantName ?? string.Empty,
                TemplateType      = type,
                TemplateTypeLabel = TypeLabels.GetValueOrDefault(type, type),
                Body              = body,
                SourceLabel       = sourceLabel
            });
        }

        // POST /EmailTemplates/Edit
        [HttpPost("Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EmailTemplateEditViewModel model, CancellationToken ct)
        {
            if (!await IsAuthorizedAsync(model.TenantId))
                return Forbid();

            if (!TryParseType(model.TemplateType, out var templateType))
                return BadRequest("Unrecognized template type.");

            var errors = _templateService.ValidateTemplate(templateType, model.Body);
            foreach (var error in errors)
                ModelState.AddModelError(nameof(model.Body), error);

            if (!ModelState.IsValid)
            {
                model.TemplateTypeLabel = TypeLabels.GetValueOrDefault(model.TemplateType, model.TemplateType);
                return View(model);
            }

            var actorId = GetActorId();
            if (actorId is null)
                return Unauthorized();

            if (model.TenantId.HasValue)
                await _templateService.UpsertTenantTemplateAsync(templateType, model.TenantId.Value, model.Body, actorId.Value, ct);
            else
                await _templateService.UpsertGlobalTemplateAsync(templateType, model.Body, actorId.Value, ct);

            TempData["Success"] = $"{TypeLabels.GetValueOrDefault(model.TemplateType, model.TemplateType)} template saved successfully.";
            return RedirectToAction(nameof(Index), new { tenantId = model.TenantId });
        }

        // GET /EmailTemplates/Delete?type=&tenantId=
        [HttpGet("Delete")]
        public async Task<IActionResult> Delete(string type, Guid? tenantId, CancellationToken ct)
        {
            if (!await IsAuthorizedAsync(tenantId))
                return Forbid();

            if (!TryParseType(type, out var templateType))
                return BadRequest("Unrecognized template type.");

            // Determine what the system falls back to after deletion
            string fallbackLabel;
            if (tenantId.HasValue)
            {
                var globalTpl = await _templateService.GetGlobalTemplateAsync(templateType, ct);
                fallbackLabel = globalTpl is not null ? "Global Default" : "Built-in Default";
            }
            else
            {
                fallbackLabel = "Built-in Default";
            }

            string? tenantName = null;
            if (tenantId.HasValue)
                tenantName = (await _db.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId.Value && !t.IsDeleted, ct))?.Name ?? string.Empty;

            return View(new EmailTemplateDeleteViewModel
            {
                TenantId          = tenantId,
                TenantName        = tenantName ?? string.Empty,
                TemplateType      = type,
                TemplateTypeLabel = TypeLabels.GetValueOrDefault(type, type),
                FallbackLabel     = fallbackLabel
            });
        }

        // POST /EmailTemplates/Delete
        [HttpPost("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(EmailTemplateDeleteViewModel model, CancellationToken ct)
        {
            if (!await IsAuthorizedAsync(model.TenantId))
                return Forbid();

            if (!TryParseType(model.TemplateType, out var templateType))
                return BadRequest("Unrecognized template type.");

            if (model.TenantId.HasValue)
                await _templateService.DeleteTenantTemplateAsync(templateType, model.TenantId.Value, ct);
            else
                await _templateService.DeleteGlobalTemplateAsync(templateType, ct);

            TempData["Success"] = $"{TypeLabels.GetValueOrDefault(model.TemplateType, model.TemplateType)} template reset to {model.FallbackLabel}.";
            return RedirectToAction(nameof(Index), new { tenantId = model.TenantId });
        }

        // POST /EmailTemplates/Preview  — AJAX, returns rendered HTML fragment
        [HttpPost("Preview")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Preview([FromForm] string type, [FromForm] string body, [FromForm] Guid? tenantId)
        {
            if (!await IsAuthorizedAsync(tenantId))
                return Forbid();

            if (!TryParseType(type, out var templateType))
                return BadRequest("Unrecognized template type.");

            var errors = _templateService.ValidateTemplate(templateType, body);
            if (errors.Count > 0)
                return BadRequest(string.Join(" ", errors));

            var rendered = _templateService.RenderPreview(templateType, body);
            return Content(rendered, "text/html");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool TryParseType(string slug, out EmailTemplateType result)
        {
            result = default;
            return slug switch
            {
                "EmailVerification" => (result = EmailTemplateType.EmailVerification) == EmailTemplateType.EmailVerification,
                "TwoFactor"         => (result = EmailTemplateType.TwoFactor) == EmailTemplateType.TwoFactor,
                _                   => false
            };
        }

        private async Task<bool> IsAuthorizedAsync(Guid? tenantId)
        {
            if (User.IsInRole("PlatformAdministrator"))
                return true;

            // Non-platform-admin must supply a tenantId and be a TENANT_OWNER or TENANT_ADMIN for it
            if (!tenantId.HasValue)
                return false;

            var userId = User.GetUserId();
            if (userId == Guid.Empty)
                return false;

            return await _db.UserMemberships.AnyAsync(m =>
                m.UserId == userId
                && m.TenantId == tenantId.Value
                && m.CompanyId == null
                && m.Status == "Active"
                && m.MembershipRoles.Any(r =>
                    r.Status == "Active" &&
                    (r.RoleDefinition.Code == "TENANT_OWNER" || r.RoleDefinition.Code == "TENANT_ADMIN")));
        }

        private Guid? GetActorId()
        {
            var sub = User.FindFirst("sub")?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }
}
