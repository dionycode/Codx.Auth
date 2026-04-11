using System;
using System.Collections.Generic;

namespace Codx.Auth.Data.Entities.Enterprise
{
    /// <summary>
    /// Represents an application-level role for an <see cref="EnterpriseApplication"/>.
    /// Distinct from the ASP.NET Identity ApplicationRole in Data.Entities.AspNet.
    /// </summary>
    public class EnterpriseApplicationRole
    {
        public Guid Id { get; set; }
        public string ApplicationId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        /// <summary>
        /// When true, this role is automatically assigned to any user who gains membership
        /// in the workspace (tenant/company) and has not yet been assigned any role for this
        /// application. Provides a sensible minimum access level without requiring manual setup.
        /// </summary>
        public bool IsDefault { get; set; }
        public DateTime CreatedAt { get; set; }

        public EnterpriseApplication Application { get; set; }
        public ICollection<UserApplicationRole> UserAssignments { get; set; }
    }
}
