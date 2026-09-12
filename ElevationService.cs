using System.Security.Principal;

namespace PrintFreeTool;

internal static class ElevationService
{
    public static bool IsAdministrator
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
