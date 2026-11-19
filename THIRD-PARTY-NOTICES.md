# Third-party notices

Ctrl Monitor is distributed under the MIT License (see [LICENSE](LICENSE)). It includes or depends on the following third-party software and assets.

## LibreHardwareMonitorLib 0.9.6 — MPL-2.0

- Project and source code: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License: Mozilla Public License 2.0 — see [LICENSES/MPL-2.0.txt](LICENSES/MPL-2.0.txt)

`LibreHardwareMonitorLib.dll` is used to read hardware sensors. It remains under MPL-2.0; the MIT License of Ctrl Monitor applies only to Ctrl Monitor's own code.

## MIT-licensed components

| Component | Version | Copyright |
|---|---|---|
| .NET runtime and WPF | 10.0 | Copyright (c) .NET Foundation and Contributors |
| CommunityToolkit.Mvvm | 8.4.2 | Copyright (c) .NET Foundation and Contributors |
| H.NotifyIcon.Wpf | 2.4.1 | Copyright (c) 2020 havendv |
| Microsoft.Diagnostics.Tracing.TraceEvent | 4.0.0 | Copyright (c) Microsoft Corporation |
| System.Diagnostics.PerformanceCounter | 10.0.2 | Copyright (c) Microsoft Corporation |
| System.Management | 10.0.2 | Copyright (c) Microsoft Corporation |

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

The self-contained build also includes the .NET runtime. Its third-party notices are available at https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT.

## Brand logos

The brand logos in `src/CtrlMonitor.App/Assets` are trademarks or registered trademarks of their respective owners. They are used only to identify the detected hardware and are **not** covered by the MIT License. See the "Brand logos" section of the [README](README.md) for the list.

## PawnIO

Ctrl Monitor can download and install PawnIO 2.2.0 from its official release. PawnIO is licensed under GPL-2.0 and is not bundled or redistributed with Ctrl Monitor: the installer downloads it from https://github.com/namazso/PawnIO.Setup and verifies its SHA-256 before running it.
