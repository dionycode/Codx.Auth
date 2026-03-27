using System;
using System.Collections.Generic;

namespace Codx.Auth.Data.Entities.Enterprise
{
    /// <summary>
    /// Represents a registered application (distinct from the ASP.NET Identity ApplicationRole).
    /// Stores the application registry used for application-level role assignments.
    /// </summary>
    public class EnterpriseApplication
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public bool AllowSelfRegistration { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedByUserId { get; set; }

        public ICollection<EnterpriseApplicationRole> Roles { get; set; }
    }
}
