using Codx.Auth.Data.Entities.Enterprise;
using System;
using System.Collections.Generic;

namespace Codx.Auth.ViewModels.Applications
{
    public class ApplicationDetailsViewModel
    {
        public EnterpriseApplication Application { get; set; }
        public List<string> LinkedClientIds { get; set; } = new List<string>();
    }

    public class ApplicationAddViewModel
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "ID must be lowercase letters, numbers, and hyphens only.")]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(200)]
        public string DisplayName { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(1000)]
        public string Description { get; set; }

        public bool AllowSelfRegistration { get; set; }
    }

    public class ApplicationRoleAddViewModel
    {
        public string ApplicationId { get; set; }
        public string ApplicationName { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string Description { get; set; }

        /// <summary>
        /// When true, new members who have no application role assigned will automatically
        /// receive this role on their first workspace token request.
        /// </summary>
        public bool IsDefault { get; set; }
    }

    public class ApplicationRoleEditViewModel
    {
        public Guid Id { get; set; }
        public string ApplicationId { get; set; }
        public string ApplicationName { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100)]
        public string Name { get; set; }

        [System.ComponentModel.DataAnnotations.StringLength(500)]
        public string Description { get; set; }

        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
    }
}
