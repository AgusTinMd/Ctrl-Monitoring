using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using CtrlMonitor.Core.Diagnostics;

namespace CtrlMonitor.App.Services;

/// <summary>
/// Devuelve a Windows la memoria que la app no está usando. Se llama al entrar
/// en reposo: lo que haga falta después se vuelve a cargar solo.
/// </summary>
internal static class MemoryTrimmer
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimum, nint maximum);

    public static void Trim()
    {
        try
        {
            using Process self = Process.GetCurrentProcess();
            long before = self.WorkingSet64;

            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            // -1/-1 le pide a Windows que saque de la RAM las páginas que no se usan.
            SetProcessWorkingSetSize(self.Handle, -1, -1);

            self.Refresh();
            AppLog.Info($"Reposo: memoria de {before / 1048576} MB a {self.WorkingSet64 / 1048576} MB.");
        }
        catch (Exception ex)
        {
            AppLog.Warn("No se pudo liberar memoria: " + ex.Message);
        }
    }
}
