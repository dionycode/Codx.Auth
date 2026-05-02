using Codx.Auth.Data.Entities.AspNet;
using System.Threading;
using System.Threading.Tasks;

namespace Codx.Auth.Services.Interfaces
{
    public interface IWorkspaceInitializationService
    {
        /// <summary>
        /// Assigns the default application role to a newly registered user,
        /// scoped to their personal workspace (tenant + company).
        /// No-ops gracefully if no application_id or default role is configured.
        /// </summary>
        Task InitializeUserForApplicationAsync(
            ApplicationUser user,
            string clientId,
            CancellationToken cancellationToken = default);
    }
}
