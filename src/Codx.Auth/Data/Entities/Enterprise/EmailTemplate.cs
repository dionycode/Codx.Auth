using System;

namespace Codx.Auth.Data.Entities.Enterprise
{
    public class EmailTemplate
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string TemplateType { get; set; }
        public string Body { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public Tenant Tenant { get; set; }
    }
}
