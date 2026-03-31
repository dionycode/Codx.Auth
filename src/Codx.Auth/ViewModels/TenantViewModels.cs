using Codx.Auth.Helpers.CustomAttributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels
{
    public class TenantDetailsViewModel : BaseTenantViewModel
    {
        public Guid  Id { get; set; }
        public bool IsOwner { get; set; }
    }

    public class TenantAddViewModel : BaseTenantViewModel
    { 
    }

    public class TenantEditViewModel : BaseTenantViewModel
    {
        public Guid Id { get; set; }
    }
    public class BaseTenantViewModel
    {

        [Required]
        [StringLength(100)]
        public string Name { get; set; }
        [StringLength(100)]
        [CustomEmailAddress(ErrorMessage = "Please enter a valid email address")]
        public string Email { get; set; }
        [StringLength(15)]
        public string Phone { get; set; }
        [StringLength(200)]
        public string Address { get; set; }
        [StringLength(200)]
        public string Logo { get; set; }
        [StringLength(50)]
        public string Theme { get; set; }
        [StringLength(500)]
        public string Description { get; set; }
    }

}
