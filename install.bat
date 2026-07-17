@echo off
setlocal EnableDelayedExpansion

:: VoiceTyper installer
:: Compila la app (single-file, self-contained) y la copia a %LOCALAPPDATA%\Programs\VoiceTyper

:: 1. Verificar dotnet instalado
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] dotnet no esta instalado o no esta en el PATH.
    echo Descargalo desde https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

:: 2. Verificar VC++ Redist
if not exist "%WINDIR%\System32\vcruntime140.dll" (
    echo [WARN] No se detecto Visual C++ 2015-2022 Redistributable.
    echo VoiceTyper no funcionara hasta que lo instales.
    start "" "https://aka.ms/vc14/vc_redist.x64.exe"
    echo Instala el VC++ Redist y volve a correr este script.
    pause
    exit /b 1
)

:: 3. Definir rutas
set "SRC=%~dp0"
set "INSTALL_DIR=%LOCALAPPDATA%\Programs\VoiceTyper"
set "PUBLISH_DIR=%SRC%src\VoiceTyper\bin\Release\net8.0-windows\win-x64\publish"

:: 4. Publish
echo Compilando VoiceTyper (puede tardar 1-2 minutos)...
pushd "%SRC%"
dotnet publish src\VoiceTyper -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
if errorlevel 1 (
    popd
    echo [ERROR] dotnet publish fallo. Revisa los mensajes arriba.
    pause
    exit /b 1
)
popd

:: 5. Crear carpeta de instalacion
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

:: 6. Copiar ejecutable
if not exist "%PUBLISH_DIR%\VoiceTyper.exe" (
    echo [ERROR] No se encontro %PUBLISH_DIR%\VoiceTyper.exe
    pause
    exit /b 1
)
copy /Y "%PUBLISH_DIR%\VoiceTyper.exe" "%INSTALL_DIR%\VoiceTyper.exe" >nul
if errorlevel 1 (
    echo [ERROR] No se pudo copiar a %INSTALL_DIR%.
    pause
    exit /b 1
)

:: 7. Pre-crear carpeta de modelos
if not exist "%LOCALAPPDATA%\VoiceTyper\models" mkdir "%LOCALAPPDATA%\VoiceTyper\models"

:: 8. Acceso directo en Start Menu (via PowerShell)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s=(New-Object -COM WScript.Shell).CreateShortcut('%LOCALAPPDATA%\Microsoft\Windows\Start Menu\Programs\VoiceTyper.lnk');" ^
  "$s.TargetPath='%INSTALL_DIR%\VoiceTyper.exe';" ^
  "$s.WorkingDirectory='%INSTALL_DIR%';" ^
  "$s.IconLocation='%INSTALL_DIR%\VoiceTyper.exe,0';" ^
  "$s.Save()"

echo.
echo Instalacion completa.
echo VoiceTyper se ha copiado a: %INSTALL_DIR%
echo.
echo Iniciando VoiceTyper...
start "" "%INSTALL_DIR%\VoiceTyper.exe"
endlocal
