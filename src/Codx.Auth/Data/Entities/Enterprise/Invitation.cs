using System;
using System.Collections.Generic;

namespace Codx.Auth.Data.Entities.Enterprise
{
    /// <summary>
    /// Represents a workspace invitation. InviteTokenHash stores SHA-256(rawToken).
    /// The raw token is never persisted — it is sent to the invitee via email only.
    /// </summary>
    public class Invitation
    {
        public Guid Id { get; set; }
        public string Email { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CompanyId { get; set; }
        /// <summary>SHA-256 hash of the raw invite token. Never store the raw token.</summary>
        public string InviteTokenHash { get; set; }
        public string Status { get; set; }
        public DateTime ExpiresAt { get; set; }
        public Guid InvitedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }

        public ICollection<InvitationRole> InvitationRoles { get; set; }
    }
}
