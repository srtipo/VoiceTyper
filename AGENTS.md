# AGENTS.md — VoiceTyper

App WPF de Windows (.NET 8, single-instance, sin UAC) que escucha voz con
`AltGr+Space` y la transcribe con Whisper local para pegarla donde esté el
cursor. Funciona en cualquier app: Notepad, Chrome, VSCode, Slack, Word.

- Especificación funcional: `spec.md`.
- Plan por fases y estado de implementación: `phases.md` (fuente de verdad de "qué existe").
- Stack: WPF + `Hardcodet.NotifyIcon.Wpf` + `Microsoft.Extensions.Hosting` (DI) + `CommunityToolkit.Mvvm`.

## Estado actual
**Fases 0–7 completadas** (bootstrap, esqueleto WPF + tray, hook global
AltGr+Space, captura NAudio, Whisper.net, inyección de texto con
`SendInput` Unicode + fallback `WM_PASTE`, Settings window + cursor
indicator + auto-start + tray menu dinámico, **empaquetado single-file
+ scripts install/uninstall + smoke-test + VC++ check**). `MainWindow.xaml`
es placeholder. Hoy: tray + hotkey configurable + grabar audio +
transcribir + inyectar texto donde esté el cursor (probado en Notepad
moderno).

## Comandos
Todo desde la raíz del repo. No hay script de build propio.

- Compilar: `dotnet build VoiceTyper.sln`
- Ejecutar (sólo Windows): `dotnet run --project src/VoiceTyper`
- Publicar single-file: `dotnet publish src/VoiceTyper -c Release -r win-x64 --self-contained true`
- Instalar (en la misma máquina): doble-click en `install.bat` desde la raíz.
- Desinstalar: doble-click en `uninstall.bat` desde la raíz.
- Smoke test del publicado: `"src\VoiceTyper\bin\Release\net8.0-windows\win-x64\publish\VoiceTyper.exe" --smoke-test` (exit 0 = OK, 1 = FAIL; ver gotcha sobre Whisper.net + single-file).
- No hay proyecto de tests. **No asumas xUnit/NUnit/MSTest** hasta que se cree.

## Layout
```
VoiceTyper.sln
install.bat                  ← F7: compila + copia a %LOCALAPPDATA%\Programs\VoiceTyper
uninstall.bat                ← F7: limpia registry + carpeta + datos (con confirmación)
docs/screenshots/            ← F7: placeholder para capturas futuras
src/VoiceTyper/
  App.xaml(.cs)              ← entrypoint; Mutex + DI host + tray
  MainWindow.xaml(.cs)       ← placeholder; OnClosing cancela y oculta
  Services/TrayIconService.cs
  Services/CursorIndicatorService.cs ← ventana flotante cerca del cursor (F6)
  Services/HotkeyService.cs         ← reset ConsumeNextKeyDown on stop
  Services/TextInjectorService.cs   ← dispatch UI thread + SendText/WM_PASTE
  Services/AutoStartService.cs      ← Registry HKCU Run (F6)
  Services/VcRedistChecker.cs       ← F7: chequea vcruntime140 en System32
  Services/NativeLibPathResolver.cs ← F7: copia whisper.dll a runtimes\win-x64 en single-file
  Services/SettingsService.cs       ← AppSettings + Changed event
  Services/TranscriberService.cs    ← F7: SmokeTestAsync (WhisperFactory.FromPath)
  Services/Log.cs                   ← logger estático (F2)
  ViewModels/SettingsViewModel.cs   ← MVVM para SettingsWindow (F6)
  Views/SettingsWindow.xaml(.cs)    ← UI de configuración (F6)
  Views/ModelDownloadWindow.xaml(.cs)← ventana modal de descarga (F4)
  Models/RecordingState.cs   ← enum { Idle, Recording, Processing, Error, NotReady }
  Resources/                 ← app.ico + tray-{idle,recording,processing,error}.ico
  app.manifest               ← asInvoker, sin UAC, PerMonitorV2 DPI
  VoiceTyper.csproj          ← net8.0-windows, WinExe, Nullable+ImplicitUsings
                               + RuntimeIdentifier/PublishSingleFile/SelfContained
                                 /IncludeNativeLibrariesForSelfExtract/EnableCompressionInSingleFile
                                 /EnableDynamicLoading
  Native/SendInputInterop.cs        ← SendText con KEYEVENTF_UNICODE
  Native/ClipboardInterop.cs        ← Backup/Set/Restore via WPF Clipboard
  Native/ClipboardInjector.cs       ← SendMessage WM_PASTE (fallback)
  Native/CursorInterop.cs           ← GetCursorPos/MonitorFromPoint (F6)
  Native/VirtualKey.cs              ← enum VK (Space, RMenu, F1–F12, etc.)
```

## Gotchas no obvios

- **Windows-only.** WPF no compila en Linux/macOS. No agregues targets multiplataforma.
- **Sin UAC.** `app.manifest` usa `asInvoker`. Installers deben escribir en `%LOCALAPPDATA%` / `HKCU` o elevar explícitamente.
- **Single-instance.** `App.OnStartup` toma un Mutex con nombre `Global\VoiceTyper_SingleInstance_v1`; la segunda instancia llama `Shutdown(0)`. Si renombrás la constante, no la dejes inconsistente.
- **Ciclo de vida.** `App.xaml` define `ShutdownMode="OnExplicitShutdown"` — la app sobrevive sin ventanas. El botón X de `MainWindow` cancela el cierre y oculta (`OnClosing` → `e.Cancel = true; Hide();`). La única salida limpia es el ítem "Salir" del menú del tray.
- **DI.** El host se arma en `App.OnStartup` con `Host.CreateDefaultBuilder().ConfigureServices(...)`. Los servicios nuevos se registran ahí como singletons. `TrayIconService` se resuelve **eager** (se llama `GetRequiredService` apenas se construye) para que el icono aparezca apenas arranca.
- **ContextMenu del tray.** Desde F6, se construye en **code-behind** dentro de `TrayIconService.BuildContextMenu(AppSettings, bool)`. No hay más `ResourceDictionary` en `App.xaml`. `TrayIconService` expone eventos (`OpenSettingsRequested`, `ModelChangeRequested`, `LanguageChangeRequested`, `AutoStartToggleRequested`, `PauseOnFullscreenToggleRequested`, `ExitRequested`, etc.) que `App.xaml.cs` suscribe y enruta a los servicios correspondientes (`SettingsService.Save`, `HotkeyService.ApplySettings`, `AutoStartService.SyncTo`, `ModelManagerService.EnsureModelAsync`). El estado se accede vía el campo `_host`, no vía `App.Current.MainWindow` (suele ser null).
- **Iconos del tray.** `ApplicationIcon` del exe es `Resources\app.ico`. Los cuatro `tray-*.ico` se mapean desde `RecordingState` en `TrayIconService.SetState`. Se cargan con `pack://application:,,,/Resources/{name}.ico` (mayúscula en `Resources`); `LoadIcon` tiene un fallback a minúscula — no lo borres sin verificar primero.
- **Threading de tray.** Las llamadas a `TaskbarIcon` deben hacerse en el thread de UI. `TrayIconService.SetState` / `ShowBalloon` hoy no son thread-safe; si la transcripción/hook corre en background, marshalizá con `Application.Current.Dispatcher.Invoke`.
- **Empaquetado (F7) hecho.** El .csproj ya tiene `RuntimeIdentifier`, `PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract`, `EnableCompressionInSingleFile` y `EnableDynamicLoading`. **No** le agregues `PublishReadyToRun` (R2R agrega ~10-20% al tamaño sin beneficio perceptible para esta app). Bundle final: ~71 MB sin modelo.
- **Single-file + Whisper: workaround obligatorio.** Whisper.net 1.9.0 usa `NativeLibrary.Load("whisper")` que en single-file **no encuentra los DLLs extraídos** porque (a) `AppContext.BaseDirectory` apunta al directorio del .exe (no al temp extraction), y (b) los DLLs se extraen a `%TEMP%\.net\VoiceTyper\<hash>\runtimes\win-x64\`, no a un path estándar. **Workaround aplicado**: `Services/NativeLibPathResolver.cs` corre al inicio de `OnStartup` (antes que cualquier servicio que use Whisper) y copia los 4 DLLs nativos (`whisper.dll`, `ggml-whisper.dll`, `ggml-base-whisper.dll`, `ggml-cpu-whisper.dll`) desde el temp extraction path a `<BaseDirectory>\runtimes\win-x64\`. Después, Whisper.net los encuentra por su path estándar. La copia es idempotente (sobrescribe). **No borrar** este resolver ni cambiar su ubicación — sin él, la app crashea al transcribir en producción.
- **Single-file: `Assembly.Location` vacío.** En publish single-file, `Assembly.GetExecutingAssembly().Location` devuelve string vacío (el assembly vive en memoria tras self-extract). Usá `AppContext.BaseDirectory` que apunta a la carpeta donde se extrajo el bundle. `EnvService.ResolveEnvPath` lo usa para encontrar `.env`. Si ves un servicio que llama a `Environment.CurrentDirectory` como fallback, también puede dar problemas — `AppContext.BaseDirectory` es la API correcta.
- **Install path es `%LOCALAPPDATA%\Programs\VoiceTyper\`.** NO uses `C:\Program Files\` ni otro path que requiera elevación (`app.manifest` está en `asInvoker`, sin UAC). `install.bat` y `uninstall.bat` deben ser operables por un usuario sin permisos de admin.
- **VC++ Redistributable: Whisper.net no lo trae embebido.** El native `whisper.dll` requiere `vcruntime140.dll` + `vcruntime140_1.dll` en `System32`. `VcRedistChecker.IsInstalled()` valida esto al inicio de la app y muestra un balloon no-bloqueante si falta. `install.bat` también chequea antes de publicar. **El `app.manifest asInvoker` no afecta la dependencia de VC++ Redist** — el binario necesita las DLLs del sistema, no manifest elevation.
- **GPU NVIDIA (CUDA):**
  - **API de Whisper.net 1.9.0**: la GPU se configura vía `WhisperFactoryOptions { UseGpu, GpuDevice }` pasado a `WhisperFactory.FromPath(path, options)`. El `WhisperProcessorBuilder` **NO** expone `.WithUseGpu()` ni `.WithCudaDevice()`. Si lo buscás, no lo vas a encontrar. Toda la config GPU va en `WhisperFactoryOptions`.
  - **Whisper.net auto-selecciona el runtime**: si tenés `Whisper.net.Runtime.Cuda` instalado y el driver NVIDIA provee CUDA 13+, se usa. Si no, cae transparente a CPU. La selección la hace `Whisper.net.LibraryLoader.CudaHelper.IsCudaAvailable()` internamente. **No** trates de forzar la carga del runtime manualmente.
  - **Single-file + CUDA workaround**: `Whisper.net.Runtime.Cuda.Windows` pone los DLLs nativos (`ggml-cuda-whisper.dll`, etc.) en `runtimes/cuda/win-x64/`, NO en `runtimes/win-x64/`. El `NativeLibPathResolver` original solo copiaba de `runtimes/win-x64/`, por lo que **los DLLs de CUDA no se copiaban** y la app crasheaba al transcribir en GPU. **Solución**: el resolver ahora detecta subcarpetas de `runtimes/` que NO son platform-specific (excluye `win-x64`, `win-x86`, `win-arm64`, `linux-*`, etc.) y copia sus DLLs a `<BaseDir>\runtimes\<backend>\win-x64\`, preservando la estructura que Whisper.net espera para su `NativeLibraryLoader`. Sin esto, GPU init falla con "Native Library not found". Si ves este error, revisá que `<BaseDir>\runtimes\cuda\win-x64\ggml-cuda-whisper.dll` exista. **Importante**: NO mezclar DLLs CPU y CUDA en el mismo `runtimes\win-x64\` — Whisper.net elige el backend según el path del DLL, y si los CUDA whisper.dll están en `win-x64\`, se cargan aunque `UseGpu=false` (rompe el fallback a CPU en sistemas sin driver).
  - **CudaDetector propio vs. Whisper.net's CudaHelper**: `CudaDetector` (nuestro) hace P/Invoke a `nvcuda.dll` para chequear disponibilidad y contar devices. Es **independiente** de Whisper.net y no carga ningún runtime CUDA. Lo usamos para (a) decidir si ofrecer la opción GPU en el Settings y (b) para el balloon de auto-sugerencia. Cuando finalmente se construye el factory con `UseGpu = true`, Whisper.net hace su propia carga y validación; si falla, caemos a CPU con log warn.
  - **Fallback policy**: NUNCA crashear por GPU. `TranscriberService.TryCreateFactory` envuelve la creación del factory GPU en try/catch. Si falla (driver desactualizado, GPU no soportada, error de carga de DLL), log warn + reconstruir con `UseGpu = false`. El usuario sigue pudiendo transcribir, solo que más lento.
  - **Cache invalidation al cambiar GPU**: `TranscriberService` trackea `_loadedGpu` (bool?) además de `_loadedModel` y `_loadedLanguage`. Si el usuario toggle `GpuEnabled` en Settings, el processor se reconstruye con el modo correcto.
  - **Tamaño del bundle**: ~270 MB con CUDA vs ~71 MB sin él. Decisión consciente: bundle único (no split). El incremento viene de `ggml-cuda-whisper.dll` (~150 MB comprimido) más los demás DLLs CUDA. Los runtimes CUDA del sistema (cudart, cublas) los provee el driver NVIDIA, no se embeben.
  - **Auto-suggestion**: `GpuSuggestionShown` (bool en AppSettings) previene spamear al usuario. Se setea en `true` después de mostrar el balloon por primera vez. Si el usuario quiere volver a ver la sugerencia, debe resetear manualmente en `settings.json`.
  - **`nvcuda.dll` vs `cuda.dll`**: usar `nvcuda.dll` (driver user-mode de NVIDIA). `cuda.dll` es el runtime toolkit (no siempre presente). `nvcuda.dll` viene con el driver GeForce/Quadro/RTX y es lo que usan los P/Invoke estándar.
  - **`CudaDetector` requiere `cuInit(0)` antes de cualquier llamada del Driver API**: la CUDA Driver API exige inicializar el driver con `cuInit(0)` antes de `cuDeviceGetCount` / `cuDeviceGetName` / etc. Sin esto, todas las llamadas retornan `CUDA_ERROR_NOT_INITIALIZED` (status 700) y el detector reporta 0 devices aunque haya GPU. `CudaDetector.EnsureInitialized()` cachea el resultado en `_initStatus` (campo de instancia) para no llamarlo más de una vez. **Si reescribís el detector, no te olvides del cuInit**, es el bug más común y no se manifiesta en compile-time.
  - **Firma correcta de `cuDeviceGetName`**: `CUresult cuDeviceGetName(char *name, int len, CUdevice dev)`. El P/Invoke debe declarar los **3** parámetros en orden: `(byte[] name, int len, int dev)`. Pasar solo 2 parámetros hace que el marshaller confunda `len` con `dev` y retorne `CUDA_ERROR_INVALID_VALUE` (status 101) — el detector va a reportar count correcto pero nombres vacíos. Whisper.net internamente lo hace bien, pero nuestro detector necesita los 3 parámetros explícitos.
- **Smoke test en single-file: ya funciona.** Flag `--smoke-test` instancia `WhisperFactory.FromPath()` y sale con exit code 0 si carga OK, 1 si falla. **No** lo expongas al usuario; es solo para CI / verificación manual post-publish. En dev (`dotnet run -- --smoke-test`) también funciona. Verificá que tras `dotnet publish`, el `.exe` del publish dir corra el smoke test con exit 0 antes de taggear un release.
- **SendInput debe correr en el UI thread.** `SendInput` desde threadpool hace que apps modernas (WinUI, XAML, algunos Electron) ignoren el input. `TextInjectorService` dispatchea al `Application.Current.Dispatcher` antes de cualquier `SendInput`/`SendMessage`.
- **Text injection: Unicode SendInput, no WM_PASTE.** Controles WinUI/XAML (Notepad moderno, `RichEditBox` UWP, etc.) tienen paste protection que ignora `WM_PASTE` sintetizado. `SendInputInterop.SendText` con `KEYEVENTF_UNICODE` bypassea eso enviando `WM_UNICHAR` por char. `WM_PASTE` queda solo como fallback en `ClipboardInjector.SendPaste`.
- **Hook consume flag: reset en `OnHookKeyUp`.** `ConsumeNextKeyDown` se setea en `true` cada 20ms por `HotkeyService.ConsumeLoopAsync` mientras `IsRecording`. Si no se resetea al terminar la grabación, el flag residual se come el primer keydown sintetizado por el `SendInput` post-transcripción y la inyección se rompe silenciosamente. Ver `HotkeyService.OnHookKeyUp:93`.
- **Cursor indicator: no roba foco.** La ventana de `CursorIndicatorService` debe tener `ShowActivated=false` e `IsHitTestVisible=false`. Sin esto, cada vez que se hace `Show()` el caret del control destino (Notepad, Chrome, etc.) se mueve fuera del campo y la inyección posterior de `SendInput` se va al vacío. También lleva `Topmost=true` para no quedar tapada por overlays de otras apps. **Tamaño 16×16 DIPs** (reducido desde 28×28 original). **Animación: solo pulse opacity** — Recording a 0.9 s, Processing a 0.6 s. La rotación fue removida porque el `RenderTransform` sobre la Ellipse hacía que el bounding box rotado se extendiera más allá del frame de la Window y WPF lo clipaba (forma de Pac-Man). Si querés diferenciar visualmente Processing de Recording, cambiá el color o la frecuencia del pulse, no agregues rotación.
- **AutoStart vía Registry.** `AutoStartService` escribe `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\VoiceTyper` con el path absoluto del exe + flag `--autostart`. No requiere elevación (HKCU). Al iniciar la app, `SyncTo(settings.AutoStart)` reconcilia el estado del registry con la setting (corrige drift si el usuario editó el registry a mano).
- **Re-enganche de hotkey durante processing descarta el nuevo dictado.** `RecordingOrchestrator` serializa el ciclo recording→processing con un `SemaphoreSlim(1, 1)`. Si el usuario vuelve a apretar `AltGr+Space` mientras la transcripción anterior está corriendo (indicador ámbar, 2-3 s con `small` en CPU), `OnRecordingStarted` intenta `Wait(0)` (no bloqueante) y, si el lock está tomado, loguea warn + muestra balloon `"Esperá a que termine la transcripción anterior"` + `return` sin tocar el audio. El indicador sigue ámbar hasta que la transcripción anterior termina. Esto evita que dos `OnRecordingStoppedAsync` corran en paralelo y disparen `InjectAsync` dos veces, que era el bug que duplicaba el texto en el cursor. Ver `RecordingOrchestrator:OnRecordingStarted:42` y `:OnRecordingStoppedAsync:128` (finally con Release). **No cambiar a `CancellationTokenSource`**: hay una ventana entre "Whisper terminó, texto listo" e "InjectAsync ejecuta SendInput" donde la cancelación llega tarde y la duplicación ya ocurrió. El semáforo es atómico. **No** envolver el `Wait(0)` con retry/timeout — si el usuario insiste durante processing, está bien que vea el balloon cada vez.

## Gitignore: trampa con `models/`

`src/.gitignore` ya filtra **sólo los binarios** del modelo Whisper
(`models/*.bin`, `*.ggml`, `ggml-*.bin`, `whisper-*.bin`).
**No reintroducir** la regla `models/` desnuda ni `Models/` global: en Windows
el FS es case-insensitive e ignora también la carpeta `Models/` de C# donde
vive `RecordingState.cs`. Si `Models/RecordingState.cs` desaparece del
`git status`, esa es la causa.

## Privacidad

Cero telemetría. El único tráfico de red previsto es la descarga del modelo
Whisper desde HuggingFace en F4. **No agregues** analytics, auto-update
silencioso, ni llamadas a servicios externos.
