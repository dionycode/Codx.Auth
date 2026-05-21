using System;

namespace Codx.Auth.Models.WorkspaceUsers
{
    public sealed record RemoveMemberResponse
    {
        public required Guid UserId { get; init; }
        public required string Outcome { get; init; } // always "removed"
    }
}
