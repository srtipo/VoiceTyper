# VoiceTyper

App de Windows que escucha tu voz, la transcribe con Whisper (local, sin internet) y pega el texto donde estés escribiendo. Funciona en cualquier app: Notepad, Chrome, VSCode, Slack, Word, etc.

## Quick start
- Mantener `AltGr + Space` y hablar. Al soltar, el texto se inserta automáticamente donde está el cursor.

## Requisitos
- Windows 10 21H2 o superior
- .NET 8 (ya viene en el paquete self-contained)
- Visual C++ 2015-2022 Redistributable (verificamos al inicio)

## Estado del proyecto
Ver [`phases.md`](./phases.md) para el plan de implementación por fases.
Ver [`spec.md`](./spec.md) para las especificaciones detalladas.

## MVP — Criterios de aceptación
- [ ] App arranca y queda en system tray sin ventana visible
- [ ] Mantener AltGr+Space graba; soltar transcribe y pega el texto
- [ ] Funciona en: Notepad, Chrome, VSCode, Slack, Word
- [ ] Sin tráfico de red tras descarga inicial del modelo

## Privacidad
- Cero telemetría. El único uso de red es la descarga inicial del modelo Whisper (~460 MB) desde HuggingFace.
- El audio se descarta tras transcribir. No se guarda nada.

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
| `VT_OPENAI_API_KEY` | _(vacío)_ | Reservado para futuro |
| `VT_GROQ_API_KEY` | _(vacío)_ | Reservado para futuro |

## Para contribuidores

Este es un **proyecto público**. Reglas obligatorias:

- ❌ **Nunca** commitear secrets, API keys, tokens ni paths personales
- ✅ Toda config opcional va en `.env.example` (que sí se commitea)
- ✅ El `.env` real está en `.gitignore` desde el día 1
- ✅ Si una funcionalidad necesita un secret, leerlo siempre vía `EnvService` (nunca hardcoded)
- ✅ Si agregás una nueva variable, documentala en `.env.example` Y en `spec.md` sección 11
