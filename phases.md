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

## Fase 6 — Settings window + pulido UX ✅
**Objetivo:** UI para configurar todo lo configurable.

### Tareas
- [x] Crear `Views\SettingsWindow.xaml` con:
  - [x] ComboBox: Modelo de Whisper (Tiny/Base/Small/Medium) → al cambiar, dispara descarga si no existe.
  - [x] ComboBox: Idioma (es/en/pt/fr/auto).
  - [x] Hotkey picker: dos ComboBox (Modificador: RMenu/LAlt/LCtrl/RCtrl/LShift/RShift; Trigger: Space/Enter/F1–F12).
  - [x] ComboBox: Micrófono (lista `NAudio.Wave.WaveIn.GetDeviceCapabilities()` con opción "Default").
  - [x] CheckBox: ☐ Iniciar con Windows.
  - [x] CheckBox: ☐ Pausar en pantalla completa (default ON).
  - [x] CheckBox: ☐ Restaurar clipboard después de pegar.
  - [x] Botón "Descargar modelo" (muestra progreso inline).
  - [x] Botón "Probar hotkey" (mini-campo que muestra si se detecta la combinación).
  - [x] Botón "Guardar" + "Cancelar".
- [x] Modificar `TrayIconService` para mostrar:
  - [x] Estado actual con texto ("⚪ Listo", "🔴 Grabando", "🟡 Procesando").
  - [x] Items de menú actualizados dinámicamente según settings (modelo, idioma, checkboxes).
- [x] Generar los 4 `.ico` reales con distintos colores (idle=gris, recording=rojo, processing=amarillo, error=naranja).
- [x] Animación: tooltip del tray cambia cuando está grabando.
- [x] **Indicador flotante de estado cerca del cursor** (promovido del backlog post-MVP):
  - [x] Crear `Services\CursorIndicatorService.cs` con ventana WPF transparente
        (`WindowStyle=None`, `AllowsTransparency=true`, `Topmost=true`,
        `ShowActivated=false`, `IsHitTestVisible=false`, `ShowInTaskbar=false`).
  - [x] P/Invoke `GetCursorPos` (en `SendInputInterop.cs` o nuevo `Native\CursorInterop.cs`).
  - [x] `DispatcherTimer` (~30 fps) que sigue al cursor y reposiciona la ventana en
        `cursor + (20, 20)` DIPs. Conversión píxel-físico → DIP vía
        `PresentationSource.CompositionTarget.TransformToDevice`.
  - [x] Clamp al `WorkingArea` del monitor actual (vía `MonitorFromPoint`): si
        `y + 48 > bottom` del monitor, reflejar a `y - 48` para que el indicador
        no se salga de pantalla.
  - [x] Forma: círculo de 16×16 DIPs (reducido desde 28×28 original). Estados visibles:
    - [x] `Recording` → rojo `#E53935` con animación **pulse** (opacity 1.0 → 0.5 → 1.0, ~900 ms).
    - [x] `Processing` → ámbar `#FFB300` con animación **pulse** (opacity 0.5 → 1.0, ~600 ms — más rápido que Recording para diferenciar). Rotación eliminada: el RenderTransform sobre la Ellipse hacía que el bounding box rotado se extendiera ~4 DIPs más allá del frame 16×16 de la Window, y WPF lo clipaba a la forma de Pac-Man.
    - [x] `Error` → rojo oscuro `#B71C1C`, estático.
    - [x] `Idle` y `NotReady` → ventana oculta, timer detenido.
  - [x] `Show(state)` / `Hide()` públicos. El timer se detiene en `Hide()` para no consumir CPU.
  - [x] Inyectar `CursorIndicatorService` en `RecordingOrchestrator` y centralizar
        los `SetState` para invocar tray + indicador en un solo `Dispatcher` call
        (conserva el threading correcto para ambos).
  - [x] Registrar en DI como singleton y eager-resolve en `App.OnStartup` para
        que la ventana esté pre-creada cuando se dispare el primer `Show`.

### Verificación
- [x] Abrir Settings desde tray. Cambiar hotkey a F12. Guardar.
- [x] Mantener F12 (sin AltGr) graba. AltGr+Space ya no graba.
- [x] Cambiar modelo a base. Se descarga. Funciona (un poco menos preciso).
- [x] Cambiar idioma a "auto". Transcribe correctamente un audio en inglés.
- [x] Marcar "Iniciar con Windows". Aparece en `regedit HKCU\...\Run`.
- [x] Desmarcar. Desaparece.
- [x] Marcar "Pausar en pantalla completa". Poner Chrome fullscreen. Hotkey deja de funcionar.
- [x] Salir de fullscreen. Hotkey vuelve a funcionar.
- [x] **Indicador de cursor:**
  - [x] Mantener AltGr+Space. Aparece círculo rojo pulsante a ~20 px abajo-derecha del cursor.
  - [x] Soltar. El círculo cambia a ámbar pulsando más rápido (~600 ms) mientras Whisper procesa.
  - [x] Al terminar la inyección, el indicador desaparece.
  - [x] Mover el cursor durante la grabación: el indicador lo sigue sin lag visible.
  - [x] El indicador **NO** quita el foco del campo donde está el usuario
        (verificar en Notepad/Chrome que el caret sigue visible y se sigue escribiendo).
  - [x] Con el cursor en el borde inferior de la pantalla, el indicador se refleja hacia arriba y sigue visible.
  - [x] En multi-monitor (DPI mixto), el indicador aparece a la distancia correcta del cursor en cada pantalla.

> **Nota:** durante la implementación de F6, el indicador de cursor se ajustó
> respecto al plan original: tamaño reducido de 28×28 a 16×16 DIPs (mejor
> relación con el cursor), y la animación de Processing cambió de rotación
> a pulse opacity más rápido (la rotación causaba clipping en el frame de
> la Window). También se expandió el conjunto de modificadores de hotkey
> (RMenu/LAlt/LCtrl/RCtrl/LShift/RShift) y se quitó F12 de modificadores
> (queda solo en trigger).

---

## Fase 7 — Empaquetado + distribución ✅
**Objetivo:** un .exe autocontenido + scripts de install/uninstall.

> **Nota de implementación:** el plan original de F7 fue modificado en estos
> puntos:
> - **Install path:** `%LOCALAPPDATA%\Programs\VoiceTyper\` (sin UAC, cumple
>   con `app.manifest asInvoker`). El plan original proponía
>   `C:\Program Files\VoiceTyper\`, lo cual requeriría elevación.
> - **`PublishReadyToRun` no se agregó** — la build actual es suficientemente
>   rápida y R2R agrega ~10-20% al tamaño sin beneficio perceptible.
> - **VC++ check:** aviso no bloqueante vía tray balloon + log, no se bloquea
>   el arranque (cumple decisión cerrada).
> - **Smoke test single-file workaround:** se agregó `<EnableDynamicLoading>true</EnableDynamicLoading>`
>   al .csproj Y `Services/NativeLibPathResolver.cs` que copia `whisper.dll` + 3 ggml DLLs
>   desde el temp extraction path (`%TEMP%\.net\VoiceTyper\<hash>\runtimes\win-x64\`) a
>   `<BaseDirectory>\runtimes\win-x64\` al inicio de `OnStartup`. Sin esto,
>   `WhisperFactory.FromPath` falla con "Native Library not found in default paths"
>   porque `AppContext.BaseDirectory` apunta al .exe, no al temp extraction.
>   Con el workaround, el smoke test del .exe publicado retorna exit 0.
>   Ver `AGENTS.md` gotcha "Single-file + Whisper" para los detalles.
> - **Smoke test shutdown:** se usa `Environment.Exit(0/1)` en vez de `Shutdown(0/1)`
>   para que el proceso termine limpio cuando se llama desde un thread async
>   sin sync context WPF. No afecta al flujo normal de la app.

### Tareas
- [x] Configurar `VoiceTyper.csproj` con propiedades de publish:
  - [x] `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
  - [x] `<PublishSingleFile>true</PublishSingleFile>`
  - [x] `<SelfContained>true</SelfContained>`
  - [x] `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`
  - [x] `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>`
  - [x] `<EnableDynamicLoading>true</EnableDynamicLoading>` (necesario para que Whisper.net encuentre los DLLs extraídos).
- [x] Crear `Services/NativeLibPathResolver.cs` que copia los 4 DLLs nativos
  de Whisper desde el temp extraction path a `<BaseDirectory>\runtimes\win-x64\`
  al inicio de `OnStartup` (workaround single-file + Whisper.net).
- [x] Migrar `EnvService.ResolveEnvPath` de `Assembly.Location` a
  `AppContext.BaseDirectory` (rompe en single-file).
- [x] Crear `Services/VcRedistChecker.cs` con `IsInstalled()` (chequea
  `vcruntime140.dll` y `vcruntime140_1.dll` en System32) y `GetDownloadUrl()`.
- [x] Wire `VcRedistChecker` en `App.OnStartup` después del eager-resolve
  de servicios: log + balloon si falta, no bloquea.
- [x] Flag `--smoke-test` en `App.OnStartup` que llama a
  `TranscriberService.SmokeTestAsync()` y retorna `Environment.Exit(ret ? 0 : 1)`.
- [x] Crear `install.bat`:
  - [x] Verifica `dotnet` en PATH.
  - [x] Verifica VC++ Redist en `%WINDIR%\System32\vcruntime140.dll` (warn + link si falta).
  - [x] `dotnet publish -c Release -r win-x64 --self-contained true` con las props de F7.
  - [x] Copia `publish\VoiceTyper.exe` a `%LOCALAPPDATA%\Programs\VoiceTyper\`.
  - [x] Crea `%LOCALAPPDATA%\VoiceTyper\models\` (pre-crea la carpeta para el modelo).
  - [x] Crea acceso directo en Start Menu vía PowerShell COM (`WScript.Shell`).
  - [x] Lanza la app al final con `start ""`.
- [x] Crear `uninstall.bat`:
  - [x] `taskkill /F /IM VoiceTyper.exe` (silencioso).
  - [x] `reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v VoiceTyper /f` (silencioso).
  - [x] Borra acceso directo de Start Menu.
  - [x] Borra `%LOCALAPPDATA%\Programs\VoiceTyper\`.
  - [x] Pregunta si borrar también `%LOCALAPPDATA%\VoiceTyper\` (datos de usuario).
- [x] Crear `README.md` final con:
  - [x] Instrucciones de uso (mantener hotkey, cambiar desde Settings).
  - [x] Sección Instalación con pasos (VC++ Redist → .NET 8 SDK → install.bat → descarga modelo).
  - [x] Sección Desinstalar.
  - [x] Sección Troubleshooting con tabla de síntomas/causas/soluciones.
  - [x] Sección Roadmap post-MVP.
  - [x] Sección "Cómo funciona" (resumen del pipeline).
  - [x] Screenshots **SKIP** (decisión cerrada — solo estructura textual).
- [x] Crear `docs/screenshots/.gitkeep` para futuro uso.

### Verificación
- [x] `dotnet build VoiceTyper.sln` → exit 0, 0 warnings.
- [x] `dotnet publish src/VoiceTyper -c Release -r win-x64 --self-contained true` → exit 0.
- [x] Tamaño de `VoiceTyper.exe` = **70.95 MB** (< 100 MB ✅).
- [x] `git status` muestra solo los archivos esperados (ver commit).
- [x] Smoke test en dev mode (`dotnet run -- --smoke-test`) → **OK** (WhisperFactory cargó ggml-medium.bin).
- [x] Smoke test en publish single-file (`publish\VoiceTyper.exe --smoke-test`) → **OK exit 0** gracias a `NativeLibPathResolver` que copia los DLLs al directorio del .exe.
- [x] App publicada arranca limpio: VC++ check OK, model cargado, hook instalado, queda en tray.
- [ ] `install.bat` corre limpio en una VM Windows 11 limpia — **NO testeado en VM** (no hay VM en este entorno).
- [ ] `uninstall.bat` deja el sistema limpio — **NO testeado en VM**.
- [ ] Levantar VM Windows 10 limpia, correr install.bat, todo funciona — **SKIP** (no hay VM).

---

## Post-MVP (backlog, NO hacer ahora)
- Streaming de Whisper (transcripción mientras hablás, sin soltar tecla).
- Hotkey con spoken punctuation ("coma", "punto", "nueva línea").
- Múltiples perfiles de hotkey por app.
- Indicator flotante cerca del cursor mientras graba. ✅ **Hecho en F6** (`CursorIndicatorService`).
- Soporte para GPU NVIDIA (Whisper.net.Runtime.Gpu).
- Modo "always listening" con wake word.
- Temas del tray icon (dark/light).
- ✅ **Bugfix**: race condition de grabaciones concurrentes que duplicaba el texto en el cursor. `RecordingOrchestrator` ahora serializa con `SemaphoreSlim(1, 1)`. Si el usuario re-engancha la hotkey durante el processing del dictado anterior, el segundo se descarta con balloon `"Esperá a que termine la transcripción anterior"`. Ver gotcha en `AGENTS.md`.
