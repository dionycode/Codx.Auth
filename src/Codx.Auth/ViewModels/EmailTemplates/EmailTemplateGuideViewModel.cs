namespace Codx.Auth.ViewModels.EmailTemplates
{
    public class EmailTemplateGuideViewModel
    {
        public string EmailVerificationDefaultBody { get; set; } = string.Empty;
        public string TwoFactorDefaultBody         { get; set; } = string.Empty;
        public string PasswordResetDefaultBody     { get; set; } = string.Empty;
        public string InvitationDefaultBody        { get; set; } = string.Empty;
    }
}
