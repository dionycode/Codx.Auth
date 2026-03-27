using System;
using System.Collections.Generic;

namespace Codx.Auth.Services
{
    public class WorkspaceContextAccessor : IWorkspaceContextAccessor
    {
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CompanyId { get; set; }
        public Guid MembershipId { get; set; }
        public string WorkspaceContextType { get; set; }
        public Guid WorkspaceSessionId { get; set; }
        public IReadOnlyList<string> WorkspaceRoleCodes { get; set; } = Array.Empty<string>();
    }
}
