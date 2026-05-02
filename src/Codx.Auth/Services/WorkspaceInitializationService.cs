using Codx.Auth.Data.Contexts;
using Codx.Auth.Data.Entities.AspNet;
using Codx.Auth.Data.Entities.Enterprise;
using Codx.Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public class WorkspaceInitializationService : IWorkspaceInitializationService
    {
        private readonly UserDbContext _userDbContext;
        private readonly IdentityServerDbContext _isDbContext;
        private readonly IAuditService _auditService;
        private readonly ILogger<WorkspaceInitializationService> _logger;

        public WorkspaceInitializationService(
            UserDbContext userDbContext,
            IdentityServerDbContext isDbContext,
            IAuditService auditService,
            ILogger<WorkspaceInitializationService> logger)
        {
            _userDbContext = userDbContext;
            _isDbContext = isDbContext;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task InitializeUserForApplicationAsync(
            ApplicationUser user,
            string clientId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Step 1: Resolve the EnterpriseApplication.Id from the OIDC client's application_id property.
                var appId = await _isDbContext.ClientProperties
                    .Where(cp => cp.Key == "application_id" && cp.Client.ClientId == clientId)
                    .Select(cp => cp.Value)
                    .FirstOrDefaultAsync(cancellationToken);

                if (string.IsNullOrEmpty(appId))
                {
                    _logger.LogInformation(
                        "WorkspaceInitializationService: client '{ClientId}' has no application_id property. Skipping workspace init for user {UserId}.",
                        clientId, user.Id);
                    return;
                }

                // Step 2: Load the EnterpriseApplication.
                var application = await _userDbContext.EnterpriseApplications
                    .FirstOrDefaultAsync(a => a.Id == appId && a.IsActive, cancellationToken);

                if (application == null)
                {
                    _logger.LogWarning(
                        "WorkspaceInitializationService: EnterpriseApplication '{AppId}' not found or inactive. Skipping workspace init for user {UserId}.",
                        appId, user.Id);
                    return;
                }

                // Step 3: Load the default role for this application.
                var defaultRole = await _userDbContext.EnterpriseApplicationRoles
                    .Where(r => r.ApplicationId == appId && r.IsDefault && r.IsActive)
                    .FirstOrDefaultAsync(cancellationToken);

                if (defaultRole == null)
                {
                    _logger.LogInformation(
                        "WorkspaceInitializationService: No default active role configured for application '{AppId}'. Skipping workspace init for user {UserId}.",
                        appId, user.Id);
                    return;
                }

                // Step 4: Load the company-scoped UserMembership for this user.
                var membership = await _userDbContext.UserMemberships
                    .Where(m => m.UserId == user.Id && m.CompanyId != null)
                    .OrderByDescending(m => m.JoinedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (membership == null)
                {
                    _logger.LogWarning(
                        "WorkspaceInitializationService: No company-scoped membership found for user {UserId}. Skipping workspace init.",
                        user.Id);
                    return;
                }

                // Step 5: Idempotency check — do not insert a duplicate role assignment.
                var alreadyAssigned = await _userDbContext.UserApplicationRoles
                    .AnyAsync(
                        r => r.UserId == user.Id
                             && r.ApplicationId == appId
                             && r.TenantId == membership.TenantId
                             && r.CompanyId == membership.CompanyId.Value,
                        cancellationToken);

                if (alreadyAssigned)
                {
                    return;
                }

                // Step 6: Insert the default role assignment.
                var roleAssignment = new UserApplicationRole
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TenantId = membership.TenantId,
                    CompanyId = membership.CompanyId.Value,
                    ApplicationId = appId,
                    RoleId = defaultRole.Id,
                    AssignedAt = DateTime.UtcNow,
                    AssignedByUserId = user.Id
                };

                await _userDbContext.UserApplicationRoles.AddAsync(roleAssignment, cancellationToken);
                await _userDbContext.SaveChangesAsync(cancellationToken);

                // Step 7: Audit.
                await _auditService.LogAsync(
                    "WorkspaceRoleInitialized",
                    userId: user.Id,
                    actorUserId: user.Id,
                    tenantId: membership.TenantId,
                    companyId: membership.CompanyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "WorkspaceInitializationService: Unexpected error during workspace init for user {UserId} via client '{ClientId}'. Registration will continue.",
                    user.Id, clientId);
            }
        }
    }
}
