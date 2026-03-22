#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Fully automated setup for SoftPhone - SIP Softphone application.
    Installs all dependencies, compiles PJSIP, builds the C# project, and launches the app.

.DESCRIPTION
    This script performs the following steps:
    1. Installs Visual Studio 2022 Build Tools (C++ & .NET workloads)
    2. Installs .NET 8 SDK
    3. Installs CMake
    4. Installs Git
    5. Clones and compiles PJSIP from source
    6. Builds the native pjsip_wrapper.dll
    7. Builds the WPF SoftPhone application
    8. Launches the application

    Run as Administrator: Right-click PowerShell -> Run as Administrator -> .\setup.ps1
#>

param(
    [switch]$SkipInstall,
    [switch]$SkipPjsip,
    [switch]$SkipBuild,
    [switch]$NoLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ─── Configuration ───
$ProjectRoot    = $PSScriptRoot
$NativeDir      = Join-Path $ProjectRoot "native"
$SrcDir         = Join-Path $ProjectRoot "src\SoftPhone"
$BuildDir       = Join-Path $ProjectRoot "build"
$PjsipDir       = Join-Path $ProjectRoot "deps\pjproject"
$PjsipBuildDir  = Join-Path $BuildDir "pjsip"
$WrapperBuildDir = Join-Path $BuildDir "wrapper"
$OutputDir      = Join-Path $SrcDir "bin\Release\net8.0-windows"
$TempDir        = Join-Path $env:TEMP "softphone_setup"

# ─── Helper Functions ───

function Write-Step($msg) {
    Write-Host ""
    Write-Host "====================================================" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "====================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Info($msg) {
    Write-Host "  [INFO] $msg" -ForegroundColor Gray
}

function Write-Ok($msg) {
    Write-Host "  [OK]   $msg" -ForegroundColor Green
}

function Write-Warn($msg) {
    Write-Host "  [WARN] $msg" -ForegroundColor Yellow
}

function Write-Err($msg) {
    Write-Host "  [ERR]  $msg" -ForegroundColor Red
}

function Test-CommandExists($cmd) {
    $null -ne (Get-Command $cmd -ErrorAction SilentlyContinue)
}

function Invoke-Download($url, $outFile) {
    Write-Info "Downloading $url ..."
    if (!(Test-Path (Split-Path $outFile))) {
        New-Item -ItemType Directory -Path (Split-Path $outFile) -Force | Out-Null
    }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $wc = New-Object System.Net.WebClient
    $wc.DownloadFile($url, $outFile)
    Write-Ok "Downloaded to $outFile"
}

function Refresh-Path {
    $machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$machinePath;$userPath"
}

function Find-VsWhere {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) { return $vswhere }
    $vswhere = "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) { return $vswhere }
    return $null
}

function Find-VcVars {
    $vswhere = Find-VsWhere
    if ($vswhere) {
        $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
        if ($installPath) {
            $vcvars = Join-Path $installPath "VC\Auxiliary\Build\vcvars64.bat"
            if (Test-Path $vcvars) { return $vcvars }
        }
    }

    # Fallback: search common paths
    $paths = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $p }
    }
    return $null
}

function Invoke-VcCmd($cmd) {
    $vcvars = Find-VcVars
    if (!$vcvars) {
        throw "Cannot find vcvars64.bat. Is Visual Studio 2022 with C++ workload installed?"
    }
    $fullCmd = "`"$vcvars`" && $cmd"
    cmd /c $fullCmd
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE : $cmd"
    }
}

# ═══════════════════════════════════════════════════════
#  STEP 1: Install Prerequisites
# ═══════════════════════════════════════════════════════

if (!$SkipInstall) {
    New-Item -ItemType Directory -Path $TempDir -Force | Out-Null

    # ── 1a. Visual Studio 2022 Build Tools ──
    Write-Step "Step 1/7: Checking Visual Studio 2022 Build Tools"

    $vcvars = Find-VcVars
    if ($vcvars) {
        Write-Ok "Visual Studio C++ tools found: $vcvars"
    } else {
        Write-Info "Installing Visual Studio 2022 Build Tools..."
        $vsInstaller = Join-Path $TempDir "vs_buildtools.exe"
        Invoke-Download "https://aka.ms/vs/17/release/vs_BuildTools.exe" $vsInstaller

        Write-Info "Running Visual Studio installer (this may take 10-30 minutes)..."
        $vsArgs = @(
            "--quiet", "--wait", "--norestart",
            "--add", "Microsoft.VisualStudio.Workload.VCTools",
            "--add", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "--add", "Microsoft.VisualStudio.Component.Windows11SDK.22621",
            "--add", "Microsoft.VisualStudio.Workload.ManagedDesktop",
            "--add", "Microsoft.VisualStudio.Component.VC.CMake.Project",
            "--includeRecommended"
        )
        $proc = Start-Process -FilePath $vsInstaller -ArgumentList $vsArgs -Wait -PassThru
        if ($proc.ExitCode -notin 0, 3010) {
            Write-Err "VS Build Tools install failed (exit code $($proc.ExitCode))"
            Write-Warn "You may need to install Visual Studio 2022 manually with C++ Desktop workload"
        } else {
            Write-Ok "Visual Studio 2022 Build Tools installed"
        }
        Refresh-Path
    }

    # ── 1b. .NET 8 SDK ──
    Write-Step "Step 2/7: Checking .NET 8 SDK"

    $dotnetOk = $false
    if (Test-CommandExists "dotnet") {
        $sdks = dotnet --list-sdks 2>&1
        if ($sdks -match "8\.\d+\.\d+") {
            Write-Ok ".NET 8 SDK already installed"
            $dotnetOk = $true
        }
    }

    if (!$dotnetOk) {
        Write-Info "Installing .NET 8 SDK..."
        $dotnetInstaller = Join-Path $TempDir "dotnet-sdk-8.exe"
        Invoke-Download "https://dot.net/v1/dotnet-install.ps1" (Join-Path $TempDir "dotnet-install.ps1")
        & (Join-Path $TempDir "dotnet-install.ps1") -Channel 8.0 -InstallDir "$env:ProgramFiles\dotnet"
        Refresh-Path

        if (!(Test-CommandExists "dotnet")) {
            $env:Path += ";$env:ProgramFiles\dotnet"
            [Environment]::SetEnvironmentVariable("Path", $env:Path, "Machine")
        }
        Write-Ok ".NET 8 SDK installed"
    }

    # ── 1c. CMake ──
    Write-Step "Step 3/7: Checking CMake"

    if (Test-CommandExists "cmake") {
        Write-Ok "CMake already installed: $(cmake --version | Select-Object -First 1)"
    } else {
        Write-Info "Installing CMake..."
        $cmakeVersion = "3.28.3"
        $cmakeInstaller = Join-Path $TempDir "cmake-installer.msi"
        Invoke-Download "https://github.com/Kitware/CMake/releases/download/v$cmakeVersion/cmake-$cmakeVersion-windows-x86_64.msi" $cmakeInstaller

        Start-Process msiexec.exe -ArgumentList "/i", $cmakeInstaller, "/quiet", "/norestart", "ADD_CMAKE_TO_PATH=System" -Wait
        Refresh-Path

        if (!(Test-CommandExists "cmake")) {
            $env:Path += ";$env:ProgramFiles\CMake\bin"
            [Environment]::SetEnvironmentVariable("Path", $env:Path, "Machine")
        }
        Write-Ok "CMake installed"
    }

    # ── 1d. Git ──
    Write-Step "Step 4/7: Checking Git"

    if (Test-CommandExists "git") {
        Write-Ok "Git already installed: $(git --version)"
    } else {
        Write-Info "Installing Git..."
        $gitInstaller = Join-Path $TempDir "git-installer.exe"
        Invoke-Download "https://github.com/git-for-windows/git/releases/download/v2.44.0.windows.1/Git-2.44.0-64-bit.exe" $gitInstaller

        Start-Process $gitInstaller -ArgumentList "/VERYSILENT", "/NORESTART", "/NOCANCEL", "/SP-", "/CLOSEAPPLICATIONS", "/RESTARTAPPLICATIONS", "/COMPONENTS=ext\shellhere,assoc,assoc_sh" -Wait
        Refresh-Path

        if (!(Test-CommandExists "git")) {
            $env:Path += ";$env:ProgramFiles\Git\cmd"
            [Environment]::SetEnvironmentVariable("Path", $env:Path, "Machine")
        }
        Write-Ok "Git installed"
    }
}

# ═══════════════════════════════════════════════════════
#  STEP 2: Clone and Build PJSIP
# ═══════════════════════════════════════════════════════

if (!$SkipPjsip) {
    Write-Step "Step 5/7: Building PJSIP"

    $depsDir = Join-Path $ProjectRoot "deps"
    New-Item -ItemType Directory -Path $depsDir -Force | Out-Null

    # Clone PJSIP
    if (!(Test-Path (Join-Path $PjsipDir "pjlib"))) {
        Write-Info "Cloning PJSIP from GitHub..."
        git clone --depth 1 --branch "2.14.1" "https://github.com/pjsip/pjproject.git" $PjsipDir
        if ($LASTEXITCODE -ne 0) {
            # Fallback to master if tag not found
            Write-Warn "Tag 2.14.1 not found, cloning master..."
            if (Test-Path $PjsipDir) { Remove-Item $PjsipDir -Recurse -Force }
            git clone --depth 1 "https://github.com/pjsip/pjproject.git" $PjsipDir
        }
        Write-Ok "PJSIP source cloned"
    } else {
        Write-Ok "PJSIP source already present"
    }

    # Create config_site.h for Windows build
    $configSiteDir = Join-Path $PjsipDir "pjlib\include\pj"
    $configSite = Join-Path $configSiteDir "config_site.h"
    Write-Info "Writing config_site.h..."
    @"
/* Auto-generated by setup.ps1 */
#define PJ_WIN32 1
#define PJ_WIN64 1
#define PJMEDIA_HAS_SRTP 0
#define PJMEDIA_HAS_VIDEO 0
#define PJMEDIA_AUDIO_DEV_HAS_WMME 1
#define PJMEDIA_AUDIO_DEV_HAS_WASAPI 0
#define PJ_HAS_SSL_SOCK 0
#define PJ_IS_BIG_ENDIAN 0
#define PJ_IS_LITTLE_ENDIAN 1
"@ | Set-Content $configSite -Encoding UTF8
    Write-Ok "config_site.h created"

    # Build PJSIP using Visual Studio
    Write-Info "Building PJSIP with MSBuild (Release x64)..."

    # Find the VS solution file
    $pjSlnCandidates = @(
        (Join-Path $PjsipDir "pjproject-vs14.sln"),
        (Join-Path $PjsipDir "pjproject.sln")
    )
    $pjSln = $null
    foreach ($candidate in $pjSlnCandidates) {
        if (Test-Path $candidate) {
            $pjSln = $candidate
            break
        }
    }

    if ($pjSln) {
        Write-Info "Found PJSIP solution: $pjSln"

        # ── Retarget all .vcxproj files from v140/v141/v142 to v143 (VS 2022) ──
        Write-Info "Retargeting all .vcxproj files to PlatformToolset v143 (VS 2022)..."
        $vcxprojFiles = Get-ChildItem -Path $PjsipDir -Filter "*.vcxproj" -Recurse
        $retargetCount = 0
        foreach ($vcx in $vcxprojFiles) {
            $content = [System.IO.File]::ReadAllText($vcx.FullName)
            $original = $content

            # Replace any PlatformToolset v100-v142 with v143 (match all variations)
            $content = $content -replace '<PlatformToolset>v\d+</PlatformToolset>', '<PlatformToolset>v143</PlatformToolset>'

            # Replace or insert WindowsTargetPlatformVersion
            if ($content -match '<WindowsTargetPlatformVersion>') {
                $content = $content -replace '<WindowsTargetPlatformVersion>[^<]+</WindowsTargetPlatformVersion>', '<WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>'
            } else {
                # Insert WindowsTargetPlatformVersion in first PropertyGroup that has PlatformToolset
                $content = $content -replace '(<PlatformToolset>v143</PlatformToolset>)', '<WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>$1'
            }

            if ($content -ne $original) {
                [System.IO.File]::WriteAllText($vcx.FullName, $content)
                $retargetCount++
            }
        }
        Write-Ok "Retargeted $retargetCount .vcxproj files to v143"

        # Also retarget .vcxproj.filters and .sln if needed for ToolsVersion
        $slnContent = [System.IO.File]::ReadAllText($pjSln)
        if ($slnContent -match 'ToolsVersion="14\.0"') {
            $slnContent = $slnContent -replace 'ToolsVersion="14\.0"', 'ToolsVersion="17.0"'
            [System.IO.File]::WriteAllText($pjSln, $slnContent)
            Write-Ok "Solution file retargeted to ToolsVersion 17.0"
        }

        # Build with MSBuild through vcvars - force v143 toolset via properties
        Invoke-VcCmd "msbuild `"$pjSln`" /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /p:WindowsTargetPlatformVersion=10.0 /m /t:Build /v:minimal"
        Write-Ok "PJSIP built successfully via MSBuild"
    } else {
        # Fallback: use configure + make approach via CMake or nmake
        Write-Info "No VS solution found. Attempting build with PJSIP's aconfigure..."

        # For Windows without a .sln, we build individual projects
        $projects = @("pjlib", "pjlib-util", "pjnath", "pjmedia", "pjsip")
        foreach ($proj in $projects) {
            $projBuild = Join-Path $PjsipDir "$proj\build"
            if (Test-Path (Join-Path $projBuild "*.vcxproj")) {
                $vcxproj = Get-ChildItem (Join-Path $projBuild "*.vcxproj") | Select-Object -First 1
                Write-Info "Building $proj..."
                Invoke-VcCmd "msbuild `"$($vcxproj.FullName)`" /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /p:WindowsTargetPlatformVersion=10.0 /v:minimal"
            }
        }
        Write-Ok "PJSIP projects built"
    }

    # Collect libraries into a known location
    $pjLibDir = Join-Path $PjsipDir "lib"
    New-Item -ItemType Directory -Path $pjLibDir -Force | Out-Null

    # Copy all .lib files from various output directories
    $libSearchPaths = @(
        (Join-Path $PjsipDir "pjlib\lib"),
        (Join-Path $PjsipDir "pjlib-util\lib"),
        (Join-Path $PjsipDir "pjnath\lib"),
        (Join-Path $PjsipDir "pjmedia\lib"),
        (Join-Path $PjsipDir "pjsip\lib"),
        (Join-Path $PjsipDir "third_party\lib"),
        (Join-Path $PjsipDir "lib")
    )

    foreach ($searchPath in $libSearchPaths) {
        if (Test-Path $searchPath) {
            Get-ChildItem -Path $searchPath -Filter "*.lib" -Recurse -ErrorAction SilentlyContinue |
                ForEach-Object {
                    $dest = Join-Path $pjLibDir $_.Name
                    if (!(Test-Path $dest)) {
                        Copy-Item $_.FullName $dest
                        Write-Info "  Collected: $($_.Name)"
                    }
                }
        }
    }
    Write-Ok "PJSIP libraries collected in $pjLibDir"

    # ── Build the native wrapper DLL ──
    Write-Step "Step 6/7: Building pjsip_wrapper.dll"

    New-Item -ItemType Directory -Path $WrapperBuildDir -Force | Out-Null

    $vcvars = Find-VcVars
    if (!$vcvars) { throw "vcvars64.bat not found" }

    # Use CMake to build the wrapper
    $cmakeCmd = "cmake -S `"$NativeDir`" -B `"$WrapperBuildDir`" -G `"Visual Studio 17 2022`" -A x64 -DPJSIP_DIR=`"$PjsipDir`""
    Write-Info "Running: $cmakeCmd"
    Invoke-VcCmd $cmakeCmd

    $buildCmd = "cmake --build `"$WrapperBuildDir`" --config Release"
    Write-Info "Running: $buildCmd"
    Invoke-VcCmd $buildCmd

    # Find and copy the built DLL
    $wrapperDll = Get-ChildItem -Path $WrapperBuildDir -Filter "pjsip_wrapper.dll" -Recurse | Select-Object -First 1
    if ($wrapperDll) {
        Copy-Item $wrapperDll.FullName (Join-Path $SrcDir "pjsip_wrapper.dll") -Force
        Write-Ok "pjsip_wrapper.dll built and copied to project"
    } else {
        Write-Err "pjsip_wrapper.dll not found after build!"
        Write-Warn "Attempting manual compilation..."

        # Fallback: direct cl.exe compilation
        $includeFlags = @(
            "/I`"$PjsipDir\pjlib\include`"",
            "/I`"$PjsipDir\pjlib-util\include`"",
            "/I`"$PjsipDir\pjnath\include`"",
            "/I`"$PjsipDir\pjmedia\include`"",
            "/I`"$PjsipDir\pjsip\include`""
        ) -join " "

        $libFiles = (Get-ChildItem -Path $pjLibDir -Filter "*.lib" | ForEach-Object { "`"$($_.FullName)`"" }) -join " "
        $sysLibs = "ws2_32.lib ole32.lib oleaut32.lib uuid.lib winmm.lib dsound.lib mswsock.lib advapi32.lib gdi32.lib user32.lib iphlpapi.lib"

        $clCmd = "cl /LD /O2 /DPJ_WIN32=1 /DPJ_WIN64=1 /DPJMEDIA_HAS_SRTP=0 /DPJ_IS_BIG_ENDIAN=0 /DPJ_IS_LITTLE_ENDIAN=1 /D_CRT_SECURE_NO_WARNINGS /DWRAPPER_EXPORTS $includeFlags `"$NativeDir\pjsip_wrapper.c`" /Fe:`"$SrcDir\pjsip_wrapper.dll`" /link $libFiles $sysLibs"
        Invoke-VcCmd $clCmd
        Write-Ok "pjsip_wrapper.dll built via cl.exe fallback"
    }
}

# ═══════════════════════════════════════════════════════
#  STEP 3: Build the WPF Application
# ═══════════════════════════════════════════════════════

if (!$SkipBuild) {
    Write-Step "Step 7/7: Building SoftPhone WPF Application"

    # Restore NuGet packages
    Write-Info "Restoring NuGet packages..."
    dotnet restore $SrcDir
    Write-Ok "NuGet packages restored"

    # Build in Release mode
    Write-Info "Building application..."
    dotnet build $SrcDir -c Release -r win-x64 --no-restore
    Write-Ok "Application built"

    # Publish as self-contained (optional, for distribution)
    Write-Info "Publishing application..."
    dotnet publish $SrcDir -c Release -r win-x64 --self-contained false -o (Join-Path $BuildDir "publish")
    Write-Ok "Application published to $BuildDir\publish"

    # Ensure wrapper DLL is in output
    $publishDir = Join-Path $BuildDir "publish"
    $wrapperSrc = Join-Path $SrcDir "pjsip_wrapper.dll"
    if (Test-Path $wrapperSrc) {
        Copy-Item $wrapperSrc $publishDir -Force
        Copy-Item $wrapperSrc $OutputDir -Force -ErrorAction SilentlyContinue
        Write-Ok "pjsip_wrapper.dll copied to output directories"
    }
}

# ═══════════════════════════════════════════════════════
#  STEP 4: Launch the Application
# ═══════════════════════════════════════════════════════

if (!$NoLaunch) {
    Write-Step "Launching SoftPhone"

    $exeCandidates = @(
        (Join-Path $BuildDir "publish\SoftPhone.exe"),
        (Join-Path $OutputDir "SoftPhone.exe")
    )

    $exe = $null
    foreach ($candidate in $exeCandidates) {
        if (Test-Path $candidate) {
            $exe = $candidate
            break
        }
    }

    if ($exe) {
        Write-Ok "Starting: $exe"
        Start-Process $exe
    } else {
        Write-Warn "Could not find SoftPhone.exe. Try running manually:"
        Write-Info "  dotnet run --project `"$SrcDir`""
    }
}

# ═══════════════════════════════════════════════════════
#  Cleanup
# ═══════════════════════════════════════════════════════

Write-Host ""
Write-Host "====================================================" -ForegroundColor Green
Write-Host "  Setup Complete!" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green
Write-Host ""
Write-Info "Project:   $ProjectRoot"
Write-Info "Solution:  $ProjectRoot\SoftPhone.sln"
Write-Info "Output:    $BuildDir\publish"
Write-Host ""
Write-Info "To rebuild manually:"
Write-Info "  dotnet build src\SoftPhone -c Release"
Write-Host ""
Write-Info "To run manually:"
Write-Info "  dotnet run --project src\SoftPhone"
Write-Host ""
