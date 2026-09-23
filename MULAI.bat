@echo off
setlocal
cd /d "%~dp0"
if not exist "KartuBacaNgaju.exe" goto build
if exist "logs\build-ui-0.4.2.ok" goto run
:build
call "%~dp0BANGUN-ULANG.bat" nopause
if errorlevel 1 goto failed
:run
start "" "%~dp0KartuBacaNgaju.exe"
exit /b 0
:failed
echo.
echo Aplikasi belum berhasil dibuat. Lihat logs\build.txt.
pause
exit /b 1
