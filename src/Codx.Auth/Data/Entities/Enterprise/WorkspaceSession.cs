using System;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class WorkspaceSession
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CompanyId { get; set; }
        public string ClientId { get; set; }
        public string WorkspaceContextType { get; set; }
        public string Status { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
