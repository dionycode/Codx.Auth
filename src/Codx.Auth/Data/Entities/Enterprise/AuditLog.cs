using System;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class AuditLog
    {
        public Guid Id { get; set; }
        public string EventType { get; set; }
        public Guid? UserId { get; set; }
        public Guid? ActorUserId { get; set; }
        public Guid? TenantId { get; set; }
        public Guid? CompanyId { get; set; }
        public string ResourceType { get; set; }
        public string ResourceId { get; set; }
        public string Details { get; set; }
        public DateTime OccurredAt { get; set; }
        public string ClientId { get; set; }
        public string IpAddress { get; set; }
    }
}
