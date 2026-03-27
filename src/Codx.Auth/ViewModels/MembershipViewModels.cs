using Codx.Auth.Helpers.CustomAttributes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels
{
    public class MembershipCreateViewModel
    {
        [Required]
        public Guid TenantId { get; set; }

        public Guid? CompanyId { get; set; }

        public Guid? UserId { get; set; }

        [CustomEmailAddress(ErrorMessage = "Please enter a valid email address")]
        [StringLength(256)]
        [Display(Name = "User Email")]
        public string UserEmail { get; set; }

        public string UserName { get; set; }

        [Required(ErrorMessage = "At least one role must be selected.")]
        [Display(Name = "Roles")]
        public List<int> SelectedRoleIds { get; set; } = new List<int>();

        /// <summary>When true, only tenant-scoped roles are offered and CompanyId must be null.</summary>
        public bool TenantScopeOnly { get; set; }
    }
}
