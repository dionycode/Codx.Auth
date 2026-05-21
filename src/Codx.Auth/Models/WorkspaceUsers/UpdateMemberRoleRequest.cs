namespace Codx.Auth.Models.WorkspaceUsers
{
    public sealed record UpdateMemberRoleRequest
    {
        public required string ApplicationRole { get; init; }
    }
}
