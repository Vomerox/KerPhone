@echo off
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >/dev/null 2>&1
dumpbin /exports "C:\Users\Pierre\Documents\SoftPhone\publish\pjsip_wrapper.dll"
