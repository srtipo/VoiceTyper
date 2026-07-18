# VoiceTyper

> Dictado por voz global para Windows — mantené la hotkey, hablá, soltá. El texto se inserta donde esté el cursor.

App de Windows que escucha tu voz, la transcribe con Whisper (local, sin internet) y pega el texto donde estés escribiendo. Funciona en cualquier app: Notepad, Chrome, VSCode, Slack, Word, etc.

## Uso

1. Iniciá VoiceTyper (queda en el system tray, **sin ventana visible**).
2. Click derecho en el ícono del tray → **Configuración…** para elegir:
   - **Modelo de Whisper** (Tiny / Base / Small / Medium). Small es el default, balance entre velocidad y precisión.
   - **Idioma** (es / en / pt / fr / auto).
   - **Hotkey** (modificador + tecla disparadora). Default: `AltGr + Space`.
   - **Micrófono** (si tenés más de uno).
   - **Iniciar con Windows** (autoarranque vía Registry HKCU).
   - **Pausar en pantalla completa** (default ON).
3. Mantené la hotkey y hablá. Al soltar, el texto aparece donde está el cursor.
4. El indicador flotante cerca del cursor cambia de color: rojo mientras grabás, ámbar mientras Whisper procesa, desaparece al terminar.

## Requisitos

- Windows 10 21H2 o superior (64-bit)
- .NET 8 SDK (sólo para compilar desde el repo — el `.exe` final es self-contained)
- Visual C++ 2015-2022 Redistributable (verificamos al inicio, ver [Troubleshooting](#troubleshooting))
- GPU NVIDIA opcional (acelera la transcripción 5-10x, requiere driver reciente)

## Instalación

### Desde el repo (desarrolladores / testers)

1. Cloná el repo (o descargá el zip):
   ```
   git clone https://github.com/<user>/VoiceTyper.git
   ```
2. Asegurate de tener **Visual C++ 2015-2022 Redistributable** instalado:
   <https://aka.ms/vc14/vc_redist.x64.exe>
3. Asegurate de tener el **.NET 8 SDK** instalado:
   <https://dotnet.microsoft.com/download/dotnet/8.0>
4. Hacé doble-click en `install.bat`.
   - El script compila la app en modo Release (puede tardar 1-2 minutos la primera vez).
   - Copia el ejecutable a `%LOCALAPPDATA%\Programs\VoiceTyper\`.
   - Crea un acceso directo en el Menú Inicio.
   - Crea la carpeta `%LOCALAPPDATA%\VoiceTyper\models\` para los modelos.
   - Lanza la app al final.
5. En el primer arranque se descarga el modelo Whisper `small` (~460 MB) desde HuggingFace. El progreso se muestra en una ventana con botón **Cancelar**.

### Ubicación de archivos

| Qué | Dónde |
|---|---|
| Ejecutable | `%LOCALAPPDATA%\Programs\VoiceTyper\VoiceTyper.exe` |
| Modelos Whisper | `%LOCALAPPDATA%\VoiceTyper\models\ggml-*.bin` |
| Settings | `%LOCALAPPDATA%\VoiceTyper\settings.json` |
| Logs | `%LOCALAPPDATA%\VoiceTyper\logs\voicetyper.log` |
| Autoarranque | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\VoiceTyper` |
| Acceso directo | `%LOCALAPPDATA%\Microsoft\Windows\Start Menu\Programs\VoiceTyper.lnk` |

## Desinstalar

Hacé doble-click en `uninstall.bat`. El script:

1. Cierra la app si está corriendo.
2. Borra la entrada de autoarranque del Registry.
3. Borra el acceso directo del Menú Inicio.
4. Borra la carpeta de instalación.
5. Te pregunta si querés borrar también los datos de usuario (settings, modelos descargados y logs).

## Troubleshooting

| Síntoma | Causa probable | Solución |
|---|---|---|
| Al iniciar aparece un balloon "Falta Visual C++ Redistributable" | VC++ 2015-2022 no instalado | Bajá e instalá desde <https://aka.ms/vc14/vc_redist.x64.exe> y reiniciá VoiceTyper |
| La hotkey no funciona cuando estoy en un juego / video fullscreen | Feature, no bug — la hotkey se pausa automáticamente para no comerse teclas de juegos | Desmarcá **"Pausar en pantalla completa"** en Configuración si querés que funcione igual |
| La transcripción vuelve vacía o con `[BLANK_AUDIO]` | Audio demasiado bajo, mucho ruido ambiente, o no se detectó voz | Acercate al micrófono, hablá más fuerte, o cambiá a un modelo más grande (Medium) en Configuración |
| `install.bat` falla con "dotnet no está en PATH" | .NET 8 SDK no instalado | Instalalo desde <https://dotnet.microsoft.com/download/dotnet/8.0> y volvé a correr el script |
| VoiceTyper no arranca con Windows (autoarranque) | El exe se movió de lugar (el path absoluto quedó desactualizado en el registry) | Abrí Configuración, desmarcá y volvé a marcar **"Iniciar con Windows"** para reescribir el path |
| `settings.json` corrupto tras un crash | Apagón durante escritura, etc. | Borrá `%LOCALAPPDATA%\VoiceTyper\settings.json` y reiniciá la app (vuelve a defaults) |
| El indicador flotante no aparece cerca del cursor | El foco está en una app que no recibe overlays correctamente (raro en Win10+, común en VMs sin aceleración gráfica) | No es un bug — el indicador es puramente visual. La grabación y la transcripción funcionan igual sin él |
| `VoiceTyper.exe --smoke-test` devuelve exit code 1 | Alguna native lib de Whisper no se cargó (VC++ faltante, o publish mal hecho) | Reinstala VC++ Redist y reintentá. Si persiste, recompilá con `dotnet publish ... -c Release` |
| Transcripción sigue lenta con GPU activada | El driver NVIDIA es muy viejo o no soporta CUDA | Actualizá desde https://www.nvidia.com/drivers. La app cae automáticamente a CPU. |
| `VoiceTyper.exe` es más grande que antes (~270 MB) | Ahora incluye el runtime CUDA opcional | Los DLLs de CUDA no se cargan si la GPU no está activada en Configuración |

## Privacidad

- Cero telemetría. El único uso de red es la descarga inicial del modelo Whisper desde HuggingFace.
- El audio se descarta tras transcribir. No se guarda nada en disco.
- Sin analytics, sin auto-update silencioso, sin llamadas a servicios externos.

## Configuración

VoiceTyper lee configuración con esta precedencia (mayor a menor):

1. **Variable de entorno del sistema** — para CI / override global
2. **Archivo `.env`** junto a `VoiceTyper.exe` — overrides por usuario
3. **`settings.json`** en `%LOCALAPPDATA%\VoiceTyper\` — preferencias persistentes editables desde la UI
4. **Defaults del código** — último recurso, nunca contienen secrets

### Override rápido con `.env`

1. Copiá `.env.example` a `.env` (en la misma carpeta que `VoiceTyper.exe`)
2. Editá los valores que quieras
3. Reiniciá la app

El archivo `.env` puede contener posibles secrets (API keys) y **nunca debe commitearse al repo**. Está excluido vía `.gitignore`.

### Variables disponibles

Ver [`.env.example`](./.env.example) para la lista completa y documentada.

| Variable | Default | Propósito |
|---|---|---|
| `VT_LANGUAGE` | `es` | Idioma de transcripción |
| `VT_MODEL` | `small` | Modelo Whisper default |
| `VT_MODEL_DIR` | `%LOCALAPPDATA%\VoiceTyper\models` | Carpeta de modelos |
| `VT_LOG_LEVEL` | `Information` | Nivel de log |
| `VT_LOG_DIR` | `%LOCALAPPDATA%\VoiceTyper\logs` | Carpeta de logs |
| `VT_GPU_ENABLED` | `false` | Activar aceleración GPU NVIDIA (experimental) |
| `VT_GPU_DEVICE` | `0` | Índice de GPU NVIDIA a usar |
| `VT_OPENAI_API_KEY` | _(vacío)_ | Reservado para futuro |
| `VT_GROQ_API_KEY` | _(vacío)_ | Reservado para futuro |

## Cómo funciona

Audio del micrófono → `SendInput` consume Space para no llegar a la app destino → WAV 16 kHz mono en memoria → Whisper local (CPU o GPU NVIDIA opcional) → texto plano → `SendInput` con `KEYEVENTF_UNICODE` (bypassea paste protection de WinUI/XAML) → aparece donde está el cursor. Más detalle en [`spec.md`](./spec.md).

## Roadmap

Backlog post-MVP. **No hay fechas ni compromiso** — esto es lo que se podría hacer después del MVP:

- Streaming de Whisper (transcripción mientras hablás, sin soltar la tecla)
- Hotkey con spoken punctuation ("coma", "punto", "nueva línea")
- Múltiples perfiles de hotkey por app
- Modo "always listening" con wake word
- Temas del tray icon (dark / light)
- ✅ **Hecho en F8**: Soporte para GPU NVIDIA (`Whisper.net.Runtime.Cuda`)

## Para contribuidores

Este es un **proyecto público**. Reglas obligatorias:

- ❌ **Nunca** commitear secrets, API keys, tokens ni paths personales
- ✅ Toda config opcional va en `.env.example` (que sí se commitea)
- ✅ El `.env` real está en `.gitignore` desde el día 1
- ✅ Si una funcionalidad necesita un secret, leerlo siempre vía `EnvService` (nunca hardcoded)
- ✅ Si agregás una nueva variable, documentala en `.env.example` Y en `spec.md` sección 11

### Comandos útiles

```sh
# Compilar
dotnet build VoiceTyper.sln

# Ejecutar en modo debug
dotnet run --project src/VoiceTyper

# Publicar single-file self-contained
dotnet publish src/VoiceTyper -c Release -r win-x64 --self-contained true

# Smoke test de native libs (sale con exit 0 si OK, 1 si FAIL)
"src\VoiceTyper\bin\Release\net8.0-windows\win-x64\publish\VoiceTyper.exe" --smoke-test
```

### Estructura

Ver [`AGENTS.md`](./AGENTS.md) sección "Layout" y [`phases.md`](./phases.md) para el estado por fase.
