@echo off
setlocal
cd /d "%~dp0"
if not exist "logs" mkdir "logs"
set "NG_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%NG_CSC%" set "NG_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%NG_CSC%" goto missing
echo Menyiapkan aplikasi Windows. Tidak memerlukan internet...
"%NG_CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 /win32manifest:"src\app.manifest" /out:"KartuBacaNgaju.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll "src\App.cs" > "logs\build.txt" 2>&1
if errorlevel 1 goto failed
echo Memeriksa kartu, gambar, navigasi, dan urutan acak...
start "" /wait "%~dp0KartuBacaNgaju.exe" --self-test
if errorlevel 1 goto testfailed
echo 0.4.2> "logs\build-ui-0.4.2.ok"
echo Berhasil. Aplikasi siap dibuka.
if /i not "%~1"=="nopause" pause
exit /b 0
:missing
echo Compiler .NET Framework bawaan Windows tidak ditemukan. > "logs\build.txt"
echo Komponen .NET Framework Windows perlu diperiksa.
goto failed
:testfailed
echo Pemeriksaan otomatis gagal. Baca logs\aplikasi.log.
if /i not "%~1"=="nopause" pause
exit /b 1
:failed
echo Proses belum berhasil. Tutup aplikasi jika masih terbuka, lalu coba lagi.
type "logs\build.txt"
if /i not "%~1"=="nopause" pause
exit /b 1
