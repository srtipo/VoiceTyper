# VoiceTyper — Plan de ejecución por fases

Documento ejecutable. Cada fase termina con su checklist de aceptación.
No avanzar a la siguiente fase hasta que la actual esté ✅ completa.

---

## Fase 0 — Bootstrap del proyecto ✅
**Objetivo:** dejar la carpeta lista para desarrollo.

### Tareas
- [x] Crear estructura de carpetas:
  ```
  C:\Users\victor\proyectos\VoiceTyper\
  ├── spec.md
  ├── phases.md
  ├── README.md
  └── src\
  ```
- [x] Crear `README.md` placeholder con descripción del proyecto.
- [x] Crear `src\.gitignore` para .NET.
- [x] **Inicializar git** en la raíz del proyecto.
- [x] **Crear `\.env.example`** en la raíz documentando todas las vars soportadas.
- [x] **Configurar `\.gitignore`** en la raíz para ignorar `.env` (pero NO `.env.example`).
- [x] **Crear `src\VoiceTyper\Services\EnvService.cs`** con loader minimalista.
- [x] **Wire `Env.Load()` en `App.OnStartup`** antes de construir el Host.

### Verificación
- [x] `Test-Path C:\Users\victor\proyectos\VoiceTyper\src` → `True`.
- [x] `git status` no muestra `.env` (aunque exista en disco).
- [x] `git status` muestra `.env.example` como tracked.
- [x] App arranca con y sin `.env` presente, sin crashear.

---

## Fase 1 — Esqueleto WPF + System Tray ✅
**Objetivo:** app que arranca, queda residente en tray, sale limpia.

### Tareas
- [x] Crear solución: `dotnet new sln -n VoiceTyper -o C:\Users\victor\proyectos\VoiceTyper`.
- [x] Crear proyecto WPF: `dotnet new wpf -n VoiceTyper -o src\VoiceTyper -f net8.0-windows`.
- [x] Agregar a la solución: `dotnet sln add src\VoiceTyper`.
- [x] Configurar `VoiceTyper.csproj`:
  - [x] `<TargetFramework>net8.0-windows</TargetFramework>`
  - [x] `<UseWPF>true</UseWPF>`
  - [x] `<Nullable>enable</Nullable>`
  - [x] `<ApplicationIcon>Resources\app.ico</ApplicationIcon>`
  - [x] `<AssemblyName>VoiceTyper</AssemblyName>`
  - [x] `<RootNamespace>VoiceTyper</RootNamespace>`
- [x] Agregar NuGet packages:
  - [x] `Hardcodet.NotifyIcon.Wpf` (tray)
  - [x] `Microsoft.Extensions.Hosting` (DI)
  - [x] `CommunityToolkit.Mvvm` (MVVM)
- [x] En `App.xaml`:
  - [x] Eliminar `StartupUri="MainWindow.xaml"`.
  - [x] Definir recursos para `TaskbarIcon` (de Hardcodet).
- [x] En `App.xaml.cs`:
  - [x] `OnStartup`: crear `Mutex` global `Global\VoiceTyper_SingleInstance`.
    Si ya existe → `Shutdown()` con exit code 0.
  - [x] Si mutex creado: inicializar `Host` con DI, instanciar `TrayIconService`.
  - [x] `OnExit`: dispose del host, release del mutex.
- [x] Crear `Services\TrayIconService.cs`:
  - [x] Crea un `TaskbarIcon` con icono `Resources\tray-idle.ico` (placeholder).
  - [x] ContextMenu con items: "Configuración…", "—", "Salir".
  - [x] Click en "Salir" → `Application.Current.Shutdown()`.
- [x] Crear `Resources\` con 4 placeholders `.ico`:
  - [x] `tray-idle.ico`, `tray-recording.ico`, `tray-processing.ico`, `tray-error.ico`
  - [x] (pueden ser el mismo .ico por ahora; se diferencian en fases siguientes).
- [x] `MainWindow.xaml`: solo contiene un `TextBlock` con "VoiceTyper running".
  - [x] `WindowState = Minimized`, `ShowInTaskbar = false`, `Visibility = Hidden`
    al iniciar (se reabre solo desde "Configuración…").
- [x] Configurar `app.manifest` con `requestedExecutionLevel level="asInvoker"`
  - [x] (no queremos UAC).

### Verificación
- [x] `dotnet build` sin warnings ni errores.
- [x] `dotnet run` → aparece icono en tray, sin ventana visible.
- [x] Click derecho en tray → menú con "Salir".
- [x] Click en "Salir" → app se cierra limpia.
- [x] Doble click en `dotnet run` mientras hay una instancia → solo queda 1 proceso
  - [x] (verificar en Task Manager).
- [x] No aparece `VoiceTyper` en Taskbar (solo en tray).

---

## Fase 2 — Hook global de teclado (push-to-talk) ✅
**Objetivo:** detectar AltGr+Space down/up globalmente.

### Tareas
- [x] Crear `Native\LowLevelKeyboardHook.cs`:
  - [x] P/Invoke `SetWindowsHookEx(WH_KEYBOARD_LL, ...)`, `UnhookWindowsHookEx`.
  - [x] `HookProc` recibe `wParam` (WM_KEYDOWN/WM_KEYUP/WM_SYSKEYDOWN/WM_SYSKEYUP)
    y `lParam` (puntero a KBDLLHOOKSTRUCT).
  - [x] Exponer eventos: `KeyDown(VirtualKey)`, `KeyUp(VirtualKey)`.
  - [x] Helper `IsKeyPressed(ushort vk)` consultando `GetAsyncKeyState`.
  - [x] Filtrar autorepeat: ignorar keydown si la key ya estaba down.
  - [x] Método `SuppressNext()` para que el próximo evento no se propague
    (usado para consumir Space durante grabación).
  - [x] Singleton gestionado por DI, IDisposable.
- [x] Crear `Services\HotkeyService.cs`:
  - [x] Configurado por defecto: `Modifier = RightAlt (VK_RMENU)`, `Trigger = Space (VK_SPACE)`.
  - [x] Estado interno: `IsRecording = false`.
  - [x] Suscribe a `KeyDown` del hook: si modifier down + trigger down → `IsRecording = true`.
  - [x] Suscribe a `KeyUp`: si trigger up mientras `IsRecording` → `IsRecording = false`.
  - [x] Expone evento `RecordingStarted` y `RecordingStopped` (TaskCompletionSource para
    esperar a que termine la transcripción async).
  - [x] Mientras `IsRecording`, llama a `SuppressNext()` en cada KeyDown de Space
    para que no llegue a la app destino.
  - [x] Si la app en foco está fullscreen exclusivo (detectar con
    `GetForegroundWindow` + `GetWindowLongPtr(GWL_EXSTYLE) & WS_EX_TOPMOST` y
    verificar DWM): pausar el hook (deshabilitar flag global).
- [x] Registrar el hook al iniciar la app (después del tray).
- [x] En esta fase, log a consola + `Debug.WriteLine` cuando se detecta down/up.

### Verificación
- [x] Abrir Notepad. Mantener AltGr+Space → log dice "KeyDown Space, recording=true".
- [x] Soltar Space → log dice "KeyUp Space, recording=false".
- [x] En Notepad NO se escribe ningún espacio mientras se mantiene la combinación.
- [x] En VSCode, Chrome, Word: misma verificación.
- [x] Abrir un juego fullscreen (o un video fullscreen en Chrome) → log dice "paused".
- [x] Cerrar juego → log dice "resumed".
- [x] No se imprimen keystrokes de autorepeat.

---

## Fase 3 — Captura de audio con NAudio ✅
**Objetivo:** mientras se graba, capturar audio a MemoryStream.

### Tareas
- [x] Agregar NuGet `NAudio` (última estable).
- [x] Crear `Services\AudioRecorderService.cs`:
  - [x] `using NAudio.Wave;`
  - [x] `WaveInEvent` con `WaveFormat = 16000 Hz, 16 bit, 1 channel (mono)`.
  - [x] Suscribe a `DataAvailable` → acumula en `MemoryStream` (escribir WAV
    header al inicio).
  - [x] Al detener: completar header WAV, devolver `byte[]`.
  - [x] Métodos `Start()` y `StopAsync()`.
  - [x] Property `IsRecording`.
  - [x] Configurable: device number (default `-1` = default del sistema).
  - [x] Timeout de seguridad: auto-stop a los 5 minutos.
- [x] Crear `Models\RecordingState.cs`:
  - [x] `enum RecordingState { Idle, Recording, Processing, Error }`
- [x] En `HotkeyService` (o un nuevo `RecordingOrchestrator`):
  - [x] `RecordingStarted` → `AudioRecorderService.Start()` + `TrayIconService.SetState(Recording)`.
  - [x] `RecordingStopped` → `byte[] audio = await AudioRecorderService.StopAsync()`
    + `TrayIconService.SetState(Processing)`.
  - [x] Por ahora: log del tamaño del audio. Transcripción se hace en Fase 4.

### Verificación
- [x] Mantener AltGr+Space 3 segundos diciendo algo. Soltar.
- [x] En el log aparece: "Recording started", luego "Recording stopped, N bytes captured".
- [x] El byte array empieza con `RIFF....WAVE` (header válido).
- [x] En el tray, el icono cambia a rojo al grabar, vuelve a gris al soltar.
- [x] No se escucha feedback (eco) en los auriculares.

---

## Fase 4 — Whisper.net: descarga del modelo y transcripción ✅
**Objetivo:** el byte[] se convierte a texto en español.

### Tareas
- [x] Agregar NuGet `Whisper.net` y `Whisper.net.Runtime` (CPU).
- [x] Crear `Models\WhisperModel.cs`:
  - [x] `enum WhisperModel { Tiny, Base, Small, Medium }`
  - [x] `string GetFileName()` → `ggml-{name}.bin`
  - [x] `string GetDownloadUrl()` → URL de HuggingFace (`ggerganov/whisper.cpp`).
  - [x] `long GetApproxSizeBytes()`.
- [x] Crear `Services\ModelManagerService.cs`:
  - [x] Carpeta default desde `Env.ModelDir`, con fallback a `%LOCALAPPDATA%\VoiceTyper\models\`.
  - [x] `EnsureModelAsync(WhisperModel)`: si no existe, descargar con
    `HttpClient` mostrando progreso (vía `IProgress<double>`).
  - [x] Cancelable: `CancellationToken` en signature.
- [x] Crear `Services\TranscriberService.cs`:
  - [x] Lazy init del `WhisperFactory` y `WhisperProcessor` con el modelo cargado
    una sola vez (singleton).
  - [x] `Task<string> TranscribeAsync(byte[] wavBytes, string language = "es", CancellationToken ct)`
  - [x] Usa `WhisperProcessor.ProcessAsync(buffer, CancellationToken.None)`.
  - [x] Configuración: `WhisperProcessorBuilder.WithLanguage(language).WithGreedySamplingStrategy().Build()`.
  - [x] Devuelve el texto concatenando los segments (trim, sin prefijos como "[BLANK_AUDIO]").
  - [x] `Dispose` adecuado.
- [x] Crear `Services\SettingsService.cs`:
  - [x] Carga/guarda `%AppData%\VoiceTyper\settings.json`.
  - [x] Modelo `AppSettings { Model, Language, HotkeyModifier, HotkeyTrigger,
    AutoStart, PauseOnFullscreen, MicrophoneDeviceIndex }`.
  - [x] **Cascada de defaults**: `Env` → `settings.json` → defaults del código.
  - [x] Settings default razonables.
- [x] En `RecordingOrchestrator`, después de capturar audio:
  - [x] `TrayIconService.SetState(Processing)`.
  - [x] `text = await TranscriberService.TranscribeAsync(audio)`.
  - [x] `TrayIconService.SetState(Idle)`.
  - [x] Por ahora: log del texto transcrito.
- [x] Manejo de errores:
  - [x] Si falla: `LoggerService.LogError(ex)`, `TrayIconService.SetState(Error)` por 2s.
  - [x] Si texto vacío: volver a idle silenciosamente.

### Verificación
- [x] Primer arranque → aparece diálogo de descarga del modelo (~460 MB).
  - [x] Progreso visible.
  - [x] Cancelable (botón "Cancelar").
  - [x] Se reanuda si se interrumpe (reutilizar `.tmp` parcial).
- [x] Tras descarga, mantener AltGr+Space, decir "hola mundo", soltar.
- [x] Log muestra: `"hola mundo"`.
- [x] Decir una frase larga (20+ segundos). Transcripción aparece en < 5s.
- [x] Si el modelo falla en cargar (archivo corrupto): log de error, no crashea.
- [x] Verificar `%AppData%\VoiceTyper\models\ggml-small.bin` existe.
- [x] Crear `.env` con `VT_MODEL_DIR=ruta\custom` y verificar que se usa esa carpeta.

---

## Fase 5 — Inyección de texto ✅
**Objetivo:** el texto transcrito se pega donde está el cursor.

> **Nota:** la implementación final diverge del plan original. Se descartaron
> los pasos "clipboard + Ctrl+V con `SendInput` virtual-key" porque los controles
> WinUI/XAML (Notepad moderno, etc.) ignoran tanto `WM_PASTE` como `SendInput`
> con VK_V cuando hay paste protection. El approach que terminó funcionando es
> `SendInput` con `KEYEVENTF_UNICODE` (bypassea paste protection, el target
> app recibe `WM_UNICHAR` por char).

### Tareas
- [x] Crear `Native\ClipboardInterop.cs`:
  - [x] `BackupAsync()`, `SetTextAsync(text, retries=3, backoff=100ms)`,
        `RestoreAsync(backup)`.
  - [x] Usar `Clipboard.SetDataObject(text, copy: true)` de WPF (dispatch al
        UI thread porque `Clipboard.*` lo requiere).
- [x] Crear `Native\SendInputInterop.cs`:
  - [x] P/Invoke `SendInput` con `INPUT` struct (KEYBOARD type).
  - [x] Constantes `INPUT_KEYBOARD`, `KEYEVENTF_KEYUP`, `KEYEVENTF_UNICODE`.
  - [x] Método `SendText(string text)`: por cada char, 2 INPUT events
        (down + up) con `wVk=0`, `wScan=<char>`, `dwFlags=KEYEVENTF_UNICODE`.
        Bypassea el paste protection de WinUI/XAML.
- [x] Crear `ClipboardInjector` (en el mismo archivo) como fallback:
  - [x] P/Invoke `GetForegroundWindow`, `GetFocus`, `SendMessage`.
  - [x] `SendPaste()`: envía `WM_PASTE` al HWND focused (fallback
        `GetForegroundWindow`).
- [x] `HotkeyService.OnHookKeyUp`: resetear `_hook.ConsumeNextKeyDown = false`
      cuando termina la grabación, para evitar que el flag residual
      (seteado por `ConsumeLoopAsync` cada 20ms) se coma el primer keydown
      sintetizado por el `SendInput` post-transcripción.
- [x] Crear `Services\TextInjectorService.cs`:
  - [x] `Task InjectAsync(string text)`:
    1. Dispatch al UI thread (`Application.Current.Dispatcher.InvokeAsync`).
    2. `SendInputInterop.SendText(text)` — método principal, no usa clipboard.
    3. Si retorna 0 → fallback: `ClipboardInterop.BackupAsync`,
       `SetTextAsync(text)`, delay 50ms, `ClipboardInjector.SendPaste()`,
       delay 200ms, `RestoreAsync(backup)` (si `RestoreClipboard` está ON).
  - [x] Si falla el SetText: log + balloon "Clipboard ocupado, reintentá".
- [x] En `RecordingOrchestrator`, después de transcribir:
  - [x] Si texto no vacío: `await TextInjectorService.InjectAsync(text)`.
  - [x] Si texto vacío: skip.

### Verificación
- [x] Abrir Notepad (moderno, Store), click en una línea vacía.
- [x] Mantener AltGr+Space, decir "esto es una prueba", soltar.
- [x] En Notepad aparece `esto es una prueba` (sin espacios extra al inicio/fin).
- [ ] Repetir en:
  - [ ] Chrome (campo de búsqueda de Google)
  - [ ] VSCode (línea de código)
  - [ ] Slack (input de mensaje)
  - [ ] Word
- [x] Si otra app está bloqueando el clipboard: aparece balloon de error.

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
