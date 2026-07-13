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
