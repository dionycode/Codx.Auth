using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.Enterprise;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public class MembershipResolver : IMembershipResolver
    {
        private readonly UserDbContext _context;

        public MembershipResolver(UserDbContext context)
        {
            _context = context;
        }

        public async Task<UserMembership> ResolveMembershipAsync(Guid userId, Guid tenantId, Guid? companyId = null)
        {
            return await _context.UserMemberships
                .AsNoTracking()
                .FirstOrDefaultAsync(m =>
                    m.UserId == userId &&
                    m.TenantId == tenantId &&
                    m.CompanyId == companyId &&
                    m.Status == "Active");
        }

        public async Task<IReadOnlyList<string>> ResolveWorkspaceRolesAsync(Guid membershipId)
        {
            var codes = await _context.UserMembershipRoles
                .AsNoTracking()
                .Where(umr => umr.MembershipId == membershipId && umr.Status == "Active")
                .Join(
                    _context.WorkspaceRoleDefinitions.Where(wrd => wrd.IsActive),
                    umr => umr.RoleId,
                    wrd => wrd.Id,
                    (umr, wrd) => wrd.Code)
                .ToListAsync();

            return codes.AsReadOnly();
        }

        public async Task<IReadOnlyList<UserMembership>> GetUserMembershipsAsync(Guid userId)
        {
            var memberships = await _context.UserMemberships
                .AsNoTracking()
                .Where(m => m.UserId == userId && m.Status == "Active")
                .Include(m => m.MembershipRoles)
                    .ThenInclude(mr => mr.RoleDefinition)
                .ToListAsync();

            return memberships.AsReadOnly();
        }
    }
}
