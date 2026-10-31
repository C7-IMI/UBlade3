@echo off
call "C:\tools\vsbt\VC\Auxiliary\Build\vcvars64.bat"
"C:\Users\StephenSimmons\AppData\Local\Programs\GameTools\CMake\4.4.4\cmake-4.4.4-windows-x86_64\bin\cmake.exe" --preset msvc-x64 -DHYPERX_GAME_DATA=D:/Games/HyperBlade/HYB
exit /b %ERRORLEVEL%
