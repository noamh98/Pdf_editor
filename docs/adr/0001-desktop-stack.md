# ADR-0001: Desktop stack — .NET 8 with Avalonia

Status: accepted (milestone 2)

## Context

The product must be a portable Windows 10/11 x64 desktop application: self-contained, no installer,
no administrator rights, no runtime download on first start, full right-to-left Hebrew support, and
capable of PDF rendering, local OCR and Windows printing. It must also be automatically testable.

The development environment available for this project is Linux. That is not a cosmetic detail: a
stack that cannot be built or tested here cannot be verified at all before it reaches a user.

## Options

1. **WPF (.NET 8, `net8.0-windows`).** The most mature Windows UI stack, best screen-reader support,
   native `System.Windows.Xps` printing. Cannot be built on Linux — the Windows Desktop SDK is
   Windows-only — so nothing could be compiled or tested in this environment.
2. **WinUI 3 / Windows App SDK.** Modern Fluent controls, but a heavier deployment story, historically
   awkward unpackaged/self-contained distribution, and likewise Windows-only to build.
3. **Avalonia 11 (`net8.0`).** Cross-platform XAML UI, Skia renderer, `FlowDirection` RTL support,
   MIT licence. Builds and runs headless UI tests on Linux; cross-publishes a self-contained
   `win-x64` binary from Linux. Not native Win32 controls, so accessibility is good but not equal to
   WPF.
4. **Tauri (Rust + web frontend).** Small binary, but depends on the WebView2 runtime. The evergreen
   bootstrapper downloads on first run, which the requirements forbid; the fixed-version runtime can
   be bundled but adds roughly 180 MB and a second update surface. Cross-compiling to Windows from
   Linux needs an MSVC or mingw toolchain that is not available here.
5. **Electron.** Fully self-contained and buildable from Linux, but the largest artifact, a much
   wider attack surface for an application whose main promise is privacy, and a Chromium security
   update treadmill for an app that is meant to never phone home.

## Decision

**Avalonia 11.3.7 on .NET 8, all projects targeting `net8.0`.**

Windows-only functionality (printing via `System.Drawing.Printing`, DPAPI signature protection) is
reached through interfaces in `PdfEditor.Core` and implemented in `PdfEditor.Platform` behind
`OperatingSystem.IsWindows()` guards, so the whole solution still compiles and unit-tests on Linux.

## Consequences

Positive:

- Verified in this environment: `dotnet publish -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true` produced a 44 MB single executable from Linux.
- Avalonia headless testing runs real UI tests in CI without a display.
- `FlowDirection="RightToLeft"` mirrors the entire visual tree, which is what genuine RTL requires.
- MIT licence throughout, no redistribution restrictions.

Negative, and accepted:

- Avalonia's UI Automation support is less complete than WPF's. Mitigation: full keyboard operation
  and automation names on every control; the gap is recorded in `docs/KNOWN_LIMITATIONS.md`.
- Controls are drawn, not native, so the application will not inherit every Windows high-contrast
  or accessibility behaviour automatically.
- Printing needs `System.Drawing.Printing`, whose APIs are Windows-only and produce CA1416
  diagnostics that must be handled with explicit platform guards rather than suppressed globally.

## Rejected alternatives

WPF and WinUI 3 were rejected only because they cannot be built or tested in the available
environment — on a Windows development machine WPF would be a defensible choice, and the
architecture deliberately keeps the UI layer thin so that a future port would touch one project.
Tauri was rejected for the WebView2 runtime dependency, Electron for size and attack surface.
