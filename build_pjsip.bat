@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
echo [INFO] MSVC environment loaded
echo [INFO] Building PJSIP Release x64...
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" "C:\Users\Pierre\Documents\SoftPhone\deps\pjproject\pjproject-vs14.sln" /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /p:WindowsTargetPlatformVersion=10.0 /m /t:Build /v:minimal
if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] PJSIP build complete
) else (
    echo [FAILED] PJSIP build failed with code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
