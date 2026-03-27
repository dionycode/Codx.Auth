using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public class DbAuditService : IAuditService
    {
        private readonly UserDbContext _context;
        private readonly ILogger<DbAuditService> _logger;

        public DbAuditService(UserDbContext context, ILogger<DbAuditService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogAsync(
            string eventType,
            Guid? userId = null,
            Guid? actorUserId = null,
            Guid? tenantId = null,
            Guid? companyId = null,
            string resourceType = null,
            string resourceId = null,
            string details = null,
            string clientId = null,
            string ipAddress = null)
        {
            try
            {
                var entry = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    EventType = eventType,
                    UserId = userId,
                    ActorUserId = actorUserId,
                    TenantId = tenantId,
                    CompanyId = companyId,
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    Details = details,
                    ClientId = clientId,
                    IpAddress = ipAddress,
                    OccurredAt = DateTime.UtcNow
                };

                _context.AuditLogs.Add(entry);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Audit write failures must not propagate — log and continue.
                _logger.LogError(ex, "Failed to write audit log entry for event {EventType}", eventType);
            }
        }
    }
}
