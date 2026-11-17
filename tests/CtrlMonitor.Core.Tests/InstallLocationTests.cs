using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.Core.Tests;

public class InstallLocationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "CtrlMonitorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SystemExecutable_IsProtected()
    {
        // System32 solo lo pueden modificar TrustedInstaller y los administradores.
        string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");

        Assert.True(InstallLocation.IsProtectedFromNonAdmins(notepad));
    }

    [Fact]
    public void UserTempFolder_IsNotProtected()
    {
        // Cualquier carpeta del perfil del usuario es escribible sin elevar,
        // como un build del repo o un zip descomprimido en Descargas.
        Directory.CreateDirectory(_directory);
        string exe = Path.Combine(_directory, "CtrlMonitor.exe");
        File.WriteAllText(exe, "x");

        Assert.False(InstallLocation.IsProtectedFromNonAdmins(exe));
    }

    [Fact]
    public void MissingFile_IsNotProtected()
    {
        Assert.False(InstallLocation.IsProtectedFromNonAdmins(Path.Combine(_directory, "nope.exe")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
