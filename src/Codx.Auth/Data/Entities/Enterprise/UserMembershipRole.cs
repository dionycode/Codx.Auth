using System;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class UserMembershipRole
    {
        public Guid Id { get; set; }
        public Guid MembershipId { get; set; }
        public int RoleId { get; set; }
        public string Status { get; set; }
        public DateTime AssignedAt { get; set; }
        public Guid AssignedByUserId { get; set; }

        public UserMembership Membership { get; set; }
        public WorkspaceRoleDefinition RoleDefinition { get; set; }
    }
}
