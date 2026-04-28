using Codx.Auth.Data.Entities.Enterprise;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public interface IMembershipResolver
    {
        Task<UserMembership> ResolveMembershipAsync(Guid userId, Guid tenantId, Guid? companyId = null);
        Task<IReadOnlyList<string>> ResolveWorkspaceRolesAsync(Guid membershipId);
        Task<IReadOnlyList<UserMembership>> GetUserMembershipsAsync(Guid userId);
    }
}
