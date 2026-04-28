using System;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class UserApplicationRole
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public Guid CompanyId { get; set; }
        public string ApplicationId { get; set; }
        public Guid RoleId { get; set; }
        public DateTime AssignedAt { get; set; }
        public Guid AssignedByUserId { get; set; }

        public EnterpriseApplicationRole Role { get; set; }
    }
}
