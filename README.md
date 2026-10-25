<div align="center">

# Auto HDR Process Tool

**Apply Windows 11 Auto HDR compatibility settings and adjust per-process Auto HDR intensity without Xbox Game Bar.**

<a href="#requirements"><img src="https://img.shields.io/badge/Windows-11-0078D4" alt="Windows 11" /></a>
<a href="#build-from-source"><img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4" alt=".NET Framework 4.8" /></a>
<a href="#build-from-source"><img src="https://img.shields.io/badge/UI-WinForms-7A3E9D" alt="Windows Forms" /></a>
<br />
<a href="https://github.com/shizuku30/AutoHdrProcessTool/actions/workflows/ci.yml"><img src="https://github.com/shizuku30/AutoHdrProcessTool/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI" /></a>
<a href="#compatibility-and-limitations"><img src="https://img.shields.io/badge/architecture-x64-455A64" alt="x64" /></a>
<a href="LICENSE"><img src="https://img.shields.io/badge/license-0BSD-2E7D32" alt="0BSD License" /></a>

</div>

## Overview

Auto HDR Process Tool is a lightweight Windows 11 utility for games and applications that do not enter the Auto HDR pipeline automatically. It lists currently running processes, creates a path-specific Auto HDR compatibility profile, and exposes the same per-application intensity range used by Windows.

Built for .NET Framework 4.8, Auto HDR Process Tool uses Windows system components directly and requires no administrator privileges, third-party runtime libraries, or Xbox Game Bar installation.

## Features

- Select a target from the current Windows process list.
- Filter processes by name, PID, or executable path.
- Enable or remove an Auto HDR compatibility profile for an exact executable path.
- Optionally request a 10-bit buffer upgrade.
- Read and set live Auto HDR intensity from 0 to 100.
- Preserve unrelated Direct3D compatibility settings when updating an existing profile.
- Detect unsupported Windows helper versions and disable live intensity controls safely.
- Use an x64 WinForms interface with no NuGet package dependencies.

## Requirements

- Windows 11
- An HDR-capable display with HDR and Auto HDR enabled in Windows Settings
- .NET Framework 4.8
- An x64 Windows installation
- A compatible DXGI presentation path, typically used by DirectX 11 or DirectX 12 SDR content

## Usage

1. Enable HDR and Auto HDR in Windows Settings.
2. Start the target game or application.
3. Open Auto HDR Process Tool and select the target process.
4. Enable the Auto HDR profile for the selected executable. Fully exit and restart the target so Windows can load the new compatibility profile.
5. While the target is running, choose an intensity value and apply it to the current process.

The tool verifies that the selected PID still belongs to the same executable before applying a live intensity change. Refresh the process list if the target was restarted.

## How It Works

### Auto HDR compatibility profile

The tool stores a per-user Direct3D compatibility profile under:

```text
HKEY_CURRENT_USER\Software\Microsoft\Direct3D\<tool-owned-key>
```

The profile uses the target's full executable path and the following compatibility behavior:

```text
Name         = <full executable path>
D3DBehaviors = BufferUpgradeOverride=1[;BufferUpgradeEnable10Bit=1]
```

Using the complete path prevents the setting from unintentionally matching unrelated executables with the same file name.

### Live intensity

For a running target, the tool loads the Windows copy of `directxdatabasehelper.dll` exclusively from `System32`. It checks the required entry points before enabling live intensity controls and passes the executable path, process ID, and requested intensity through the Windows DirectX settings path.

## Security Model

The application is intentionally designed without game-memory access or code injection:

- Process paths are queried with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` and `QueryFullProcessImageName`.
- It does not use `Process.MainModule`, `ReadProcessMemory`, `WriteProcessMemory`, `VirtualAllocEx`, remote threads, DLL injection, or executable patching.
- It does not modify game files.
- Compatibility profiles are stored in the current user's Windows registry hive.
- The application manifest uses `asInvoker` and does not request elevation.
- System DLL loading is restricted to `System32`.

These constraints avoid the behaviors commonly associated with injectors and memory editors. Third-party anti-cheat products use their own detection policies, so no external utility can guarantee how every product will classify it.

## Compatibility and Limitations

- Auto HDR must be enabled globally in Windows.
- A compatibility profile does not bypass Windows graphics-pipeline requirements.
- Native HDR output, unsupported swap chains, protected presentation paths, Vulkan/OpenGL presentation, or indirect composition may prevent Auto HDR from activating.
- Enabling or removing the compatibility profile normally requires a full restart of the target process.
- The Direct3D compatibility fields and live intensity entry points are Windows implementation details rather than stable public APIs. A future Windows update may require compatibility changes.
- Packaged applications and protected system processes may not expose a usable executable path.

## Build From Source

Install Visual Studio with the .NET desktop development workload and the .NET Framework 4.8 targeting pack, then build the x64 configuration:

```powershell
msbuild .\AutoHdrProcessTool.sln `
  /t:Rebuild `
  /p:Configuration=Release `
  /p:Platform=x64
```

The project has no NuGet package dependencies. It must remain x64 with `Prefer32Bit` disabled because the Windows DirectX helper is invoked from a 64-bit process.

## Automated Tests

After building the Release x64 configuration, run the same checks used by CI:

```powershell
pwsh -NoProfile -File .\tests\Run-Tests.ps1 `
  -Configuration Release `
  -Platform x64
```

The test suite validates localization resources and language selection, Auto HDR behavior merging, binary architecture and version metadata, and the absence of process-memory or injection APIs.

## Contributing

Bug reports and focused pull requests are welcome. When reporting a Windows compatibility issue, include the Windows build number, `directxdatabasehelper.dll` file version, graphics API, and whether the application uses native HDR.

Do not submit changes that add process-memory access, code injection, or executable patching.

## Disclaimer

Auto HDR Process Tool is an independent project and is not affiliated with, endorsed by, sponsored by, or otherwise associated with Microsoft. Windows, DirectX, .NET, Xbox, and Xbox Game Bar are trademarks of the Microsoft group of companies.

## License

This project is released under the [BSD Zero Clause License](LICENSE), SPDX identifier [`0BSD`](https://spdx.org/licenses/0BSD.html).
