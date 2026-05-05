using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Models;
using Codx.Auth.Models.Email;
using Codx.Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public class InvitationService : IInvitationService
    {
        private readonly UserDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IEmailTemplateService _templateService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;
        private readonly ILogger<InvitationService> _logger;
        private readonly string _baseUrl;
        private readonly int _expiryDays;

        public InvitationService(
            UserDbContext context,
            IEmailService emailService,
            IEmailTemplateService templateService,
            UserManager<ApplicationUser> userManager,
            IAuditService auditService,
            ILogger<InvitationService> logger,
            IConfiguration configuration)
        {
            _context = context;
            _emailService = emailService;
            _templateService = templateService;
            _userManager = userManager;
            _auditService = auditService;
            _logger = logger;
            _baseUrl = configuration["BaseUrl"] ?? "https://localhost";
            _expiryDays = configuration.GetValue<int>("InvitationExpiryDays", 7);
        }

        public async Task<(bool success, string error)> CreateInvitationAsync(
            Guid tenantId, Guid? companyId, IReadOnlyList<int> roleIds, string email, Guid invitedByUserId)
        {
            if (roleIds == null || roleIds.Count == 0)
                return (false, "At least one role must be specified.");

            // Validate all role IDs exist and are active
            var roles = await _context.WorkspaceRoleDefinitions
                .Where(r => roleIds.Contains(r.Id) && r.IsActive)
                .ToListAsync();
            if (roles.Count != roleIds.Count)
                return (false, "One or more role IDs are invalid or inactive.");

            // All roles must share the same scope type
            var scopeTypes = roles.Select(r => r.ScopeType).Distinct().ToList();
            if (scopeTypes.Count > 1)
                return (false, "All roles in one invitation must share the same scope type.");

            var scopeType = scopeTypes[0];
            if (scopeType == "Tenant" && companyId.HasValue)
                return (false, "Tenant-scoped roles cannot be combined with a company context.");
            if (scopeType == "Company" && !companyId.HasValue)
                return (false, "Company-scoped roles require a company context.");

            var rawBytes = RandomNumberGenerator.GetBytes(32);
            var rawToken = Convert.ToBase64String(rawBytes)
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var tokenHash = ComputeSha256(rawToken);

            var invitation = new Invitation
            {
                Id = Guid.NewGuid(),
                Email = email,
                TenantId = tenantId,
                CompanyId = companyId,
                InviteTokenHash = tokenHash,
                Status = "Pending",
                ExpiresAt = DateTime.UtcNow.AddDays(_expiryDays),
                InvitedByUserId = invitedByUserId,
                CreatedAt = DateTime.UtcNow,
                InvitationRoles = roleIds.Select(rid => new InvitationRole
                {
                    Id = Guid.NewGuid(),
                    RoleId = rid
                }).ToList()
            };

            await _context.Invitations.AddAsync(invitation);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync("InvitationCreated",
                actorUserId: invitedByUserId,
                tenantId: tenantId,
                companyId: companyId,
                resourceType: "Invitation",
                resourceId: invitation.Id.ToString());

            try
            {
                var inviteUrl = $"{_baseUrl.TrimEnd('/')}/invite/{rawToken}";

                var inviter = await _userManager.FindByIdAsync(invitedByUserId.ToString());
                var inviterName = inviter is not null
                    ? $"{inviter.GivenName} {inviter.FamilyName}".Trim()
                    : string.Empty;

                var renderContext = new EmailTemplateRenderContext(
                    UserName:      email,
                    UserEmail:     email,
                    TenantName:    string.Empty,
                    CompanyName:   string.Empty,
                    InvitationLink: inviteUrl,
                    InviterName:   inviterName
                );

                var ct = CancellationToken.None;
                var body = await _templateService.GetResolvedBodyAsync(
                    EmailTemplateType.Invitation, tenantId, renderContext, ct);

                await _emailService.SendEmailAsync(new EmailMessage
                {
                    To      = email,
                    Subject = "You have been invited",
                    Body    = body,
                    IsHtml  = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send invitation email to {Email}", email);
                // Do not fail — invitation is created even if email delivery fails
            }

            return (true, null);
        }

        public async Task<InvitationValidationResult> ValidateInviteTokenAsync(string rawToken)
        {
            var tokenHash = ComputeSha256(rawToken);
            var invitation = await _context.Invitations
                .Include(i => i.InvitationRoles)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InviteTokenHash == tokenHash);

            if (invitation == null)
                return new InvitationValidationResult { IsValid = false, ErrorCode = "not_found" };
            if (invitation.Status != "Pending")
                return new InvitationValidationResult { IsValid = false, ErrorCode = invitation.Status.ToLowerInvariant() };
            if (invitation.ExpiresAt < DateTime.UtcNow)
                return new InvitationValidationResult { IsValid = false, ErrorCode = "expired" };

            return new InvitationValidationResult
            {
                IsValid = true,
                InvitationId = invitation.Id,
                Email = invitation.Email,
                TenantId = invitation.TenantId,
                CompanyId = invitation.CompanyId,
                RoleIds = invitation.InvitationRoles.Select(r => r.RoleId).ToList()
            };
        }

        public async Task<Invitation> GetByIdAsync(Guid invitationId)
        {
            return await _context.Invitations
                .Include(i => i.InvitationRoles)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == invitationId);
        }

        public async Task<(bool success, string error)> AcceptInvitationAsync(Guid invitationId, Guid userId)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var invitation = await _context.Invitations
                    .Include(i => i.InvitationRoles)
                    .FirstOrDefaultAsync(i => i.Id == invitationId);

                if (invitation == null)
                    return (false, "Invitation not found.");
                if (invitation.Status != "Pending")
                    return (false, $"Invitation is {invitation.Status.ToLowerInvariant()}.");
                if (invitation.ExpiresAt < DateTime.UtcNow)
                    return (false, "Invitation has expired.");

                var membership = new UserMembership
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TenantId = invitation.TenantId,
                    CompanyId = invitation.CompanyId,
                    Status = "Active",
                    JoinedAt = DateTime.UtcNow,
                    MembershipRoles = invitation.InvitationRoles.Select(ir => new UserMembershipRole
                    {
                        Id = Guid.NewGuid(),
                        RoleId = ir.RoleId,
                        Status = "Active",
                        AssignedAt = DateTime.UtcNow,
                        AssignedByUserId = invitation.InvitedByUserId
                    }).ToList()
                };

                invitation.Status = "Accepted";
                await _context.UserMemberships.AddAsync(membership);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                await _auditService.LogAsync("InvitationAccepted",
                    userId: userId,
                    actorUserId: userId,
                    tenantId: invitation.TenantId,
                    companyId: invitation.CompanyId,
                    resourceType: "Invitation",
                    resourceId: invitationId.ToString());

                return (true, null);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task<(bool success, string error)> RevokeInvitationAsync(Guid invitationId, Guid actorUserId)
        {
            var invitation = await _context.Invitations.FindAsync(invitationId);
            if (invitation == null)
                return (false, "Invitation not found.");
            if (invitation.Status != "Pending")
                return (false, $"Cannot revoke an invitation that is {invitation.Status.ToLowerInvariant()}.");

            invitation.Status = "Revoked";
            await _context.SaveChangesAsync();

            await _auditService.LogAsync("InvitationRevoked",
                actorUserId: actorUserId,
                tenantId: invitation.TenantId,
                companyId: invitation.CompanyId,
                resourceType: "Invitation",
                resourceId: invitationId.ToString());

            return (true, null);
        }

        private static string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
