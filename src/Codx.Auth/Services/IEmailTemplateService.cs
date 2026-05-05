using Codx.Auth.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Codx.Auth.Data.Entities.Enterprise;

namespace Codx.Auth.Services
{
    public interface IEmailTemplateService
    {
        Task<string> GetResolvedBodyAsync(
            EmailTemplateType type,
            Guid? tenantId,
            EmailTemplateRenderContext context,
            CancellationToken ct = default);

        Task<EmailTemplate?> GetGlobalTemplateAsync(EmailTemplateType type, CancellationToken ct = default);
        Task<EmailTemplate> UpsertGlobalTemplateAsync(EmailTemplateType type, string body, Guid actorUserId, CancellationToken ct = default);
        Task DeleteGlobalTemplateAsync(EmailTemplateType type, CancellationToken ct = default);

        Task<EmailTemplate?> GetTenantTemplateAsync(EmailTemplateType type, Guid tenantId, CancellationToken ct = default);
        Task<EmailTemplate> UpsertTenantTemplateAsync(EmailTemplateType type, Guid tenantId, string body, Guid actorUserId, CancellationToken ct = default);
        Task DeleteTenantTemplateAsync(EmailTemplateType type, Guid tenantId, CancellationToken ct = default);

        string RenderPreview(EmailTemplateType type, string body);

        IReadOnlyList<string> ValidateTemplate(EmailTemplateType type, string body);
    }
}
