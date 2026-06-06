using System.Collections.Generic;

namespace Codx.Auth.Models.Responses
{
    public class TenantEmailTemplateListItemResponse
    {
        public string TemplateType { get; set; }
        public string DisplayName { get; set; }
        public string Source { get; set; }
        public bool HasOverride { get; set; }
        public string RequiredPlaceholder { get; set; }
        public List<string> AvailablePlaceholders { get; set; } = new List<string>();
    }
}
