# Native AOT Windows build from Linux

Cross-compiles C# to a native AOT **Windows x64** binary (`.dll` or `.exe`) using native Linux tools.
No container, no Windows VM, no Visual Studio.

## How it works

Toolchain:

- [**`ilc`**](https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/ilc-architecture.md), the .NET IL Compiler, with `--targetos:windows`, produces an x86-64 **COFF** object.
- [**`lld-link`**](https://lld.llvm.org/), the LLVM linker, links that object into a PE image (like MSVC `link.exe` would).
- [**`xwin`**](https://github.com/Jake-Shadle/xwin) fetches Microsoft's MSVC CRT and Windows SDK import libraries as required by the .NET native runtime.

## Prerequisites

| Tool | Package | Needed for |
| --- | --- | --- |
| .NET SDK 8.0+ | `dotnet-sdk` | C# and IL compilers |
| `lld-link` | `lld` | linking the PE image |
| `clang` | `clang` | only for cross-compiling native C/C++ |
| `curl` | `curl` | downloading the SDK |
| `wine` | `wine` | optional, for testing the compiled result |

`setup.sh` checks all of these and names what is missing.

### License notice

Setup downloads Microsoft's MSVC CRT and Windows SDK. Doing so is governed by the [Visual Studio license terms](https://visualstudio.microsoft.com/license-terms/). `setup.sh` refuses to run unless you say so explicitly, via `--accept-license`. Read those terms before running it.

## Usage

One-time setup (about 640 MB into `~/.local/share/win-aot-sdk`):

```sh
./setup.sh --accept-license
```

Verify it works:

```sh
./selftest.sh
```

### Wine prefix

Step 4 runs the output under `wine`, which needs a wine prefix. It lives in
`~/.cache/win-aot-selftest/prefix` by default. The script will refuse to run
if `WINEPREFIX` points anywhere inside the repository, because a prefix with
the standard symlink of `dosdevices/z: -> /` will cause various tooling to
expensively misbehave during recursive searches of the project directory.

Set `WINEPREFIX` to override the location. Because it sits outside the `selftest/out`
directory the script clears on each run, repeat runs reuse the prefix instead of
rebuilding it.

### Compile

Compile a project:

```sh
./build.sh path/to/MyProxy.csproj
./build.sh path/to/MyProxy.csproj -p:OptimizationPreference=Size
```

Anything after the project path is passed through to `dotnet publish`. Output lands in the project's normal `bin/<config>/<tfm>/win-x64/publish/` path.

To compile native C/C++ against the same SDK, use the `exec` escape hatch, which runs any command with `LIB` and `INCLUDE` already pointed at the Windows SDK:

```sh
./build.sh exec clang --target=x86_64-pc-windows-msvc -fuse-ld=lld-link ...
```

Install the SDK somewhere else with `WINSDK_ROOT=/opt/winsdk ./setup.sh --accept-license`; `build.sh` and `selftest.sh` honor the same variable.

## What the project needs to declare

### Opting in to native AOT

`build.sh` supplies only the cross-compilation plumbing. Opting into native AOT stays with the project:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <PublishAot>true</PublishAot>
  <NativeLib>Shared</NativeLib>   <!-- produces a .dll; omit for an .exe -->
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

### Exports

Exports are declared with `UnmanagedCallersOnly`:

```csharp
[UnmanagedCallersOnly(EntryPoint = "DirectInput8Create")]
public static int DirectInput8Create(nint hinst, uint version, nint riid, nint ppvOut, nint punkOuter) { ... }
```

`RuntimeIdentifier` is supplied by `build.sh` (`win-x64`); do not pin a different one in the project.

### Target framework

The AOT binary embeds whatever runtime the project targets, and it is fully self-contained: a native AOT DLL loaded into a process that already hosts CoreCLR (as Atomcraft does) does not share or conflict with that runtime. Any `TargetFramework` the installed SDK can build will work, as long as the matching ILCompiler packages can be restored from NuGet.

## Required MSBuild properties

`build.sh` passes these; they are the whole trick.

| Property | Why |
| --- | --- |
| `DisableUnsupportedError=true` | The SDK hard-errors with "Cross-OS native compilation is not supported" before doing anything. The check is purely a guard, gated on this property; ILC itself cross-targets fine. |
| `CppLinker=./win-link` | The default is `link`, i.e. MSVC's linker. On Linux that resolves to GNU coreutils `link`, which fails with "missing operand". `win-link` wraps `lld-link` with `/IGNORE:4099`. |
| `CppLibCreator=./win-lib` | Same problem for `lib.exe`. Only used when `NativeLib=Static`. |
| `EnableSourceLink=false` | The SDK passes `/SOURCELINK:` to the linker; `lld-link` does not implement that option and treats it as a missing input file. Costs only source-server metadata in the PDB. |

Library search paths come from the `LIB` environment variable, set by `config.sh`. `lld-link` splits it on `;` (Windows style), **not** `:`. A colon-separated `LIB` is silently ignored and every import library fails to resolve.

## Verifying output

`selftest.sh` does the full loop: builds `ProbeLib.dll`, checks it is a PE32+ image exporting `ProbeExport`, cross-compiles `probe.c` which `LoadLibrary`s it, and (when wine is installed) runs it and checks the export returns the right value.

Ad hoc inspection:

```sh
file path/to/MyProxy.dll
llvm-readobj --coff-exports path/to/MyProxy.dll
```

## Known rough edges

- The build prints a few `findvcvarsall.bat` shell errors. The SDK unconditionally tries to locate Visual Studio by running a `.bat` file; the failure is expected and harmless, because `LIB` is already set.
- PDBs are produced but are of limited use, since Microsoft does not ship PDBs for the redistributable CRT.
- Only `win-x64` is wired up. For `win-arm64`, re-run setup with `WINSDK_ARCH=aarch64` and build with `RID=win-arm64`.
- Because the toolchain is the host's, an `lld` or .NET upgrade changes the build. TODO: Containerize
