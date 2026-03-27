using Codx.Auth.Data.Entities.AspNet;
using System;
using System.Collections.Generic;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class UserMembership
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CompanyId { get; set; }
        public string Status { get; set; }
        public DateTime JoinedAt { get; set; }

        public ApplicationUser User { get; set; }
        public Tenant Tenant { get; set; }
        public Company Company { get; set; }
        public ICollection<UserMembershipRole> MembershipRoles { get; set; }
    }
}
