@echo off
setlocal EnableDelayedExpansion

:: VoiceTyper uninstaller
:: Cierra la app, limpia el registry y la carpeta de instalacion.
:: Opcionalmente borra los datos de usuario (settings, modelos, logs).

:: 1. Cerrar la app si esta corriendo
echo Cerrando VoiceTyper si esta en ejecucion...
taskkill /F /IM VoiceTyper.exe >nul 2>&1
timeout /t 2 /nobreak >nul

:: 2. Borrar autostart del Registry
echo Eliminando entrada de autoarranque...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v VoiceTyper /f >nul 2>&1

:: 3. Borrar acceso directo del Start Menu
if exist "%LOCALAPPDATA%\Microsoft\Windows\Start Menu\Programs\VoiceTyper.lnk" (
    del /F /Q "%LOCALAPPDATA%\Microsoft\Windows\Start Menu\Programs\VoiceTyper.lnk"
)

:: 4. Borrar carpeta de instalacion
  set "INSTALL_DIR=%LOCALAPPDATA%\Programs\VoiceTyper"
  if exist "%INSTALL_DIR%" (
      echo Eliminando %INSTALL_DIR%...
      rd /s /q "%INSTALL_DIR%"
  )

:: 5. Preguntar si quiere borrar settings, modelos y logs
set "DATA_DIR=%LOCALAPPDATA%\VoiceTyper"
if exist "%DATA_DIR%" (
    echo.
    echo Se encontraron datos de usuario en:
    echo   %DATA_DIR%
    echo Esto incluye settings, modelos Whisper descargados y logs.
    echo.
    set /p "BORRAR=¿Borrarlos tambien? (S/N): "
    if /i "!BORRAR!"=="S" (
        rd /s /q "%DATA_DIR%"
        echo Datos de usuario eliminados.
    ) else (
        echo Datos de usuario conservados en %DATA_DIR%.
    )
)

echo.
echo VoiceTyper ha sido desinstalado.
endlocal
pause
