# VoiceTyper — Plan de ejecución por fases

Documento ejecutable. Cada fase termina con su checklist de aceptación.
No avanzar a la siguiente fase hasta que la actual esté ✅ completa.

---

## Fase 0 — Bootstrap del proyecto
**Objetivo:** dejar la carpeta lista para desarrollo.

### Tareas
- [ ] Crear estructura de carpetas:
  ```
  C:\Users\victor\proyectos\VoiceTyper\
  ├── spec.md
  ├── phases.md
  ├── README.md
  └── src\
  ```
- [ ] Crear `README.md` placeholder con descripción del proyecto.
- [ ] Crear `src\.gitignore` para .NET.

### Verificación
- [ ] `Test-Path C:\Users\victor\proyectos\VoiceTyper\src` → `True`.

---

## Fase 1 — Esqueleto WPF + System Tray
**Objetivo:** app que arranca, queda residente en tray, sale limpia.

### Tareas
- [ ] Crear solución: `dotnet new sln -n VoiceTyper -o C:\Users\victor\proyectos\VoiceTyper`.
- [ ] Crear proyecto WPF: `dotnet new wpf -n VoiceTyper -o src\VoiceTyper -f net8.0-windows`.
- [ ] Agregar a la solución: `dotnet sln add src\VoiceTyper`.
- [ ] Configurar `VoiceTyper.csproj`:
  - [ ] `<TargetFramework>net8.0-windows</TargetFramework>`
  - [ ] `<UseWPF>true</UseWPF>`
  - [ ] `<Nullable>enable</Nullable>`
  - [ ] `<ApplicationIcon>Resources\app.ico</ApplicationIcon>`
  - [ ] `<AssemblyName>VoiceTyper</AssemblyName>`
  - [ ] `<RootNamespace>VoiceTyper</RootNamespace>`
- [ ] Agregar NuGet packages:
  - [ ] `Hardcodet.NotifyIcon.Wpf` (tray)
  - [ ] `Microsoft.Extensions.Hosting` (DI)
  - [ ] `CommunityToolkit.Mvvm` (MVVM)
- [ ] En `App.xaml`:
  - [ ] Eliminar `StartupUri="MainWindow.xaml"`.
  - [ ] Definir recursos para `TaskbarIcon` (de Hardcodet).
- [ ] En `App.xaml.cs`:
  - [ ] `OnStartup`: crear `Mutex` global `Global\VoiceTyper_SingleInstance`.
    Si ya existe → `Shutdown()` con exit code 0.
  - [ ] Si mutex creado: inicializar `Host` con DI, instanciar `TrayIconService`.
  - [ ] `OnExit`: dispose del host, release del mutex.
- [ ] Crear `Services\TrayIconService.cs`:
  - [ ] Crea un `TaskbarIcon` con icono `Resources\tray-idle.ico` (placeholder).
  - [ ] ContextMenu con items: "Configuración…", "—", "Salir".
  - [ ] Click en "Salir" → `Application.Current.Shutdown()`.
- [ ] Crear `Resources\` con 4 placeholders `.ico`:
  - [ ] `tray-idle.ico`, `tray-recording.ico`, `tray-processing.ico`, `tray-error.ico`
  - (pueden ser el mismo .ico por ahora; se diferencian en fases siguientes).
- [ ] `MainWindow.xaml`: solo contiene un `TextBlock` con "VoiceTyper running".
  - [ ] `WindowState = Minimized`, `ShowInTaskbar = false`, `Visibility = Hidden`
    al iniciar (se reabre solo desde "Configuración…").
- [ ] Configurar `app.manifest` con `requestedExecutionLevel level="asInvoker"`
  (no queremos UAC).

### Verificación
- [ ] `dotnet build` sin warnings ni errores.
- [ ] `dotnet run` → aparece icono en tray, sin ventana visible.
- [ ] Click derecho en tray → menú con "Salir".
- [ ] Click en "Salir" → app se cierra limpia.
- [ ] Doble click en `dotnet run` mientras hay una instancia → solo queda 1 proceso
  (verificar en Task Manager).
- [ ] No aparece `VoiceTyper` en Taskbar (solo en tray).

---

## Fase 2 — Hook global de teclado (push-to-talk)
**Objetivo:** detectar AltGr+Space down/up globalmente.

### Tareas
- [ ] Crear `Native\LowLevelKeyboardHook.cs`:
  - [ ] P/Invoke `SetWindowsHookEx(WH_KEYBOARD_LL, ...)`, `UnhookWindowsHookEx`.
  - [ ] `HookProc` recibe `wParam` (WM_KEYDOWN/WM_KEYUP/WM_SYSKEYDOWN/WM_SYSKEYUP)
    y `lParam` (puntero a KBDLLHOOKSTRUCT).
  - [ ] Exponer eventos: `KeyDown(VirtualKey)`, `KeyUp(VirtualKey)`.
  - [ ] Helper `IsKeyPressed(ushort vk)` consultando `GetAsyncKeyState`.
  - [ ] Filtrar autorepeat: ignorar keydown si la key ya estaba down.
  - [ ] Método `SuppressNext()` para que el próximo evento no se propague
    (usado para consumir Space durante grabación).
  - [ ] Singleton gestionado por DI, IDisposable.
- [ ] Crear `Services\HotkeyService.cs`:
  - [ ] Configurado por defecto: `Modifier = RightAlt (VK_RMENU)`, `Trigger = Space (VK_SPACE)`.
  - [ ] Estado interno: `IsRecording = false`.
  - [ ] Suscribe a `KeyDown` del hook: si modifier down + trigger down → `IsRecording = true`.
  - [ ] Suscribe a `KeyUp`: si trigger up mientras `IsRecording` → `IsRecording = false`.
  - [ ] Expone evento `RecordingStarted` y `RecordingStopped` (TaskCompletionSource para
    esperar a que termine la transcripción async).
  - [ ] Mientras `IsRecording`, llama a `SuppressNext()` en cada KeyDown de Space
    para que no llegue a la app destino.
  - [ ] Si la app en foco está fullscreen exclusivo (detectar con
    `GetForegroundWindow` + `GetWindowLongPtr(GWL_EXSTYLE) & WS_EX_TOPMOST` y
    verificar DWM): pausar el hook (deshabilitar flag global).
- [ ] Registrar el hook al iniciar la app (después del tray).
- [ ] En esta fase, log a consola + `Debug.WriteLine` cuando se detecta down/up.

### Verificación
- [ ] Abrir Notepad. Mantener AltGr+Space → log dice "KeyDown Space, recording=true".
- [ ] Soltar Space → log dice "KeyUp Space, recording=false".
- [ ] En Notepad NO se escribe ningún espacio mientras se mantiene la combinación.
- [ ] En VSCode, Chrome, Word: misma verificación.
- [ ] Abrir un juego fullscreen (o un video fullscreen en Chrome) → log dice "paused".
- [ ] Cerrar juego → log dice "resumed".
- [ ] No se imprimen keystrokes de autorepeat.

---

## Fase 3 — Captura de audio con NAudio
**Objetivo:** mientras se graba, capturar audio a MemoryStream.

### Tareas
- [ ] Agregar NuGet `NAudio` (última estable).
- [ ] Crear `Services\AudioRecorderService.cs`:
  - [ ] `using NAudio.Wave;`
  - [ ] `WaveInEvent` con `WaveFormat = 16000 Hz, 16 bit, 1 channel (mono)`.
  - [ ] Suscribe a `DataAvailable` → acumula en `MemoryStream` (escribir WAV
    header al inicio).
  - [ ] Al detener: completar header WAV, devolver `byte[]`.
  - [ ] Métodos `Start()` y `StopAsync()`.
  - [ ] Property `IsRecording`.
  - [ ] Configurable: device number (default `-1` = default del sistema).
  - [ ] Timeout de seguridad: auto-stop a los 5 minutos.
- [ ] Crear `Models\RecordingState.cs`:
  - [ ] `enum RecordingState { Idle, Recording, Processing, Error }`
- [ ] En `HotkeyService` (o un nuevo `RecordingOrchestrator`):
  - [ ] `RecordingStarted` → `AudioRecorderService.Start()` + `TrayIconService.SetState(Recording)`.
  - [ ] `RecordingStopped` → `byte[] audio = await AudioRecorderService.StopAsync()`
    + `TrayIconService.SetState(Processing)`.
  - [ ] Por ahora: log del tamaño del audio. Transcripción se hace en Fase 4.

### Verificación
- [ ] Mantener AltGr+Space 3 segundos diciendo algo. Soltar.
- [ ] En el log aparece: "Recording started", luego "Recording stopped, N bytes captured".
- [ ] El byte array empieza con `RIFF....WAVE` (header válido).
- [ ] En el tray, el icono cambia a rojo al grabar, vuelve a gris al soltar.
- [ ] No se escucha feedback (eco) en los auriculares.

---

## Fase 4 — Whisper.net: descarga del modelo y transcripción
**Objetivo:** el byte[] se convierte a texto en español.

### Tareas
- [ ] Agregar NuGet `Whisper.net` y `Whisper.net.Runtime` (CPU).
- [ ] Crear `Models\WhisperModel.cs`:
  - [ ] `enum WhisperModel { Tiny, Base, Small, Medium }`
  - [ ] `string GetFileName()` → `ggml-{name}.bin`
  - [ ] `string GetDownloadUrl()` → URL de HuggingFace (`ggerganov/whisper.cpp`).
  - [ ] `long GetApproxSizeBytes()`.
- [ ] Crear `Services\ModelManagerService.cs`:
  - [ ] Carpeta `%AppData%\VoiceTyper\models\`.
  - [ ] `EnsureModelAsync(WhisperModel)`: si no existe, descargar con
    `HttpClient` mostrando progreso (vía `IProgress<double>`).
  - [ ] Cancelable: `CancellationToken` en signature.
- [ ] Crear `Services\TranscriberService.cs`:
  - [ ] Lazy init del `WhisperFactory` y `WhisperProcessor` con el modelo cargado
    una sola vez (singleton).
  - [ ] `Task<string> TranscribeAsync(byte[] wavBytes, string language = "es", CancellationToken ct)`
  - [ ] Usa `WhisperProcessor.ProcessAsync(buffer, CancellationToken.None)`.
  - [ ] Configuración: `WhisperProcessorBuilder.WithLanguage(language).WithGreedySamplingStrategy().Build()`.
  - [ ] Devuelve el texto concatenando los segments (trim, sin prefijos como "[BLANK_AUDIO]").
  - [ ] `Dispose` adecuado.
- [ ] Crear `Services\SettingsService.cs`:
  - [ ] Carga/guarda `%AppData%\VoiceTyper\settings.json`.
  - [ ] Modelo `AppSettings { Model, Language, HotkeyModifier, HotkeyTrigger,
    AutoStart, PauseOnFullscreen, MicrophoneDeviceIndex }`.
  - [ ] Settings default razonables.
- [ ] En `RecordingOrchestrator`, después de capturar audio:
  - [ ] `TrayIconService.SetState(Processing)`.
  - [ ] `text = await TranscriberService.TranscribeAsync(audio)`.
  - [ ] `TrayIconService.SetState(Idle)`.
  - [ ] Por ahora: log del texto transcrito.
- [ ] Manejo de errores:
  - [ ] Si falla: `LoggerService.LogError(ex)`, `TrayIconService.SetState(Error)` por 2s.
  - [ ] Si texto vacío: volver a idle silenciosamente.

### Verificación
- [ ] Primer arranque → aparece diálogo de descarga del modelo (~460 MB).
  - [ ] Progreso visible.
  - [ ] Cancelable (botón "Cancelar").
  - [ ] Se reanuda si se interrumpe (reutilizar `.tmp` parcial).
- [ ] Tras descarga, mantener AltGr+Space, decir "hola mundo", soltar.
- [ ] Log muestra: `"hola mundo"`.
- [ ] Decir una frase larga (20+ segundos). Transcripción aparece en < 5s.
- [ ] Si el modelo falla en cargar (archivo corrupto): log de error, no crashea.
- [ ] Verificar `%AppData%\VoiceTyper\models\ggml-small.bin` existe.

---

## Fase 5 — Inyección de texto (clipboard + Ctrl+V)
**Objetivo:** el texto transcrito se pega donde está el cursor.

### Tareas
- [ ] Crear `Native\ClipboardInterop.cs`:
  - [ ] P/Invoke `OpenClipboard`, `CloseClipboard`, `GetClipboardData`, `SetClipboardData`.
  - [ ] Helper `string? GetText()` y `bool SetText(string)`.
  - [ ] Helper `RestoreFromBackup(IDataObject backup)`.
  - [ ] Usar `Clipboard.SetDataObject(text, copy: true)` de WPF (más simple que Win32).
- [ ] Crear `Native\SendInputInterop.cs`:
  - [ ] P/Invoke `SendInput` con `INPUT` struct (KEYBOARD type).
  - [ ] Método `SendCtrlV()`: 4 inputs (Ctrl down, V down, V up, Ctrl up).
  - [ ] `KEYBDINPUT` con `wVk = VK_CONTROL (0x11)`, `wVk = VK_V (0x56)`.
- [ ] Crear `Services\TextInjectorService.cs`:
  - [ ] `Task InjectAsync(string text)`:
    1. `var backup = Clipboard.GetDataObject()` (puede ser null).
    2. Retry 3 veces con 100ms backoff: `Clipboard.SetText(text)`.
    3. Si exitoso: `await Task.Delay(50)`, luego `SendInputInterop.SendCtrlV()`.
    4. `await Task.Delay(200)`.
    5. `if (backup != null) Clipboard.SetDataObject(backup, copy: true)` (best-effort).
  - [ ] Si falla el SetText: log + `TrayIconService.ShowBalloon("Clipboard ocupado, reintentá")`.
- [ ] En `RecordingOrchestrator`, después de transcribir:
  - [ ] Si texto no vacío: `await TextInjectorService.InjectAsync(text)`.
  - [ ] Si texto vacío: skip.

### Verificación
- [ ] Abrir Notepad, click en una línea vacía.
- [ ] Mantener AltGr+Space, decir "esto es una prueba", soltar.
- [ ] En Notepad aparece `esto es una prueba` (sin espacios extra al inicio/fin).
- [ ] Repetir en:
  - [ ] Chrome (campo de búsqueda de Google)
  - [ ] VSCode (línea de código)
  - [ ] Slack (input de mensaje)
  - [ ] Word
- [ ] Verificar que el clipboard se restauró (pegar con Ctrl+V en Notepad después:
  debería aparecer el contenido que estaba antes de la grabación, no el dictation).
- [ ] Si otra app está bloqueando el clipboard: aparece balloon de error.

---

## Fase 6 — Settings window + pulido UX
**Objetivo:** UI para configurar todo lo configurable.

### Tareas
- [ ] Crear `Views\SettingsWindow.xaml` con:
  - [ ] ComboBox: Modelo de Whisper (Tiny/Base/Small/Medium) → al cambiar, dispara descarga si no existe.
  - [ ] ComboBox: Idioma (es/en/pt/fr/auto).
  - [ ] Hotkey picker: dos ComboBox (Modificador: Left Ctrl/Right Alt/Left Shift/F12/Right Ctrl; Trigger: Space/Enter/F1-F12).
  - [ ] ComboBox: Micrófono (lista `NAudio.Wave.WaveIn.GetDeviceCapabilities()` con opción "Default").
  - [ ] CheckBox: ☐ Iniciar con Windows.
  - [ ] CheckBox: ☐ Pausar en pantalla completa (default ON).
  - [ ] CheckBox: ☐ Restaurar clipboard después de pegar.
  - [ ] Botón "Descargar modelo" (muestra progreso inline).
  - [ ] Botón "Probar hotkey" (mini-campo que muestra si se detecta la combinación).
  - [ ] Botón "Guardar" + "Cancelar".
- [ ] Modificar `TrayIconService` para mostrar:
  - [ ] Estado actual con texto ("⚪ Listo", "🔴 Grabando", "🟡 Procesando").
  - [ ] Items de menú actualizados dinámicamente según settings (modelo, idioma, checkboxes).
- [ ] Generar los 4 `.ico` reales con distintos colores (idle=gris, recording=rojo, processing=amarillo, error=naranja).
- [ ] Animación: tooltip del tray cambia cuando está grabando.

### Verificación
- [ ] Abrir Settings desde tray. Cambiar hotkey a F12. Guardar.
- [ ] Mantener F12 (sin AltGr) graba. AltGr+Space ya no graba.
- [ ] Cambiar modelo a base. Se descarga. Funciona (un poco menos preciso).
- [ ] Cambiar idioma a "auto". Transcribe correctamente un audio en inglés.
- [ ] Marcar "Iniciar con Windows". Aparece en `regedit HKCU\...\Run`.
- [ ] Desmarcar. Desaparece.
- [ ] Marcar "Pausar en pantalla completa". Poner Chrome fullscreen. Hotkey deja de funcionar.
- [ ] Salir de fullscreen. Hotkey vuelve a funcionar.

---

## Fase 7 — Empaquetado + distribución
**Objetivo:** un .exe autocontenido + scripts de install/uninstall.

### Tareas
- [ ] Configurar `VoiceTyper.csproj` con propiedades de publish:
  - [ ] `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
  - [ ] `<PublishSingleFile>true</PublishSingleFile>`
  - [ ] `<SelfContained>true</SelfContained>`
  - [ ] `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`
  - [ ] `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>`
  - [ ] `<PublishReadyToRun>true</PublishReadyToRun>`
- [ ] Crear `install.bat`:
  - [ ] `dotnet publish -c Release -r win-x64`
  - [ ] `if not exist "%LOCALAPPDATA%\VoiceTyper\models" mkdir "%LOCALAPPDATA%\VoiceTyper\models"`
  - [ ] `if not exist "%LOCALAPPDATA%\VoiceTyper\logs" mkdir "%LOCALAPPDATA%\VoiceTyper\logs"`
  - [ ] `if exist "C:\Program Files\VoiceTyper\" rd /s /q ...` (con admin check)
  - [ ] `mkdir "C:\Program Files\VoiceTyper"`
  - [ ] `copy /Y "src\VoiceTyper\bin\Release\net8.0-windows\win-x64\publish\VoiceTyper.exe" "C:\Program Files\VoiceTyper\"`
  - [ ] `powershell -Command "Start-Process 'C:\Program Files\VoiceTyper\VoiceTyper.exe'"`
- [ ] Crear `uninstall.bat`:
  - [ ] `taskkill /F /IM VoiceTyper.exe`
  - [ ] `reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v VoiceTyper /f`
  - [ ] `rd /s /q "C:\Program Files\VoiceTyper"`
  - [ ] `rd /s /q "%LOCALAPPDATA%\VoiceTyper"` (opcional, con confirmación)
- [ ] Crear `README.md` final con:
  - [ ] Capturas de pantalla (tray con estados, settings).
  - [ ] Instrucciones de uso.
  - [ ] Sección troubleshooting (VC++ Redist, hook no funciona, etc.).
  - [ ] Roadmap post-MVP.
- [ ] Verificar VC++ Redist: agregar check al inicio de la app
  (`LoggerService` ya creado en Fase 4):
  - [ ] Buscar `vcruntime140.dll` en `C:\Windows\System32`.
  - [ ] Si falta: `TrayIconService.ShowBalloon("Falta Visual C++ Redist, descargar de https://aka.ms/vs/17/release/vc_redist.x64.exe")`.

### Verificación
- [ ] `install.bat` corre limpio en una máquina Windows 11 limpia.
- [ ] `VoiceTyper.exe` aparece en tray tras instalar.
- [ ] Tamaño del publish: < 100 MB sin modelo.
- [ ] `uninstall.bat` deja el sistema limpio (verificar no quedan procesos, carpetas ni registry keys).
- [ ] Levantar VM Windows 10 limpia, correr install.bat, todo funciona.

---

## Post-MVP (backlog, NO hacer ahora)
- Streaming de Whisper (transcripción mientras hablás, sin soltar tecla).
- Hotkey con spoken punctuation ("coma", "punto", "nueva línea").
- Múltiples perfiles de hotkey por app.
- Indicator flotante cerca del cursor mientras graba.
- Soporte para GPU NVIDIA (Whisper.net.Runtime.Gpu).
- Modo "always listening" con wake word.
- Temas del tray icon (dark/light).
