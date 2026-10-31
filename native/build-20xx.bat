@echo off
call "C:\tools\vsbt\VC\Auxiliary\Build\vcvars64.bat"
cd /d "C:\OneDrive\OneDrive - Cyber 7 Group\GHRepo\Games\UBlade3\third_party\hyperblade-20XX"
"C:\Users\StephenSimmons\AppData\Local\Programs\GameTools\CMake\4.4.4\cmake-4.4.4-windows-x86_64\bin\cmake.exe" --build --preset msvc-x64
exit /b %ERRORLEVEL%
