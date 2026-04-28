using System;
using System.Collections.Generic;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class WorkspaceRoleDefinition
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string DisplayName { get; set; }
        public string ScopeType { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<UserMembershipRole> MembershipRoles { get; set; }
    }
}
