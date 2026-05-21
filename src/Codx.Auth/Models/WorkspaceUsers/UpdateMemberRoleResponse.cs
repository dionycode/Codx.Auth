using System;

namespace Codx.Auth.Models.WorkspaceUsers
{
    public sealed record UpdateMemberRoleResponse
    {
        public required Guid UserId { get; init; }
        public required string ApplicationRole { get; init; }
    }
}
