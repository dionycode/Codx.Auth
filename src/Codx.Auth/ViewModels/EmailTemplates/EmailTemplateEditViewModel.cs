using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels.EmailTemplates
{
    public class PlaceholderReferenceItem
    {
        public string Token { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
    }

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

        public IReadOnlyList<PlaceholderReferenceItem> PlaceholderReference { get; set; }
            = Array.Empty<PlaceholderReferenceItem>();
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
