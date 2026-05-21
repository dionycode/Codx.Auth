using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Codx.Auth.Authorization
{
    public class WorkspaceUserManagementRequirement : IAuthorizationRequirement { }

    public class WorkspaceUserManagementHandler
        : AuthorizationHandler<WorkspaceUserManagementRequirement>
    {
        private static readonly HashSet<string> PermittedRoles = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TENANT_OWNER", "TENANT_ADMIN", "COMPANY_ADMIN"
        };

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            WorkspaceUserManagementRequirement requirement)
        {
            var tenantId  = context.User.FindFirstValue("tenant_id");
            var companyId = context.User.FindFirstValue("company_id");

            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            {
                context.Fail();
                return Task.CompletedTask;
            }

            var workspaceRoles = context.User.FindAll("workspace_role").Select(c => c.Value);

            if (workspaceRoles.Any(r => PermittedRoles.Contains(r)))
                context.Succeed(requirement);
            else
                context.Fail();

            return Task.CompletedTask;
        }
    }
}
