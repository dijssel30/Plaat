using System.Globalization;
using System.Security.Claims;

namespace Plaat.Api;

public static class ClaimsPrincipalExtensions
{
    extension(ClaimsPrincipal user)
    {
        public int GetUserId()
        {
            var sub = user.FindFirst("sub")?.Value
                      ?? throw new InvalidOperationException("Token is missing the 'sub' claim");

            return int.Parse(sub, new CultureInfo("nl-NL"));
        }
    }
}
