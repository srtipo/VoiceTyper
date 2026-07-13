# VoiceTyper — Especificaciones

## 1. Visión
Aplicación de Windows que permite dictar por voz y pegar el texto transcrito
en cualquier campo de texto activo, mediante un atajo global push-to-talk.

## 2. Objetivos
- Dictar texto por voz en cualquier app de Windows sin cambiar de contexto.
- Transcripción 100% local, privada, sin conexión a internet.
- Bajo consumo de recursos cuando está inactiva.
- Cero costo operativo (sin APIs de pago).

## 3. No-objetivos
- No es un asistente conversacional (no responde, solo transcribe).
- No soporta comandos de voz (puntuación por spoken punctuation queda fuera del MVP).
- No graba audio persistido: el audio se descarta tras transcribir.
- No soporta múltiples idiomas simultáneos en una sesión.

## 4. Usuarios objetivo
- Desarrolladores, redactores, estudiantes y profesionales que tipean mucho.
- Usuarios con teclado en español (latinoamericano o España).

## 5. Requisitos funcionales

### 5.1 Atajo global
- **Default:** `AltGr + Space` (mantener para grabar, soltar para transcribir).
- Configurable desde SettingsWindow (modificador + tecla).
- Diferenciación Left/Right: AltGr es específicamente `Right Alt` (`VK_RMENU`).
- Mientras la grabación está activa, el hook **consume** la tecla Space
  para que no llegue a la app destino.
- Auto-pausa del hook si la app en foco está en fullscreen exclusivo
  (juegos DirectX). Configurable (default ON).

### 5.2 Captura de audio
- Micrófono default del sistema (seleccionable en Settings).
- Formato: 16 kHz, mono, PCM 16-bit (requisito de Whisper).
- Captura vía `NAudio.Wave.WaveInEvent` a `MemoryStream` con header WAV.
- Cancelación de ruido: solo silenciar buffer si RMS < umbral (sin eco feedback).
- Duración máxima: 5 minutos (auto-stop con notificación).

### 5.3 Transcripción
- Motor: **Whisper** local vía **Whisper.net** (bindings de whisper.cpp).
- Modelo default: `ggml-small.bin` (~460 MB), descargable bajo demanda.
- Idioma default: `es` (español). Configurable: `en`, `pt`, `fr`, `auto`.
- Sin streaming: se transcribe el audio completo al soltar la tecla.
- Tiempo objetivo: < 3s para clips de 10s en CPU moderna.

### 5.4 Inserción de texto
- Método: clipboard + `SendInput` simulando `Ctrl+V`.
- Flujo:
  1. Guardar contenido actual del clipboard.
  2. Setear clipboard al texto transcrito.
  3. Esperar 50ms (latencia de clipboard).
  4. `SendInput` Ctrl down → V down → V up → Ctrl up.
  5. Esperar 200ms.
  6. Restaurar clipboard original (best-effort, ignorar si la app destino lo leyó).
- Si falla el set de clipboard (otra app lo tiene tomado): reintentar 3 veces
  con backoff de 100ms; si falla, notificar al usuario vía tray balloon.

### 5.5 Persistencia
- Configuración: `%AppData%\VoiceTyper\settings.json` (JSON, UTF-8).
- Modelos Whisper: `%AppData%\VoiceTyper\models\`.
- Logs: `%AppData%\VoiceTyper\logs\voicetyper.log` (rolling, máx 5 MB).

### 5.6 System tray
- Icono cambia según estado:
  - ⚪ Idle (gris)
  - 🔴 Grabando (rojo)
  - 🟡 Procesando (amarillo)
  - ⚠️ Error (naranja)
- Menú contextual:
  - Estado actual (deshabilitado, solo info)
  - Configuración… (abre SettingsWindow)
  - Modelo ▸ small / base / medium
  - Idioma ▸ es / en / pt / fr / auto
  - ☐ Iniciar con Windows
  - ☐ Pausar en pantalla completa
  - ─────
  - Acerca de…
  - Salir

### 5.7 Auto-start con Windows
- Vía Registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
  con valor `"VoiceTyper" = "<exe-path>"`.
- Configurable desde menú del tray. Default: OFF (opt-in).
- No requiere elevación (HKCU).

## 6. Requisitos no funcionales

### 6.1 Rendimiento
- CPU en idle: < 1% (modelo Whisper precargado pero no ejecutando).
- RAM en idle: < 200 MB.
- RAM durante transcripción: < 600 MB (modelo small).
- Latencia end-to-end (soltar tecla → texto pegado): < 4s para clips de 10s.

### 6.2 Privacidad
- Cero tráfico de red en operación normal.
- El único uso de red es descarga inicial del modelo Whisper desde HuggingFace
  (o GitHub release, a definir). Sin telemetría, sin analytics.

### 6.3 Compatibilidad
- Windows 10 21H2+ y Windows 11.
- .NET 8 (LTS).
- Requisito externo: Visual C++ 2015-2022 Redistributable.
  Verificar al inicio y mostrar mensaje si falta.

### 6.4 Confiabilidad
- Single-instance vía `Mutex` global con nombre `Global\VoiceTyper_SingleInstance`.
- Si falla la transcripción: log + tray balloon + volver a idle sin crashear.
- Si falla el hotkey hook: log + notificación + reintentar cada 5s.

### 6.5 Empaquetado
- `dotnet publish -c Release -r win-x64 --self-contained true`
- Tamaño objetivo: < 80 MB (sin modelo).
- Script `install.bat` que:
  1. Verifica VC++ Redist.
  2. Crea carpetas en `%AppData%\VoiceTyper\`.
  3. Copia el exe a `Program Files\VoiceTyper\`.
  4. Crea acceso directo en menú inicio (opcional).
- Script `uninstall.bat` que limpia todo.

## 7. Estructura del proyecto

```
C:\Users\victor\proyectos\VoiceTyper\
├── VoiceTyper.sln
├── README.md
├── spec.md                          # este archivo
├── phases.md
├── install.bat
├── uninstall.bat
└── src\
    └── VoiceTyper\
        ├── VoiceTyper.csproj
        ├── App.xaml / App.xaml.cs
        ├── MainWindow.xaml(.cs)
        ├── Views\SettingsWindow.xaml(.cs)
        ├── Services\
        │   ├── AudioRecorderService.cs
        │   ├── TranscriberService.cs
        │   ├── ModelManagerService.cs
        │   ├── HotkeyService.cs
        │   ├── TextInjectorService.cs
        │   ├── TrayIconService.cs
        │   ├── AutoStartService.cs
        │   ├── SettingsService.cs
        │   └── LoggerService.cs
        ├── Native\
        │   ├── LowLevelKeyboardHook.cs
        │   ├── SendInputInterop.cs
        │   └── ClipboardInterop.cs
        ├── Models\
        │   ├── AppSettings.cs
        │   ├── RecordingState.cs
        │   └── WhisperModel.cs
        └── Resources\
            ├── tray-idle.ico
            ├── tray-recording.ico
            ├── tray-processing.ico
            └── tray-error.ico
```

## 8. Plan de implementación

| Fase | Descripción | Esfuerzo |
|---|---|---|
| 1 | Esqueleto: solución, proyecto, tray básico, single-instance | 1-2h |
| 2 | Hook de teclado global (push-to-talk, consume Space) | 1h |
| 3 | Captura de audio con NAudio | 1-2h |
| 4 | Integración Whisper.net + descarga de modelo | 1-2h |
| 5 | Inyección de texto (clipboard + SendInput) | 1h |
| 6 | Integración + settings + pulido UX | 2h |
| 7 | Empaquetado (publish self-contained, scripts) | 1h |

**Total estimado: 8-12h**

## 9. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| AltGr+Space activado en alguna app (ej: launcher) | Hook consume la combinación; documentar |
| Apps fullscreen bloquean el hook | Auto-pausa con detección de exclusive mode |
| Clipboard tomado por otra app | Retry con backoff + notificación |
| Modelo 460 MB tarda en bajar primera vez | Barra de progreso, resume de descarga |
| VC++ Redist faltante | Verificación al inicio + link de descarga |
| Whisper se carga lento cada vez | Lazy load + cache en memoria tras primer uso |
| Restaurar clipboard borra texto que la app destino aún no leyó | Delay de 200ms + best-effort |

## 10. Criterios de aceptación (MVP)

- [ ] App arranca y queda en system tray sin ventana visible.
- [ ] Mantener AltGr+Space graba; soltar transcribe y pega el texto.
- [ ] Funciona en: Notepad, Chrome (campo de búsqueda, Gmail), VSCode, Slack, Word.
- [ ] Cambiar de app durante la grabación no interrumpe.
- [ ] Menú del tray permite cambiar hotkey, modelo, idioma, auto-start.
- [ ] Auto-start con Windows funciona tras activarlo.
- [ ] Sin tráfico de red tras descarga inicial del modelo.
- [ ] Apagado limpio vía menú "Salir" (sin procesos zombi).

## 11. Configuración y secretos

Este es un proyecto público: cualquier secreto o path personal en el código
queda expuesto para siempre. Toda la configuración opcional se canaliza
vía `.env` y está excluida del repo.

### 11.1 Precedencia de configuración (mayor a menor)
1. **Variable de entorno del sistema** — para CI, Docker o override global.
2. **Archivo `.env`** junto al ejecutable (`VoiceTyper.exe/.env`) — overrides por usuario.
3. **`settings.json`** en `%LOCALAPPDATA%\VoiceTyper\` — preferencias persistentes editables desde la UI.
4. **Defaults hardcodeados** en el código — último recurso, nunca contienen secrets.

### 11.2 Reglas para el proyecto público
- **Nunca** commitear secrets, API keys, tokens, ni paths personales.
- Toda configuración opcional se documenta en `.env.example` en la raíz del repo.
- El archivo `.env` real está en `.gitignore` desde el inicio.
- `EnvService` carga `.env` solo en runtime, nunca persiste los valores.
- `LoggerService` filtra automáticamente valores de vars que terminan en `_KEY`, `_SECRET`, `_TOKEN`, `_PASSWORD`.
- `EnvService` no loguea los valores, solo registra que las vars fueron leídas.

### 11.3 Variables de entorno reconocidas

| Variable | Default | Propósito |
|---|---|---|
| `VT_LANGUAGE` | `es` | Idioma de transcripción (`es`, `en`, `pt`, `fr`, `auto`) |
| `VT_MODEL` | `small` | Modelo Whisper default (`tiny`, `base`, `small`, `medium`) |
| `VT_MODEL_DIR` | `%LOCALAPPDATA%\VoiceTyper\models` | Carpeta de modelos |
| `VT_LOG_LEVEL` | `Information` | Nivel mínimo de log |
| `VT_LOG_DIR` | `%LOCALAPPDATA%\VoiceTyper\logs` | Carpeta de logs |
| `VT_WORK_DIR` | _(vacío)_ | Solo para debug |
| `VT_OPENAI_API_KEY` | _(vacío)_ | Reservado para futuro fallback cloud |
| `VT_GROQ_API_KEY` | _(vacío)_ | Reservado para futuro fallback cloud |

### 11.4 Implementación
- `src\VoiceTyper\Services\EnvService.cs` — clase estática `Env` con `Load()`, `Get(key)`, y properties tipadas.
- Loader minimalista (~80 líneas), sin dependencias externas.
- Llamar `Env.Load()` una sola vez en `App.OnStartup` antes de construir el `Host`.
