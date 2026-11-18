namespace CtrlMonitor.Core.Inventory;

/// <summary>Espacio de una unidad con letra (C:, D:…).</summary>
/// <param name="Letter">Letra con dos puntos, por ejemplo «C:».</param>
/// <param name="Label">Etiqueta del volumen («Windows», «Datos»); puede venir vacía.</param>
/// <param name="TotalBytes">Tamaño total.</param>
/// <param name="FreeBytes">Espacio libre para el usuario actual.</param>
public sealed record VolumeUsage(string Letter, string Label, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedPercent => TotalBytes <= 0 ? 0 : Math.Clamp(UsedBytes * 100d / TotalBytes, 0, 100);
}

/// <summary>
/// Lee el espacio de las unidades fijas y extraíbles que están listas. Las
/// unidades de red y de CD se omiten: pueden tardar o no tener disco.
/// </summary>
public static class VolumeUsageReader
{
    public static IReadOnlyList<VolumeUsage> Read()
    {
        var result = new List<VolumeUsage>();

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
                    continue;

                result.Add(new VolumeUsage(
                    drive.Name.TrimEnd('\\'),
                    drive.VolumeLabel,
                    drive.TotalSize,
                    drive.AvailableFreeSpace));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Una unidad que se desconecta durante la lectura no corta la lista.
            }
        }

        return result.OrderBy(volume => volume.Letter, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
