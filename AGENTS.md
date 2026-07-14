# AGENTS.md — VoiceTyper

App WPF de Windows (.NET 8, single-instance, sin UAC) que escucha voz con
`AltGr+Space` y la transcribe con Whisper local para pegarla donde esté el
cursor. Funciona en cualquier app: Notepad, Chrome, VSCode, Slack, Word.

- Especificación funcional: `spec.md`.
- Plan por fases y estado de implementación: `phases.md` (fuente de verdad de "qué existe").
- Stack: WPF + `Hardcodet.NotifyIcon.Wpf` + `Microsoft.Extensions.Hosting` (DI) + `CommunityToolkit.Mvvm`.

## Estado actual
**Fases 0–4 completadas** (bootstrap, esqueleto WPF + tray, hook global
AltGr+Space, captura NAudio, Whisper.net con descarga del modelo). Fases 5–7
pendientes (inyección de texto, settings window, empaquetado). `MainWindow.xaml`
es placeholder. Hoy: tray + hotkey + grabar audio + transcribir + log del
texto (todavía no inyecta en la app destino).

## Comandos
Todo desde la raíz del repo. No hay script de build propio.

- Compilar: `dotnet build VoiceTyper.sln`
- Ejecutar (sólo Windows): `dotnet run --project src/VoiceTyper`
- Publicar (cuando exista F7): `dotnet publish src/VoiceTyper -c Release -r win-x64 --self-contained true`
- No hay proyecto de tests. **No asumas xUnit/NUnit/MSTest** hasta que se cree.

## Layout
```
VoiceTyper.sln
src/VoiceTyper/
  App.xaml(.cs)              ← entrypoint; Mutex + DI host + tray
  MainWindow.xaml(.cs)       ← placeholder; OnClosing cancela y oculta
  Services/TrayIconService.cs
  Models/RecordingState.cs   ← enum { Idle, Recording, Processing, Error }
  Resources/                 ← app.ico + tray-{idle,recording,processing,error}.ico
  app.manifest               ← asInvoker, sin UAC, PerMonitorV2 DPI
  VoiceTyper.csproj          ← net8.0-windows, WinExe, Nullable+ImplicitUsings
```

No existen aún (F5–F7): `Services/TextInjectorService.cs`,
`Native/{SendInputInterop,ClipboardInterop}.cs`,
`Views/SettingsWindow.xaml(.cs)`, `install.bat` / `uninstall.bat`. Cuando los
creas, seguí las convenciones descritas abajo y el desglose de `phases.md`
para esa fase.

## Gotchas no obvios

- **Windows-only.** WPF no compila en Linux/macOS. No agregues targets multiplataforma.
- **Sin UAC.** `app.manifest` usa `asInvoker`. Installers deben escribir en `%LOCALAPPDATA%` / `HKCU` o elevar explícitamente.
- **Single-instance.** `App.OnStartup` toma un Mutex con nombre `Global\VoiceTyper_SingleInstance_v1`; la segunda instancia llama `Shutdown(0)`. Si renombrás la constante, no la dejes inconsistente.
- **Ciclo de vida.** `App.xaml` define `ShutdownMode="OnExplicitShutdown"` — la app sobrevive sin ventanas. El botón X de `MainWindow` cancela el cierre y oculta (`OnClosing` → `e.Cancel = true; Hide();`). La única salida limpia es el ítem "Salir" del menú del tray.
- **DI.** El host se arma en `App.OnStartup` con `Host.CreateDefaultBuilder().ConfigureServices(...)`. Los servicios nuevos se registran ahí como singletons. `TrayIconService` se resuelve **eager** (se llama `GetRequiredService` apenas se construye) para que el icono aparezca apenas arranca.
- **ContextMenu del tray.** Vive como `ResourceDictionary` en `App.xaml` bajo la clave `TrayContextMenu` (`x:Shared="false"`). `TrayIconService` lo obtiene con `Application.Current.FindResource("TrayContextMenu")`. Los handlers de los `MenuItem` (`OnOpenSettings`, `OnAbout`, `OnExit`) están en `App.xaml.cs` y acceden al host vía el campo `_host`; si necesitás estado del host, pasalo por ahí, no por `App.Current.MainWindow` (suele ser null).
- **Iconos del tray.** `ApplicationIcon` del exe es `Resources\app.ico`. Los cuatro `tray-*.ico` se mapean desde `RecordingState` en `TrayIconService.SetState`. Se cargan con `pack://application:,,,/Resources/{name}.ico` (mayúscula en `Resources`); `LoadIcon` tiene un fallback a minúscula — no lo borres sin verificar primero.
- **Threading de tray.** Las llamadas a `TaskbarIcon` deben hacerse en el thread de UI. `TrayIconService.SetState` / `ShowBalloon` hoy no son thread-safe; si la transcripción/hook corre en background, marshalizá con `Application.Current.Dispatcher.Invoke`.
- **Empaquetado pendiente (F7).** El .csproj todavía **no** tiene `RuntimeIdentifier`, `PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract` ni `EnableCompressionInSingleFile`. No los agregues antes de tiempo.

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
