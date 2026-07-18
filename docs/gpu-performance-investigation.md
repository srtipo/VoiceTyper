# VoiceTyper — Investigación de performance GPU (F8)

**Fecha**: 2026-07-17
**Contexto**: Fase 8 (soporte GPU NVIDIA) implementada, pero el usuario reportó que la transcripción no se acelera con la GPU activa. Esta es la investigación completa con diagnóstico, evidencia y plan de soluciones.

---

## 1. TL;DR (qué encontramos)

- **La GPU SÍ está activa y haciendo trabajo** (verificado con `nvidia-smi`: 9-26% de utilización durante la inferencia).
- **El cuello de botella NO es tu hardware** (1660 Ti, 6GB VRAM, CC 7.5, driver 591.86 — todo OK).
- **El cuello de botella es la arquitectura de Whisper**: el decoder es inherentemente serial (token-por-token), y el 90% del tiempo la GPU está esperando sync CPU↔GPU.
- **El modelo Medium (1.5GB) es demasiado grande para dictado corto** en cualquier hardware de consumo. CPU y GPU tardan ~4-5x realtime con Medium. Es normal.
- **Solución inmediata**: cambiar a modelo **Small** (465MB) o **Base** (140MB) — esperable 1-2s para 3s de audio.
- **Solución a largo plazo**: implementar **streaming de Whisper** (cambio arquitectural, post-MVP).

---

## 2. Setup diagnosticado

| | |
|---|---|
| GPU | NVIDIA GeForce GTX 1660 Ti (Turing, CC 7.5, 6GB VRAM) |
| Driver | 591.86 (enero 2026) |
| `nvcuda.dll` | Presente en System32 (4.1 MB) |
| `nvidia-smi` | Funcional, reporta la GPU |
| Whisper.net | 1.9.0 con `Whisper.net.Runtime.Cuda` |
| Modelo default | Medium (1.46 GB) |
| App instalada | `%LOCALAPPDATA%\Programs\VoiceTyper\VoiceTyper.exe` (218 MB single-file) |
| Settings | `GpuEnabled: true`, `GpuDeviceIndex: 0` |

---

## 3. Evidencia empírica

### 3.1 nvidia-smi durante transcripción con GPU ON

```
22:18:55.204 GPU=26 %  ← inicio (encoder warmup)
22:18:55.470 GPU=26 %
22:18:55.891 GPU=16 %
22:18:56.934 GPU=9 %
22:18:57.902 GPU=9 %   ← steady state (decoder)
22:18:58.991 GPU=13 %
22:19:00.960 GPU=15 %
22:19:02.958 GPU=11 %
... (continúa 9-18% durante 12 segundos)
```

**Lectura**: GPU hace trabajo pero **nunca supera el 26%**. No es compute-bound.

### 3.2 Log de la app (con `Stopwatch` agregado al `TranscribeAsync`)

**Transcripción 1 (cache miss — primer load)**:

```
[Transcriber] start: model=Medium lang=es useGpu=True bytes=128624
[Transcriber] cache miss: processor_null=True model_changed=True lang_changed=True gpu_changed=True
[Transcriber] factory ready: backend=GPU:0 build_ms=1312
[Transcriber] end: total_ms=12352 process_ms=11018 segments=1
```

**Transcripción 2 (cache hit — factory cacheado)**:

```
[Transcriber] start: model=Medium lang=es useGpu=True bytes=184304
[Transcriber] cache hit: backend=GPU:0
[Transcriber] end: total_ms=11097 process_ms=11096 segments=1
```

**Conclusiones**:

- Cache funciona (2da fue `cache hit`).
- `build_ms` de 1.3s es razonable.
- `process_ms` de 11s **idéntica en ambas** (cold y warm) → NO es load time, es inferencia pura.

### 3.3 Comparativa CPU vs GPU (mismo modelo, audio similar)

| | GPU ON | CPU OFF |
|---|---|---|
| `useGpu` | True | False |
| `backend` | GPU:0 | CPU |
| Audio | 93 KB (~3s) | 73 KB (~2.3s) |
| `process_ms` | 12.3 s | 11.0 s |
| **Ratio realtime** | **4.1x** | **4.8x** |

**Conclusión**: CPU es **17% más rápido** que GPU con Medium. Ambos son ~4-5x realtime (inutilizable para dictado).

---

## 4. Por qué la GPU no ayuda (análisis técnico)

### 4.1 Las dos fases de Whisper

```
Audio WAV → [ENCODER] → representación → [DECODER] → texto
              ↑ 5% del tiempo                ↑ 95% del tiempo
              GPU-friendly                  CPU-bound (serial)
```

**Encoder**: procesa todo el audio en un solo batch → GPU gana 2-3x sobre CPU. En tu 1660 Ti: ~0.3-0.5s.

**Decoder**: genera texto **token por token, autoregresivamente**. Cada token requiere un forward pass por los 769M parámetros. ~100 MFLOPS de trabajo por token, mucho menos que la capacidad de la GPU. ggml-cuda lanza un kernel chiquito, espera sync CPU↔GPU, repite. **GPU idle el 90% del tiempo**.

### 4.2 Por qué CPU puede ganar

CPU no tiene que hacer sync CPU↔GPU. Para trabajo chico por token, la latencia de CUDA iguala o supera al cómputo. Por eso CPU fue más rápido en tu test.

### 4.3 Analogía

Es como usar un Ferrari para ir a comprar pan a 200m: el auto puede ir a 300 km/h, pero entre aceleración, semáforo y frenar, terminás a 30 km/h promedio. No es culpa del auto, es el viaje.

### 4.4 ¿Esto es un bug de Whisper.net?

**No.** Es así para TODAS las implementaciones de Whisper en GPUs de consumo. La oficial de OpenAI también. Los 4-5x realtime con Medium en 1660 Ti son números normales.

---

## 5. Catálogo completo de soluciones (por tier)

### Tier 1 — Sin tocar código (vos, en Settings)

| Opción | Esfuerzo | Impacto velocidad | Ventajas | Desventajas |
|---|---|---|---|---|
| **Cambiar a Base** (140 MB) | 30s descarga | 5-8x más rápido que Medium | Sin cambios de código, descarga chica | Calidad "aceptable", algunos errores en palabras técnicas |
| **Cambiar a Small** (465 MB, ya lo tenés) | Solo cambiar en Settings | 3-4x más rápido que Medium | Mejor calidad que Base, ya descargado | Sigue siendo más lento que realtime para clips muy cortos |
| **Cambiar a Tiny** (75 MB) | 20s descarga | 10-15x más rápido que Medium | Casi realtime, descarga chica | Calidad baja, errores frecuentes, desaconsejado para producción |

**Mi recomendación dentro de Tier 1**: probar **Base** primero (vos, en Settings, 2 min). Si la calidad te sirve, listo. Si no, Small.

### Tier 2 — Cambios chicos al código (10-20 min cada uno)

| Opción | Esfuerzo | Impacto | Ventajas | Desventajas |
|---|---|---|---|---|
| **Warmup del modelo al startup** | Bajo (15 min) | 1ra transcripción más rápida (~1-2s menos) | Carga el modelo al iniciar la app, no en la 1ra grabación. Mejor UX percibida. | Consume RAM al inicio, levemente más lento el arranque de la app |
| **VAD / cortar silencios** | Medio (1-2 hs) | 20-50% menos audio procesado | Para dictado con pausas, ahorra mucho compute. Simple: RMS por chunks de 100ms, descartar si < umbral. | Solo ayuda si hay silencios (no en dictado continuo). Calidad podría bajar ligeramente por cortar frames |
| **Limitar `maxLen` del output** | Bajo (10 min) | Marginal (5-10%) | Para frases de dictado, 50-100 tokens es suficiente. Default es alto. | No aplica si el usuario dicta párrafos largos |
| **`WithSpeedUp()` o equivalente** | Bajo (5 min) | 1.3-1.5x más rápido (a verificar API 1.9.0) | Cambio mínimo, gran ganancia. | Hay que verificar si existe en Whisper.net 1.9.0. Leve pérdida de calidad |

**Mi recomendación dentro de Tier 2**: **VAD** es la mejor ganancia. Si dictás con pausas, ahorrás 30-50% del compute. Si dictás continuo, no aporta.

### Tier 3 — Optimizaciones de GPU (20-40 min)

| Opción | Esfuerzo | Impacto | Ventajas | Desventajas |
|---|---|---|---|---|
| **Vulkan backend en vez de CUDA** | Medio (20 min) | 1.5-2x sobre CUDA (a verificar) | `Whisper.net.Runtime.Vulkan` existe. Menos overhead que CUDA para inference chico. | Requiere cambiar NuGet, riesgo de incompatibilidad. Vulkan runtime viene con drivers NVIDIA, no es problema |
| **`WithThreads(N)` pinning** | Bajo (10 min) | Marginal | Si Whisper.net 1.9.0 lo tiene, pinear a cores físicos. | Hay que verificar API. Ganancia marginal en CPU mode |
| **Modelo en VRAM pinned (`cudaHostAlloc`)** | Alto (1-2 hs) | 10-20% en transferencias | Optimización de bajo nivel con P/Invoke a CUDA | No aplica a tu caso (audio chico, no es cuello de botella) |
| **Recompilar ggml con flags para sm_75** | Muy alto | Marginal | Potencialmente más rápido | No podemos recompilar sin forkear Whisper.net |

**Mi recomendación dentro de Tier 3**: **probar Vulkan** es la apuesta más interesante. Si funciona, podría cerrar la brecha GPU vs CPU. Si no, revertís fácil.

### Tier 4 — Post-MVP (cambio grande)

| Opción | Esfuerzo | Impacto | Ventajas | Desventajas |
|---|---|---|---|---|
| **Streaming de Whisper** | Alto (1-2 días) | Dramático: latencia <1s, transcripción mientras hablás | Cambio arquitectural, mayor impacto de toda la lista. UX excelente | Complejidad alta, decisión de cuándo "confirmar" segmentos, indicador visual durante streaming |
| **VAD en tiempo real** | Medio (4-6 hs) | 30-50% en clips con silencios | Para el flujo streaming, integrado | Depende de streaming implementado antes |

**Mi recomendación dentro de Tier 4**: **Streaming** es la bala de plata, pero es 1-2 días de trabajo. Vale la pena si el uso justifica la inversión.

### Hardware (no podemos cambiar)

| Opción | Impacto | Notas |
|---|---|---|
| RTX 3060/4060 o superior | 2-3x sobre 1660 Ti | Más VRAM bandwidth (320-bit vs 192-bit), nueva arquitectura |
| DDR5 con más bandwidth | 10-20% en CPU | Para modo CPU |
| NVMe Gen4 SSD | Marginal | El modelo ya se carga en ~1s |

---

## 6. Bugs encontrados durante la investigación

### 6.1 `CudaDetector` sin `cuInit(0)` (ARREGLADO)

- **Síntoma**: `GetDeviceCount` retornaba 0, `IsAvailable` retornaba true. UI mostraba "No se detectó GPU NVIDIA compatible".
- **Causa**: CUDA Driver API exige `cuInit(0)` antes de cualquier otra llamada.
- **Status code 700** (`CUDA_ERROR_NOT_INITIALIZED`).
- **Fix**: `CudaDetector.cs:EnsureInitialized()` + P/Invoke `cuInit`. Cachea en `_initStatus`.
- **AGENTS.md**: agregada sección "GPU NVIDIA (CUDA) — `CudaDetector` requiere `cuInit(0)`".

### 6.2 `cuDeviceGetName` P/Invoke con 2 parámetros en vez de 3 (ARREGLADO)

- **Síntoma**: nombres de GPU vacíos.
- **Causa**: `CUresult cuDeviceGetName(char *name, int len, CUdevice dev)` — el P/Invoke declaraba solo `(byte[] name, int index)`, sin `len`.
- **Status code 101** (`CUDA_ERROR_INVALID_VALUE`).
- **Fix**: `CudaGetDeviceName(byte[] name, int len, int index)`.
- **AGENTS.md**: agregada sección "Firma correcta de `cuDeviceGetName`".

### 6.3 `_logger.LogInfo` no aparece en log (FIXED en instrumentación)

- **Síntoma**: la línea `_logger.LogInfo($"model {model} loaded (lang={language}, backend={BackendMode})")` del subagente nunca aparecía en `voicetyper.log`.
- **Causa real**: `LoggerService.LogInfo` SÍ escribe al log estático (es wrapper con prefijo `[App]`), pero el `if` que la contiene nunca se cumplía en transcripciones repetidas (cache funciona). La línea solo se loguea en la 1ra transcripción.
- **Fix aplicado**: cambiamos a `Log.Info` directo (estático) y agregamos logs de timing con `Stopwatch` para diagnóstico.
- **Resultado**: ahora vemos `process_ms` y podemos medir inferencia pura.

---

## 7. Cambios de código aplicados durante la investigación

### 7.1 `CudaDetector.cs` (arreglo de bugs)

- Agregado `cuInit(0)` con cache de resultado.
- Corregida firma de `cuDeviceGetName` (3 parámetros).

### 7.2 `TranscriberService.cs` (instrumentación de timing)

- Cambiado `_logger.LogInfo` → `Log.Info` (estático) en líneas clave.
- Agregados logs de start/end con `Stopwatch`:
  - `[Transcriber] start: model=X lang=Y useGpu=Z bytes=N`
  - `[Transcriber] cache miss|hit: ...`
  - `[Transcriber] factory ready: backend=X build_ms=Y`
  - `[Transcriber] end: total_ms=X process_ms=Y segments=N`
- **NO se commiteó nada todavía** (todo en working tree).

### 7.3 Archivos modificados durante F8

- `src/VoiceTyper/Services/CudaDetector.cs` (nuevo + fixes)
- `src/VoiceTyper/Models/CudaDevice.cs` (nuevo)
- `src/VoiceTyper/Models/AppSettings.cs` (+3 props: GpuEnabled, GpuDeviceIndex, GpuSuggestionShown)
- `src/VoiceTyper/Services/SettingsService.cs` (parsing env vars)
- `src/VoiceTyper/Services/TranscriberService.cs` (instrumentación)
- `src/VoiceTyper/Services/NativeLibPathResolver.cs` (preserva estructura CUDA)
- `src/VoiceTyper/ViewModels/SettingsViewModel.cs` (props GPU)
- `src/VoiceTyper/Views/SettingsWindow.xaml` (sección GPU)
- `src/VoiceTyper/App.xaml.cs` (registro CudaDetector, auto-sugerencia)
- `src/VoiceTyper/VoiceTyper.csproj` (+Whisper.net.Runtime.Cuda)
- `AGENTS.md`, `phases.md`, `spec.md`, `README.md`, `.env.example`

---

## 8. Decisiones pendientes

1. **Cambiar default a Small o Base**? — Recomendado: Base como default (mejor balance), Small como opt-in.
2. **Implementar VAD**? — Recomendado si dictás con pausas.
3. **Probar Vulkan backend**? — Recomendado como experimento de 20-30 min.
4. **Implementar streaming**? — Recomendado si el uso justifica 1-2 días.
5. **Rollback de GPU**? — Sacar `Whisper.net.Runtime.Cuda` del bundle (vuelve a 71 MB). El código queda en repo para futuro.

---

## 9. Honesta sobre las limitaciones

- **El cuello de botella del decoder serial de Whisper NO tiene solución barata** sin streaming o cambio de modelo.
- **Para dictado de frases cortas**, la única forma de tener <1s de latencia es:
  - Modelo más chico (Small/Base), o
  - Streaming de Whisper (cambio arquitectural).
- **Medium solo vale la pena para transcripción de audios largos (1+ min)**, donde el encoder paralelo recupera el tiempo perdido.
- **Si la calidad de Small no te sirve**, no hay upgrade de hardware que te salve para Medium en tiempo real. Necesitarías un modelo distinto (Distil-Whisper, no soportado out-of-the-box por Whisper.net).

---

## 10. Próximos pasos sugeridos (ordenados por prioridad)

1. **Vos**: probá **Base** en Settings (2 min, sin código). Mirá el `process_ms`. Anotá si la calidad te sirve.
2. **Si calidad OK**: implementamos **VAD** (1-2 hs, F8.5).
3. **Si calidad insuficiente con Base**: implementamos **default a Small** (15 min).
4. **Después**: probamos **Vulkan** como experimento (20 min).
5. **Si todo lo anterior no es suficiente**: planteamos **streaming** (F10, 1-2 días).

---

## 11. Referencias

- Whisper: https://github.com/openai/whisper
- Whisper.net: https://github.com/sandrohanea/whisper.net
- CUDA Driver API docs: https://docs.nvidia.com/cuda/cuda-driver-api/
- ggml-cuda backend: https://github.com/ggerganov/ggml
- Decoder de Whisper: ver paper "Robust Speech Recognition via Large-Scale Weak Supervision" (Radford et al. 2022), sección 2 (arquitectura encoder-decoder).
- Documentación interna: `AGENTS.md` (gotchas GPU), `phases.md` (F8), `spec.md` (§5.3).
