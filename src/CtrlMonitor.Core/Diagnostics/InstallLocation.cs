using System.Security.AccessControl;
using System.Security.Principal;

namespace CtrlMonitor.Core.Diagnostics;

/// <summary>
/// Indica si el ejecutable está en una ubicación que solo los administradores
/// pueden modificar. La tarea de inicio ejecuta la app como administrador: si
/// apuntara a una carpeta escribible por cualquier usuario (un build del repo,
/// un zip descomprimido en Descargas), otro programa sin privilegios podría
/// reemplazar el exe y ganar permisos de administrador al iniciar sesión.
/// </summary>
public static class InstallLocation
{
    // Cuentas que pueden tener escritura sin que eso sea un riesgo.
    private static readonly HashSet<string> TrustedSids = new(StringComparer.OrdinalIgnoreCase)
    {
        "S-1-5-32-544", // Administradores
        "S-1-5-18",     // SYSTEM
        "S-1-3-0",      // CREATOR OWNER (solo se hereda; se descarta abajo si es InheritOnly)
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" // TrustedInstaller
    };

    private const FileSystemRights WriteRights =
        FileSystemRights.WriteData
        | FileSystemRights.AppendData
        | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.WriteAttributes
        | FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership;

    // Derechos genéricos que aparecen como enteros crudos en algunas ACE.
    private const int GenericAll = 0x10000000;
    private const int GenericWrite = 0x40000000;

    /// <summary>
    /// <c>true</c> si ni la carpeta ni el archivo dan permisos de escritura a
    /// cuentas que no sean administradores, SYSTEM o TrustedInstaller.
    /// Ante cualquier error de lectura devuelve <c>false</c> (lo seguro).
    /// </summary>
    public static bool IsProtectedFromNonAdmins(string executablePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(executablePath);
            string? directory = Path.GetDirectoryName(fullPath);

            if (string.IsNullOrEmpty(directory) || !File.Exists(fullPath))
                return false;

            AuthorizationRuleCollection directoryRules = new DirectoryInfo(directory)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

            AuthorizationRuleCollection fileRules = new FileInfo(fullPath)
                .GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

            return !GrantsUntrustedWrite(directoryRules) && !GrantsUntrustedWrite(fileRules);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool GrantsUntrustedWrite(AuthorizationRuleCollection rules)
    {
        foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;

            // Una ACE solo heredable no aplica al objeto en sí.
            if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
                continue;

            if (TrustedSids.Contains(rule.IdentityReference.Value))
                continue;

            int rights = (int)rule.FileSystemRights;

            if ((rule.FileSystemRights & WriteRights) != 0
                || (rights & GenericAll) != 0
                || (rights & GenericWrite) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
