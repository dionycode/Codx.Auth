using Codx.Auth.Data.Entities.Enterprise;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels.Invitations
{
    public class InvitationsIndexViewModel
    {
        public Guid TenantId { get; set; }
        public Guid? CompanyId { get; set; }
        public List<Invitation> Invitations { get; set; } = new List<Invitation>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    public class CreateInvitationViewModel
    {
        public Guid TenantId { get; set; }
        public Guid? CompanyId { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email Address")]
        public string Email { get; set; }

        [Display(Name = "Roles")]
        public List<int> SelectedRoleIds { get; set; } = new List<int>();

        public List<WorkspaceRoleDefinition> AvailableRoles { get; set; } = new List<WorkspaceRoleDefinition>();
    }
}
