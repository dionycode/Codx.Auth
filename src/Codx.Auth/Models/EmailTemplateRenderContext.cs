namespace Codx.Auth.Models
{
    public record EmailTemplateRenderContext(
        string UserName,
        string UserEmail,
        string TenantName,
        string CompanyName,
        string? VerificationLink  = null,
        string? TwoFactorCode     = null,
        string? PasswordResetLink = null,
        string? InvitationLink    = null,
        string? InviterName       = null
    );
}
