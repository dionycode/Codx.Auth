using System;
using System.Collections.Generic;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class Company
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; }     
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string Logo { get; set; }
        public string Theme { get; set; }
        public string Description { get; set; }
        /// <summary>Active | Suspended | Cancelled. Replaces legacy IsActive/IsDeleted flags (kept until RemoveLegacyStatusColumns migration).</summary>
        public string Status { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public Guid? UpdatedBy { get; set; }

        public Tenant Tenant { get; set; }
        public virtual ICollection<UserCompany> UserCompanies { get; set; }
        public ICollection<UserMembership> UserMemberships { get; set; }

    }
}
