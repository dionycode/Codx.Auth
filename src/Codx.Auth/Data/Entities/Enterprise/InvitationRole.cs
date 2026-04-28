using System;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class InvitationRole
    {
        public Guid Id { get; set; }
        public Guid InvitationId { get; set; }
        public int RoleId { get; set; }

        public Invitation Invitation { get; set; }
        public WorkspaceRoleDefinition RoleDefinition { get; set; }
    }
}
