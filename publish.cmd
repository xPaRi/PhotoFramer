@echo off
rem Zabali PhotoFramer do jednoho EXE.
rem
rem   publish.cmd         sobestacny EXE (obsahuje .NET, nic se neinstaluje, cca 70-100 MB)
rem   publish.cmd small   maly EXE (cca 10 MB), na pocitaci musi byt nainstalovan .NET 10 Desktop Runtime
rem
rem Vysledek: publish\PhotoFramer.exe

setlocal
cd /d "%~dp0"

set OUT=publish
set OUT2=c:\wapp\PhotoFramer
set MODE=%~1

if /i "%MODE%"=="small" (
    set SELFCONTAINED=false
    set COMPRESS=false
    echo Publikuji malou verzi - vyzaduje .NET 10 Desktop Runtime...
) else (
    set SELFCONTAINED=true
    set COMPRESS=true
    echo Publikuji sobestacnou verzi...
)

if exist "%OUT%" rmdir /s /q "%OUT%"

dotnet publish src\PhotoFramer.App\PhotoFramer.App.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained %SELFCONTAINED% ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=%COMPRESS% ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -o "%OUT%"

if errorlevel 1 (
    echo.
    echo CHYBA: publikovani selhalo.
    exit /b 1
)

if exist "%OUT2%" copy /Y "%OUT%\PhotoFramer.exe" "%OUT2%"

echo.
echo Hotovo: %CD%\%OUT%\PhotoFramer.exe
for %%F in ("%OUT%\PhotoFramer.exe") do echo Velikost: %%~zF B
endlocal
