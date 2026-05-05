using System;
using System.Collections.Generic;

namespace Codx.Auth.ViewModels.EmailTemplates
{
    public class EmailTemplateIndexViewModel
    {
        public Guid? TenantId { get; set; }
        public string? TenantName { get; set; }
        public List<EmailTemplateRowViewModel> Rows { get; set; } = new();
    }

    public class EmailTemplateRowViewModel
    {
        public string TemplateType { get; set; } = string.Empty;
        public string TemplateTypeLabel { get; set; } = string.Empty;
        public string? Body { get; set; }
        public string SourceLabel { get; set; } = string.Empty;  // "Custom" | "Global Default" | "Built-in"
        public bool CanReset { get; set; }
    }
}
