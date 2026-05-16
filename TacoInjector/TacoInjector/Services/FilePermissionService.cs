using System.Security.AccessControl;
using System.Security.Principal;

namespace TacoInjector.Services;

public sealed class FilePermissionService
{
    private static readonly SecurityIdentifier AllApplicationPackagesSid = new("S-1-15-2-1");

    public void AllowAllApplicationPackagesReadExecute(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var security = fileInfo.GetAccessControl();

        var rule = new FileSystemAccessRule(
            AllApplicationPackagesSid,
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow);

        security.AddAccessRule(rule);
        fileInfo.SetAccessControl(security);
    }
}