using Codx.Auth.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Infrastructure
{
    /// <summary>
    /// Background service that periodically marks expired Pending invitations as Expired.
    /// Runs once on startup and then every hour.
    /// </summary>
    public class InvitationExpiryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<InvitationExpiryService> _logger;
        private static readonly TimeSpan _interval = TimeSpan.FromHours(1);

        public InvitationExpiryService(IServiceScopeFactory scopeFactory, ILogger<InvitationExpiryService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExpireInvitationsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while expiring invitations");
                }
                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task ExpireInvitationsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();
            var now = DateTime.UtcNow;
            var expired = await db.Invitations
                .Where(i => i.Status == "Pending" && i.ExpiresAt < now)
                .ToListAsync(ct);

            if (expired.Count == 0) return;

            foreach (var inv in expired)
                inv.Status = "Expired";

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} invitation(s)", expired.Count);
        }
    }
}
