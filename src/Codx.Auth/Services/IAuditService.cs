using System;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public interface IAuditService
    {
        /// <summary>
        /// Logs an authorization-critical event. Write failures are silently caught, logged to
        /// <see cref="Microsoft.Extensions.Logging.ILogger"/>, and never thrown to the caller.
        /// </summary>
        Task LogAsync(
            string eventType,
            Guid? userId = null,
            Guid? actorUserId = null,
            Guid? tenantId = null,
            Guid? companyId = null,
            string resourceType = null,
            string resourceId = null,
            string details = null,
            string clientId = null,
            string ipAddress = null);
    }
}
