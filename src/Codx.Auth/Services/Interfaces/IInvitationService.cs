using Codx.Auth.Data.Entities.Enterprise;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Codx.Auth.Services.Interfaces
{
    public class InvitationValidationResult
    {
        public bool IsValid { get; set; }
        /// <summary>not_found | pending | accepted | revoked | expired</summary>
        public string ErrorCode { get; set; }
        public Guid InvitationId { get; set; }
        public string Email { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CompanyId { get; set; }
        public IReadOnlyList<int> RoleIds { get; set; }
    }

    public interface IInvitationService
    {
        Task<(bool success, string error)> CreateInvitationAsync(
            Guid tenantId, Guid? companyId, IReadOnlyList<int> roleIds, string email, Guid invitedByUserId);

        Task<InvitationValidationResult> ValidateInviteTokenAsync(string rawToken);

        Task<Invitation> GetByIdAsync(Guid invitationId);

        Task<(bool success, string error)> AcceptInvitationAsync(Guid invitationId, Guid userId);

        Task<(bool success, string error)> RevokeInvitationAsync(Guid invitationId, Guid actorUserId);
    }
}
