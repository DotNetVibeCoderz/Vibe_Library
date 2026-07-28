<#
.SYNOPSIS
    Builds the native lvglnet library on Windows and stages it into
    runtimes/win-x64/native.

.DESCRIPTION
    Uses CMake, Ninja and MSVC from PATH when they are there. Otherwise it falls
    back to the copies Visual Studio ships - a normal VS install has all three,
    but none of them are on PATH outside a Developer Command Prompt, so the
    common case is that this script has to find them itself.

.EXAMPLE
    ./native/build.ps1
    ./native/build.ps1 -NoDemos
#>
[CmdletBinding()]
param(
    [switch]$NoDemos,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $root 'native/build'

function Find-VisualStudio {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path $vswhere) {
        # -latest alone would ignore a Build Tools-only install, so ask for the C++ toolset.
        $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
                           -property installationPath 2>$null
        if ($path) { return $path.Trim() }

        $path = & $vswhere -latest -products * -property installationPath 2>$null
        if ($path) { return $path.Trim() }
    }
    return $null
}

function Import-MsvcEnvironment([string]$vsPath) {
    $vcvars = Join-Path $vsPath 'VC/Auxiliary/Build/vcvars64.bat'
    if (-not (Test-Path $vcvars)) {
        throw "Visual Studio was found at '$vsPath' but the C++ tools are missing (no vcvars64.bat). Install the 'Desktop development with C++' workload."
    }

    # vcvars only affects the cmd process it runs in, so its environment is dumped and copied
    # across. The installer directory has to be on PATH first: vcvars shells out to vswhere.
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer'
    $output = cmd /c "set `"PATH=%PATH%;$installer`" && call `"$vcvars`" >nul 2>&1 && set"

    foreach ($line in $output) {
        if ($line -match '^([^=]+)=(.*)$') {
            Set-Item -Path "env:$($matches[1])" -Value $matches[2] -ErrorAction SilentlyContinue
        }
    }

    if (-not (Get-Command cl -ErrorAction SilentlyContinue)) {
        throw "Could not initialise the MSVC environment from '$vcvars'."
    }
}

# --- locate the toolchain -------------------------------------------------

$vsPath = $null

if (-not (Get-Command cl -ErrorAction SilentlyContinue)) {
    $vsPath = Find-VisualStudio
    if (-not $vsPath) {
        throw @'
No C compiler found.

Install the "Desktop development with C++" workload from the Visual Studio
Installer, or run this script from a Developer Command Prompt.
'@
    }
    Write-Host "Using MSVC from $vsPath"
    Import-MsvcEnvironment $vsPath
}

$cmake = (Get-Command cmake -ErrorAction SilentlyContinue).Source
if (-not $cmake) {
    if (-not $vsPath) { $vsPath = Find-VisualStudio }
    $bundled = Join-Path $vsPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
    if (Test-Path $bundled) { $cmake = $bundled }
}
if (-not $cmake) {
    throw 'CMake was not found. Install it with "winget install Kitware.CMake", or add the "C++ CMake tools for Windows" component in the Visual Studio Installer.'
}

$ninja = (Get-Command ninja -ErrorAction SilentlyContinue).Source
if (-not $ninja) {
    $bundled = Join-Path $vsPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
    if (Test-Path $bundled) { $ninja = $bundled }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git was not found; it is needed to clone LVGL. Install it with "winget install Git.Git".'
}

Write-Host "Using CMake  $cmake"
if ($ninja) { Write-Host "Using Ninja  $ninja" }

# --- configure and build --------------------------------------------------

if ($Clean -and (Test-Path $buildDir)) {
    Write-Host "Removing $buildDir"
    Remove-Item -Recurse -Force $buildDir
}

$demos = if ($NoDemos) { 'OFF' } else { 'ON' }

$configureArgs = @(
    '-S', (Join-Path $root 'native')
    '-B', $buildDir
    "-DLVGLNET_WITH_DEMOS=$demos"
    '-DCMAKE_BUILD_TYPE=Release'
)

if ($ninja) {
    $configureArgs += @('-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$ninja")
}

& $cmake @configureArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed ($LASTEXITCODE)" }

& $cmake --build $buildDir --config Release
if ($LASTEXITCODE -ne 0) { throw "cmake build failed ($LASTEXITCODE)" }

$staged = Join-Path $root 'runtimes/win-x64/native/lvglnet.dll'
if (Test-Path $staged) {
    $kb = [int]((Get-Item $staged).Length / 1KB)
    Write-Host ""
    Write-Host "Staged: $staged ($kb KB)" -ForegroundColor Green
} else {
    Write-Warning "Build succeeded but $staged is missing - check the POST_BUILD step."
}
