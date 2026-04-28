using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Codx.Auth.ViewComponents
{
    public class AdminNavigationViewComponent : ViewComponent
    {
        private readonly IAuthorizationService _authorizationService;

        public AdminNavigationViewComponent(IAuthorizationService authorizationService)
        {
            _authorizationService = authorizationService;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var isAuthorized = await _authorizationService.AuthorizeAsync(UserClaimsPrincipal, "PlatformAdmin");
            return View(isAuthorized.Succeeded);
        }
    }
}
