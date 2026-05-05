using System.Collections.Generic;

namespace Codx.Auth.Models
{
    public record PreviewResult(string Rendered, IReadOnlyList<string> UnrecognizedPlaceholders);
}
