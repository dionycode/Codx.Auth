using Codx.Auth.Helpers.CustomAttributes;
using Codx.Auth.ViewModels.Account;
using System.ComponentModel.DataAnnotations;

namespace Codx.Auth.ViewModels
{
    public class RegisterViewModel : RegisterBaseModel
    {
        public string ReturnUrl { get; set; }
        /// <summary>True when registration is driven by an invitation cookie. Email field is pre-filled and read-only.</summary>
        public bool IsInviteMode { get; set; }
    }
}
