using Codx.Auth.Data.Entities.Enterprise;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels.Applications
{
    public class ApplicationDetailsViewModel
    {
        public EnterpriseApplication Application { get; set; }
        public List<string> LinkedClientIds { get; set; } = new List<string>();
        public List<Codx.Auth.Data.Entities.Enterprise.Tenant> AllTenants { get; set; } = new List<Codx.Auth.Data.Entities.Enterprise.Tenant>();
    }

    public class UserAssignmentRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserEmail { get; set; }
        public string UserDisplayName { get; set; }
        public Guid RoleId { get; set; }
        public string RoleName { get; set; }
        public DateTime AssignedAt { get; set; }
        public Guid AssignedByUserId { get; set; }
        public string AssignedByEmail { get; set; }
    }

    public class AssignUserRoleViewModel
    {
        public string AppId { get; set; }

        [Required]
        public Guid TenantId { get; set; }

        [Required]
        public Guid CompanyId { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [Required]
        public Guid RoleId { get; set; }
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
