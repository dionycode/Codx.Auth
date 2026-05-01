using System.Collections.Generic;
using System.Linq;
using Codx.Auth.Configuration;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Codx.Auth.Infrastructure.Theming
{
    public class ThemeViewLocationExpander : IViewLocationExpander
    {
        private const string ThemeKey = "theme";

        public void PopulateValues(ViewLocationExpanderContext context)
        {
            var options = context.ActionContext.HttpContext.RequestServices.GetService<IOptions<ThemeOptions>>();
            var theme = options?.Value;

            if (theme is { IsActive: true })
            {
                context.Values[ThemeKey] = theme.Name;
            }
        }

        public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
        {
            if (context.Values.TryGetValue(ThemeKey, out var themeName) && !string.IsNullOrWhiteSpace(themeName))
            {
                var themedLocations = new[]
                {
                    $"/Themes/{themeName}/Views/{{1}}/{{0}}.cshtml",
                    $"/Themes/{themeName}/Views/Shared/{{0}}.cshtml",
                    $"/Themes/{themeName}/Views/Shared/Components/{{1}}/{{0}}.cshtml"
                };

                return themedLocations.Concat(viewLocations);
            }

            return viewLocations;
        }
    }
}
