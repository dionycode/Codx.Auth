using Codx.Auth.Models.WorkspaceUsers;
using Codx.Auth.Services;
using Duende.IdentityServer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Controllers.API
{
    [ApiController]
    [Route("api/v1/workspaces")]
    [Authorize(IdentityServerConstants.LocalApi.PolicyName)]
    public class WorkspacesController : ControllerBase
    {
        private readonly IWorkspaceUserService _service;

        public WorkspacesController(IWorkspaceUserService service)
        {
            _service = service;
        }

        /// <summary>
        /// Returns a paginated list of users with an active application role in
        /// the caller's current workspace (tenant + company). Tenant, company,
        /// and application are resolved exclusively from the bearer token claims.
        /// </summary>
        [HttpGet("users")]
        [Authorize(Policy = "WorkspaceAdministratorPolicy")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetWorkspaceUsers(
            [FromQuery] int     page     = 1,
            [FromQuery] int     pageSize = 20,
            [FromQuery] string? email    = null,
            CancellationToken   cancellationToken = default)
        {
            if (page < 1)
                return BadRequest("page must be >= 1");

            pageSize = Math.Clamp(pageSize, 1, 100);

            var tenantId  = Guid.Parse(User.FindFirstValue("tenant_id")!);
            var companyId = Guid.Parse(User.FindFirstValue("company_id")!);
            var clientId  = User.FindFirstValue("client_id")!;

            var result = await _service.GetWorkspaceUsersAsync(
                tenantId, companyId, clientId, page, pageSize, email, cancellationToken);

            if (result is null)
                return Forbid();

            return Ok(result);
        }

        /// <summary>
        /// Adds an existing user to the workspace directly, or sends an invitation
        /// when no Codx.Auth account exists for the supplied email address.
        /// Responds 201 (user added) or 202 (invitation sent).
        /// </summary>
        [HttpPost("users")]
        [Authorize(Policy = "WorkspaceUserManagementPolicy")]
        [ProducesResponseType(typeof(AddWorkspaceUserResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(AddWorkspaceUserResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> AddOrInviteUser(
            [FromBody] AddWorkspaceUserRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request?.Email))
                return BadRequest(new { error = "email is required." });

            var tenantId  = Guid.Parse(User.FindFirstValue("tenant_id")!);
            var companyId = Guid.Parse(User.FindFirstValue("company_id")!);
            var callerId  = Guid.Parse(User.FindFirstValue("sub")!);
            var clientId  = User.FindFirstValue("client_id")!;
            var roles     = User.FindAll("workspace_role").Select(c => c.Value).ToList();

            var ctx = new WorkspaceAddCallerContext
            {
                TenantId             = tenantId,
                CompanyId            = companyId,
                ClientId             = clientId,
                CallerWorkspaceRoles = roles,
                CallerId             = callerId,
            };

            var result = await _service.AddOrInviteUserAsync(request, ctx, cancellationToken);

            return result.Outcome switch
            {
                AddWorkspaceUserResult.ResultOutcome.Added    => StatusCode(StatusCodes.Status201Created, result.Response),
                AddWorkspaceUserResult.ResultOutcome.Invited  => StatusCode(StatusCodes.Status202Accepted, result.Response),
                AddWorkspaceUserResult.ResultOutcome.Conflict => Conflict(new { error = result.ErrorCode }),
                AddWorkspaceUserResult.ResultOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.Detail }),
                AddWorkspaceUserResult.ResultOutcome.BadRequest => BadRequest(new { error = result.Detail }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        /// <summary>
        /// Replaces a workspace member's application role. The old UserApplicationRole is set to
        /// Revoked and a new one is created (Spec 004 lifecycle). Caller's workspace_role must
        /// permit assigning the requested role (role-hierarchy constraint, plan.md AD-6).
        /// </summary>
        [HttpPut("members/{userId:guid}/role")]
        [Authorize(Policy = "WorkspaceUserManagementPolicy")]
        [ProducesResponseType(typeof(UpdateMemberRoleResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> UpdateMemberRole(
            Guid userId,
            [FromBody] UpdateMemberRoleRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request?.ApplicationRole))
                return BadRequest(new { error = "applicationRole is required." });

            var callerContext = BuildCallerContext();
            var result = await _service.UpdateMemberRoleAsync(userId, request, callerContext, cancellationToken);

            return result.Status switch
            {
                ServiceResult<UpdateMemberRoleResponse>.ResultStatus.Success   => Ok(result.Data),
                ServiceResult<UpdateMemberRoleResponse>.ResultStatus.NotFound  => NotFound(),
                ServiceResult<UpdateMemberRoleResponse>.ResultStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorCode }),
                ServiceResult<UpdateMemberRoleResponse>.ResultStatus.Conflict  => Conflict(new { error = result.ErrorCode }),
                ServiceResult<UpdateMemberRoleResponse>.ResultStatus.BadRequest => BadRequest(new { error = result.ErrorCode }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        /// <summary>
        /// Removes a workspace member from the caller's company. Sets UserMembership → Removed,
        /// all active UserApplicationRoles → Revoked, and revokes persisted grants (refresh tokens).
        /// Returns 200 OK with body — intentional deviation from 204 per plan.md AD-15.
        /// </summary>
        [HttpDelete("members/{userId:guid}")]
        [Authorize(Policy = "WorkspaceUserManagementPolicy")]
        [ProducesResponseType(typeof(RemoveMemberResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RemoveMember(
            Guid userId,
            CancellationToken cancellationToken)
        {
            var callerContext = BuildCallerContext();
            var result = await _service.RemoveMemberAsync(userId, callerContext, cancellationToken);

            return result.Status switch
            {
                ServiceResult<RemoveMemberResponse>.ResultStatus.Success   => Ok(result.Data),
                ServiceResult<RemoveMemberResponse>.ResultStatus.NotFound  => NotFound(),
                ServiceResult<RemoveMemberResponse>.ResultStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new { error = result.ErrorCode }),
                ServiceResult<RemoveMemberResponse>.ResultStatus.Conflict  => Conflict(new { error = result.ErrorCode }),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        // ────────────────────────────────────────────────────────────────
        // Private helpers
        // ────────────────────────────────────────────────────────────────

        private WorkspaceAddCallerContext BuildCallerContext()
        {
            return new WorkspaceAddCallerContext
            {
                TenantId             = Guid.Parse(User.FindFirstValue("tenant_id")!),
                CompanyId            = Guid.Parse(User.FindFirstValue("company_id")!),
                ClientId             = User.FindFirstValue("client_id")!,
                CallerWorkspaceRoles = User.FindAll("workspace_role").Select(c => c.Value).ToList(),
                CallerId             = Guid.Parse(User.FindFirstValue("sub")!),
            };
        }
    }
}
