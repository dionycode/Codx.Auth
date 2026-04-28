using System;
using System.Collections.Generic;

namespace Codx.Auth.Services
{
    public interface IWorkspaceContextAccessor
    {
        Guid UserId { get; set; }
        Guid TenantId { get; set; }
        Guid? CompanyId { get; set; }
        Guid MembershipId { get; set; }
        string WorkspaceContextType { get; set; }
        Guid WorkspaceSessionId { get; set; }
        IReadOnlyList<string> WorkspaceRoleCodes { get; set; }
    }
}
