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
        public DateTime CreatedAt { get; set; }

        public EnterpriseApplication Application { get; set; }
        public ICollection<UserApplicationRole> UserAssignments { get; set; }
    }
}
