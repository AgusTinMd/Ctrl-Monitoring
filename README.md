# Ctrl Monitor

Aplicación de escritorio para Windows que **muestra en tiempo real** las temperaturas, la velocidad de los ventiladores, la potencia, los voltajes, la carga, el PWM, el uso de RAM y VRAM y el tráfico de red. Es **estrictamente de solo lectura**: no escribe PWM, no aplica curvas y no modifica el BIOS.

La interfaz está en español, vive en la bandeja del sistema y puede arrancar con Windows. Incluye **paneles de gráfico configurables**, una **vista de ventiladores con RPM y PWM**, iconos de CPU/GPU/placa y el logo de ASUS. El **control de los ventiladores queda a cargo del BIOS**.

> **Hardware de referencia:** ASUS PRIME B560M-A · Intel Core i5-10400F · AMD Radeon RX 6700 XT.

---

## Qué hace

- **Resumen** con los valores destacados de CPU, GPU y placa: temperatura, máxima de núcleos, hotspot, potencia, carga, RPM y VRAM de la GPU.
- **Gráfico en vivo** con paneles independientes y un selector de métrica en cada uno: CPU, GPU, placa, hotspot, RAM, VRAM y red. Por defecto se muestran CPU, GPU y RAM; el resto se agrega desde la barra superior, cualquier tarjeta se puede quitar con la **✕** y se reordenan **arrastrándolas** (el orden se guarda). Cada panel ajusta su escala vertical y comparte las ventanas de 1, 5 o 10 minutos. Las RPM del ventilador y el consumo actual en vatios aparecen junto a la temperatura; RAM y VRAM muestran GB en uso sobre el total y porcentaje; la red muestra bajada y subida en MB/s.
- **Ventiladores** con cada canal detectado, sus RPM y el PWM que está aplicando el BIOS. La casilla **Solo activos** oculta los canales detenidos.
- **Sensores** con todas las lecturas detectadas, agrupadas por componente: temperaturas, ventiladores, potencia, voltajes, carga, PWM y memoria de la GPU. Cada grupo se puede plegar o desplegar.
- **Hardware detectado** con CPU, GPU, placa, Super I/O, cantidad de RAM (módulos y velocidad) y discos físicos.
- **Actualización en vivo** cada 0,5–10 segundos.
- **Ventana redimensionable y adaptable**: las tarjetas y los gráficos pasan a una o dos columnas según el ancho, pensado para monitores verticales de 1080p.
- **Barra lateral colapsable** con el botón integrado en su cabecera o `Ctrl+B`; al ocultarla queda un botón para reabrirla.
- **Iconos vectoriales** de CPU, GPU y placa madre, más el logo de ASUS en las tarjetas de hardware.
- **Bandeja del sistema** con tooltip de CPU/GPU y menú para abrir o salir.
- **Inicio con Windows** mediante tarea programada con permisos altos.
- **Persistencia** de las preferencias de refresco, arranque, bandeja, barra lateral y el layout de los gráficos.

## Qué no hace

- No controla ventiladores ni escribe registros PWM del Super I/O.
- No incluye curvas, perfiles, modos manuales ni emergencia térmica.
- No restaura ni modifica la configuración del BIOS: nunca la cambia.
- No guarda historial en disco: el gráfico solo retiene los últimos 10 minutos en memoria y se reinicia al abrir la app.

El PWM que aparece en la pestaña **Sensores** es una lectura del valor que está aplicando el BIOS en ese momento.

## Requisitos

- Windows 10/11 de 64 bits.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (para ejecutar) o .NET 10 SDK (para compilar).
- **PawnIO 2.2** instalado. Es el driver firmado que usa LibreHardwareMonitor para acceder al Super I/O. Sin él, la app no detecta el Nuvoton y muestra menos sensores.
- Permisos de administrador. El manifiesto de la app exige elevación (`requireAdministrator`); si se inicia sin privilegios, se relanza sola y pide UAC.

## Compilar y ejecutar

```powershell
# Compilar toda la solución
dotnet build CtrlMonitor.slnx -c Release

# Ejecutar (pide UAC)
Start-Process "src\CtrlMonitor.App\bin\Release\net10.0-windows\CtrlMonitor.exe" -Verb RunAs
```

La app **siempre** se ejecuta como administrador. `CtrlMonitor.exe` lo exige desde el manifiesto y, si se inicia con `dotnet CtrlMonitor.dll`, detecta que no está elevada y se relanza sola con UAC a través de `CtrlMonitor.exe`. Si se cancela el cartel, la app se cierra: no queda un modo sin privilegios.

### Tests

```powershell
dotnet test tests\CtrlMonitor.Core.Tests\CtrlMonitor.Core.Tests.csproj -c Release
```

Los tests cubren la persistencia y normalización de la configuración, incluida la lectura de un archivo legacy con campos de control que ya no se usan, el layout de gráficos, la migración desde FanCtrl, la escala de los ejes y la conversión de velocidades de red.

## Uso

1. Abrí Ctrl Monitor y aceptá el cartel de UAC.
2. En **Resumen** revisá las tarjetas de CPU, GPU, potencia y el estado general.
3. En **Gráfico** seguí la evolución de las temperaturas, la memoria y la red. Cada tarjeta tiene su propio selector de métrica y escala automática; elegí 1, 5 o 10 minutos y usá **Limpiar** para reiniciar el trazado. Las RPM del ventilador y el consumo en vatios se muestran junto a la temperatura en CPU, GPU y hotspot; las tarjetas de RAM y VRAM muestran los GB en uso sobre el total y el porcentaje; la de red muestra bajada y subida. Usá **Agregar** para sumar otra métrica y la **✕** de una tarjeta para quitarla.
4. En **Ventiladores** está cada canal detectado con sus RPM y el PWM aplicado por el BIOS. Los canales en cero se muestran atenuados; activá **Solo activos** para ocultarlos.
5. En **Sensores** está el volcado completo agrupado por componente. Las filas con barra son porcentajes (carga y PWM). Tocá la cabecera de un grupo para plegarlo o desplegarlo.
6. En **Ajustes** cambiá el intervalo de refresco, el inicio con Windows y el comportamiento de la bandeja. La tarjeta **Hardware detectado** resume también la RAM instalada y los discos físicos.

El botón ☰ de la cabecera de la barra lateral (o `Ctrl+B`) la oculta. Cuando está oculta, el botón ☰ de la barra superior la vuelve a mostrar. El icono de la bandeja abre la ventana con un clic. Para cerrar del todo usá **Salir** en su menú contextual.

## Configuración y logs

| Dato | Ruta |
|---|---|
| Configuración | `%APPDATA%\CtrlMonitor\config.json` |
| Logs | `%APPDATA%\CtrlMonitor\logs\ctrlmonitor-AAAA-MM-DD.log` |

Claves de configuración:

| Clave | Valor por defecto | Descripción |
|---|---|---|
| `SchemaVersion` | `2` | Versión del formato. Los archivos v1 se migran descartando los campos de control. |
| `RefreshIntervalMs` | `1000` | Intervalo de lectura. Se limita a 500–10000 ms. |
| `StartMinimized` | `false` | Arranca oculto en la bandeja. |
| `MinimizeToTray` | `true` | Cerrar o minimizar la ventana la oculta en la bandeja. |
| `RunAtStartup` | `false` | Crea la tarea programada de inicio. |
| `ShowNavigation` | `true` | Muestra la barra lateral de navegación. |
| `ChartMetrics` | `["Cpu","Gpu","Ram"]` | Métricas visibles del modo gráfico, en orden. Se agregan y quitan desde la interfaz. |

Si el archivo se corrompe, la app lo respalda como `config.json.bak-*` y vuelve a los valores por defecto.

## Inicio con Windows

La opción **Iniciar con Windows** crea una tarea programada llamada `CtrlMonitor` con `/RL HIGHEST` y 20 segundos de retraso. Así se evita el cartel de UAC en cada inicio. Se elimina desde la misma opción o con:

```powershell
schtasks /Delete /TN CtrlMonitor /F
```

## Actualizar desde FanCtrl

Hasta la versión 2.5.0 la app se llamaba **FanCtrl Monitor** (`FanCtrl.exe`). El nombre cambió porque dejó de ser una herramienta de ventiladores y pasó a ser un monitor general. Al abrir Ctrl Monitor por primera vez:

- Si no existe `%APPDATA%\CtrlMonitor\config.json`, se copia `%APPDATA%\FanCtrl\config.json`. La carpeta vieja no se borra.
- Si existe la tarea programada `FanCtrl`, se elimina y, si el inicio con Windows estaba activo, se crea `CtrlMonitor` apuntando al ejecutable nuevo.

Los logs viejos quedan en `%APPDATA%\FanCtrl\logs`. Cuando ya no los necesites, podés borrar esa carpeta a mano.

## Estructura del proyecto

```
src/CtrlMonitor.Core/      Lógica sin UI
  Hardware/                LibreHardwareMonitor, lecturas y snapshots
  Inventory/               Lectura WMI de RAM y discos, uso de memoria y tráfico de red
  Configuration/           Modelo y persistencia
  Diagnostics/             Logs
src/CtrlMonitor.App/       WPF
  Themes/Theme.xaml        Paleta, estilos, iconos vectoriales y navegación
  Controls/LiveChart.cs    Gráfico en vivo por serie o combinado
  Controls/ResponsiveGrid.cs  Panel adaptable de tarjetas
  Models/ChartSample.cs    Muestra de temperaturas, memoria y red del gráfico
  Converters/              Ventana temporal, columnas y conversores de UI
  ViewModels/              MainViewModel, paneles de gráfico, grupos y ventiladores
  Services/                Bandeja e inicio con Windows
  Assets/asus.png          Logo de ASUS para identificar la placa
tests/CtrlMonitor.Core.Tests/  Tests xUnit
tools/CtrlMonitor.Probe/   Sonda de solo lectura para diagnóstico elevado
```

## Sonda de diagnóstico

`tools/CtrlMonitor.Probe` vuelca todos los sensores que ve LibreHardwareMonitor, con la misma configuración de hardware que la app. Sirve para comparar lecturas cuando falta algún dato:

```powershell
# Desde una terminal elevada (la sonda pide administrador)
dotnet run --project tools\CtrlMonitor.Probe -c Release
```

Escribe `probe-output.txt` junto al ejecutable. No escribe PWM ni toca el BIOS. Agregá `--pause` para que la consola espere un ENTER antes de cerrarse.

## Limitaciones conocidas

- **Gráfico**: los paneles comparten la misma ventana temporal y el historial vive solo en memoria; al cerrar la app se pierde. Con intervalos de refresco largos hay menos puntos por minuto.
- **Ventiladores**: los canales sin sensor propio muestran `0 RPM` o `—` en PWM. El mapeo de cada canal (CPU_FAN, SYS_FAN, etc.) depende de la placa y no se inventa.
- **Otra placa/Super I/O**: los nombres y la cantidad de sensores dependen del chip. La app muestra lo que LibreHardwareMonitor detecte; no hay mapeo específico por modelo.
- **Sensores ausentes**: sin PawnIO o sin administrador, el Super I/O no responde y faltan RPM, voltajes y PWM de la placa.
- **RAM y discos**: se leen una sola vez con WMI. Si WMI está deshabilitado o devuelve un error, la tarjeta muestra `No disponible` y el resto de la app sigue funcionando.
- **GPU**: la potencia, el hotspot y la VRAM dependen del soporte del driver. Si el fabricante no los expone, aparecen como `—`.
- **Red**: la velocidad suma las interfaces físicas Ethernet y Wi-Fi activas; las VPN y adaptadores virtuales se excluyen para no mezclar tráfico. El valor es la tasa instantánea entre refrescos.
- **UAC**: la app necesita administrador; el inicio automático usa una tarea programada para no pedirlo cada vez.
- **Antivirus/anti-cheat**: algunos sistemas bloquean el acceso directo al Super I/O. Si faltan sensores, revisá los logs y PawnIO.

## Marcas

ASUS es una marca registrada de ASUSTeK Computer Inc. El logo incluido en la app se usa únicamente para identificar el hardware detectado.
