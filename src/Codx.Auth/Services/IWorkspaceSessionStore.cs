using Codx.Auth.Data.Entities.Enterprise;
using System;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public interface IWorkspaceSessionStore
    {
        Task<WorkspaceSession> CreateAsync(WorkspaceSession session);
        Task<WorkspaceSession> GetActiveAsync(Guid userId, string clientId);
        Task RevokeAsync(Guid sessionId);
        Task RevokeAllForUserClientAsync(Guid userId, string clientId);
    }
}
