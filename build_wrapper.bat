@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"

set PJDIR=C:\Users\Pierre\Documents\SoftPhone\deps\pjproject
set NATIVE=C:\Users\Pierre\Documents\SoftPhone\native
set OUTPUT=C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone

echo [INFO] Compiling pjsip_wrapper.dll...

cl /LD /O2 /MD /DPJ_WIN32=1 /DPJ_WIN64=1 /DPJMEDIA_HAS_SRTP=0 /DPJ_IS_BIG_ENDIAN=0 /DPJ_IS_LITTLE_ENDIAN=1 /D_CRT_SECURE_NO_WARNINGS ^
  /I"%PJDIR%\pjlib\include" ^
  /I"%PJDIR%\pjlib-util\include" ^
  /I"%PJDIR%\pjnath\include" ^
  /I"%PJDIR%\pjmedia\include" ^
  /I"%PJDIR%\pjsip\include" ^
  "%NATIVE%\pjsip_wrapper.c" ^
  /Fe:"%OUTPUT%\pjsip_wrapper.dll" ^
  /link ^
  /LIBPATH:"%PJDIR%\lib" ^
  libpjproject-x86_64-x64-vc14-Release.lib ^
  ws2_32.lib ole32.lib oleaut32.lib uuid.lib winmm.lib ^
  dsound.lib mswsock.lib advapi32.lib gdi32.lib user32.lib iphlpapi.lib

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] pjsip_wrapper.dll built
    if exist "%OUTPUT%\pjsip_wrapper.dll" (
        echo [SUCCESS] DLL is at: %OUTPUT%\pjsip_wrapper.dll
    )
) else (
    echo [FAILED] Wrapper build failed with code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)
