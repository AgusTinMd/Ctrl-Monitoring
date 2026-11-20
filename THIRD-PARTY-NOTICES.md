# Third-party notices

Ctrl Monitor is distributed under the MIT License (see [LICENSE](LICENSE)). It includes or depends on the following third-party software and assets.

## MPL-2.0 components

These libraries are used without modification and remain under the Mozilla Public License 2.0 (see [LICENSES/MPL-2.0.txt](LICENSES/MPL-2.0.txt)). Their source code is available at the links below. The MIT License of Ctrl Monitor applies only to Ctrl Monitor's own code.

| Component | Version | Copyright | Source code |
|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | LibreHardwareMonitor contributors | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| DiskInfoToolkit | 1.1.2 | Florian K. | https://github.com/Blacktempel/DiskInfoToolkit |
| BlackSharp.Core | 1.0.7 | Florian K. | https://github.com/Blacktempel/BlackSharp |
| RAMSPDToolkit-NDD | 1.4.2 | Florian K. | https://github.com/Blacktempel/RAMSPDToolkit |

`LibreHardwareMonitorLib.dll` reads the hardware sensors. DiskInfoToolkit (the CrystalDiskInfo engine used for S.M.A.R.T.), BlackSharp.Core and RAMSPDToolkit-NDD are dependencies of LibreHardwareMonitorLib.

## Apache-2.0 components

| Component | Version | Copyright | Project |
|---|---|---|---|
| HidSharp | 2.6.4 | Copyright 2010-2025 James F. Bellinger | http://software.seekye.com/hidsharp |

HidSharp is a dependency of LibreHardwareMonitorLib and is used without modification. It is licensed under the Apache License, Version 2.0 — see [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt).

## MIT-licensed components

| Component | Version | Copyright |
|---|---|---|
| .NET runtime and WPF | 10.0 | Copyright (c) .NET Foundation and Contributors |
| CommunityToolkit.Mvvm | 8.4.2 | Copyright (c) .NET Foundation and Contributors |
| H.NotifyIcon.Wpf | 2.4.1 | Copyright (c) 2020 havendv |
| H.NotifyIcon | 2.4.1 | Copyright (c) 2020 havendv |
| H.GeneratedIcons.System.Drawing | 2.4.1 | Copyright (c) 2020 havendv |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.0 | Copyright (c) Microsoft Corporation |
| Microsoft.Diagnostics.NETCore.Client | 0.2.661903 | Copyright (c) Microsoft Corporation |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | Copyright (c) Microsoft Corporation |
| Microsoft.Extensions.Logging.Abstractions | 8.0.3 | Copyright (c) Microsoft Corporation |
| System.Diagnostics.PerformanceCounter | 10.0.2 | Copyright (c) Microsoft Corporation |
| System.Management | 10.0.2 | Copyright (c) Microsoft Corporation |
| System.IO.Ports | 10.0.3 | Copyright (c) Microsoft Corporation |
| System.IO.FileSystem.AccessControl | 5.0.0 | Copyright (c) Microsoft Corporation |
| System.Reflection.TypeExtensions | 4.7.0 | Copyright (c) Microsoft Corporation |

The MIT License text that applies to these components is reproduced below.

```text
MIT License

Copyright (c) .NET Foundation and Contributors
Copyright (c) 2020 havendv
Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Other components

| Component | Version | Copyright | License |
|---|---|---|---|
| Mono.Posix.NETStandard | 1.0.0 | Copyright (c) Microsoft Corporation | As declared by the package: https://go.microsoft.com/fwlink/?linkid=869050 |

Mono.Posix.NETStandard is a dependency of LibreHardwareMonitorLib.

## .NET runtime

The self-contained build also includes the .NET runtime. Its third-party notices are available at https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT.

## Brand logos

The brand logos in `src/CtrlMonitor.App/Assets` are trademarks or registered trademarks of their respective owners. They are used only to identify the detected hardware and are **not** covered by the MIT License. See the "Brand logos" section of the [README](README.md) for the list.

## PawnIO

Ctrl Monitor can download and install PawnIO 2.2.0 from its official release. PawnIO is licensed under GPL-2.0 and is not bundled or redistributed with Ctrl Monitor: the installer downloads it from https://github.com/namazso/PawnIO.Setup and verifies its SHA-256 before running it.
