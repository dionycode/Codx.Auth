using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
    }
}
