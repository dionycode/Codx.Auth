using System;
using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels.EmailTemplates
{
    public class EmailTemplateEditViewModel
    {
        public Guid? TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;

        [Required]
        public string TemplateType { get; set; } = string.Empty;

        public string TemplateTypeLabel { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        public string SourceLabel { get; set; } = string.Empty;
    }

    public class EmailTemplateDeleteViewModel
    {
        public Guid? TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string TemplateType { get; set; } = string.Empty;
        public string TemplateTypeLabel { get; set; } = string.Empty;
        public string FallbackLabel { get; set; } = string.Empty;  // "Global Default" | "Built-in Default"
    }
}
