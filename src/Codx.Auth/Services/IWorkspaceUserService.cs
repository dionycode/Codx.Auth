using Codx.Auth.Models.WorkspaceUsers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services
{
    public interface IWorkspaceUserService
    {
        /// <summary>
        /// Updates the application role of an active workspace member.
        /// Enforces the self-modification guard, role-value validation (ApplicationId-scoped),
        /// and the role-hierarchy constraint before applying the Spec 004 lifecycle transition.
        /// </summary>
        Task<ServiceResult<UpdateMemberRoleResponse>> UpdateMemberRoleAsync(
            Guid userId,
            UpdateMemberRoleRequest request,
            WorkspaceAddCallerContext callerContext,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes an active workspace member. Transitions UserMembership → Removed,
        /// all active UserApplicationRoles → Revoked, and revokes persisted grants
        /// (refresh tokens) for the removed user scoped to the calling client.
        /// </summary>
        Task<ServiceResult<RemoveMemberResponse>> RemoveMemberAsync(
            Guid userId,
            WorkspaceAddCallerContext callerContext,
            CancellationToken cancellationToken = default);


        /// <summary>
        /// Returns a paginated list of workspace users for the given tenant, company, and
        /// client. The application_id is resolved internally from <c>ClientProperties</c>
        /// using the caller's <paramref name="clientId"/> claim.
        /// Returns <c>null</c> when no application_id is mapped to <paramref name="clientId"/>
        /// in ClientProperties — the controller should treat this as 403 Forbidden.
        /// </summary>
        Task<WorkspaceUsersResponse?> GetWorkspaceUsersAsync(
            Guid   tenantId,
            Guid   companyId,
            string clientId,
            int    page,
            int    pageSize,
            string? email,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds an existing Codx.Auth user to the workspace, or dispatches an invitation
        /// if no account exists for the supplied email. The application_id is resolved
        /// internally from ClientProperties using <see cref="WorkspaceAddCallerContext.ClientId"/>.
        /// </summary>
        Task<AddWorkspaceUserResult> AddOrInviteUserAsync(
            AddWorkspaceUserRequest request,
            WorkspaceAddCallerContext callerContext,
            CancellationToken cancellationToken = default);
    }
}
