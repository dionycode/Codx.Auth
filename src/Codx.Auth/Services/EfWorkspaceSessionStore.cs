using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public class EfWorkspaceSessionStore : IWorkspaceSessionStore
    {
        private readonly UserDbContext _context;

        public EfWorkspaceSessionStore(UserDbContext context)
        {
            _context = context;
        }

        public async Task<WorkspaceSession> CreateAsync(WorkspaceSession session)
        {
            _context.WorkspaceSessions.Add(session);
            await _context.SaveChangesAsync();
            return session;
        }

        public async Task<WorkspaceSession> GetActiveAsync(Guid userId, string clientId)
        {
            return await _context.WorkspaceSessions
                .FirstOrDefaultAsync(s =>
                    s.UserId == userId &&
                    s.ClientId == clientId &&
                    s.Status == "Active" &&
                    s.ExpiresAt > DateTime.UtcNow);
        }

        public async Task RevokeAsync(Guid sessionId)
        {
            var session = await _context.WorkspaceSessions.FindAsync(sessionId);
            if (session != null && session.Status == "Active")
            {
                session.Status = "Revoked";
                await _context.SaveChangesAsync();
            }
        }

        public async Task RevokeAllForUserClientAsync(Guid userId, string clientId)
        {
            var activeSessions = await _context.WorkspaceSessions
                .Where(s => s.UserId == userId && s.ClientId == clientId && s.Status == "Active")
                .ToListAsync();

            foreach (var session in activeSessions)
            {
                session.Status = "Revoked";
            }

            if (activeSessions.Count > 0)
            {
                await _context.SaveChangesAsync();
            }
        }
    }
}
