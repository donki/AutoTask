# sOC AutoTask — Arquitectura

> Cómo está hecho lo que pide [ESPECIFICACION.md](ESPECIFICACION.md). Si algo de aquí cambia, se
> cambia en el mismo commit que el código.

## 1. Proyectos

```
AutoTask/
├── AutoTask.slnx
├── src/
│   ├── AutoTask.Core/         biblioteca sin interfaz (net10.0-windows, sin WPF): modelo, formato
│   │                          .soctask, importador .rec, editor, plan de reproducción, reproductor,
│   │                          ganchos y SendInput, atajos, ajustes, rutas, registro, textos es/en,
│   │                          instancia única, compilación a exe. Apta para Native AOT.
│   ├── sOCAutoTask/           la aplicación WPF (exe sOCAutoTask.exe). Solo ventanas y pegamento.
│   └── sOCAutoTaskPlayer/     el reproductor mínimo (stub) de los exe compilados. Native AOT.
├── tests/
│   ├── AutoTask.Tests/        xUnit sobre AutoTask.Core y la parte sin ventanas de la app (§8.6).
│   └── AutoTask.UITests/      FlaUI sobre el exe Debug en modo aislado (§8.7) + prueba E2E real
│                              contra una ventana de prueba propia (AutoTask.TestTarget).
│       └── AutoTask.TestTarget/  ventana WPF con una casilla y un botón contador.
├── Package/                   manifiesto e imágenes del MSIX.
├── store/microsoft/           fichas de la Store (es-ES, en-US).
└── tools/                     entregar.ps1, empaquetar-msix.ps1, compilar-reproductor.ps1, logos.py
```

La regla de §5 de la constitución: la lógica vive en `AutoTask.Core` y la interfaz solo la usa.
Todo lo que toca Windows de verdad (ganchos, `SendInput`, registro, tubería de instancia única)
está detrás de una interfaz o en una clase pequeña de `Native/`, para que la lógica se pruebe con
dobles.

## 2. Hilos

```
 Hilo de la interfaz (WPF)          Hilo del gancho (Recorder)         Hilo del reproductor (Player)
 ─────────────────────────          ──────────────────────────         ──────────────────────────────
 botones, atajos (RegisterHotKey)   SetWindowsHookEx LL ratón/teclado   recorre el plan con reloj
 ventana, bandeja, editor           GetMessage/Dispatch (bucle propio)  absoluto (Stopwatch) y
 recibe progreso por eventos  ◄──── copia el evento a una lista con     SendInput por evento; mira
 (Dispatcher.BeginInvoke)           lock y vuelve (CallNextHookEx)      la bandera de parada
                                                                        │
                                    Hilo del vigilante de emergencia ◄──┘ (gancho LL de teclado
                                    propio mientras se reproduce: Pausa, Bloq Despl, Esc mantenida)
```

- El gancho **nunca** hace E/S ni toca la interfaz: añade un `MacroEvent` (struct de 24 bytes) a
  una lista y devuelve. Windows quita un gancho LL que tarda más de `LowLevelHooksTimeout`.
- La parada (`PlaybackControl.Stop`) es una bandera + `ManualResetEvent`: las esperas largas del
  reproductor despiertan al momento.
- `timeBeginPeriod(1)` solo mientras se reproduce. Las esperas: `WaitOne` hasta 2 ms antes y
  espera activa (`SpinWait`) el resto.

## 3. Modelo

`MacroEvent` (struct inmutable): `Kind` (byte), `Button` (byte), `Flags` (byte), `DelayMs`
(int, desde el anterior), `X`, `Y` (int, escritorio virtual en píxeles físicos), `Data` (int:
cantidad de rueda; en teclas, `vk | scan << 16`; en `Wait`, nada).

| Kind | Significado | Campos |
|---|---|---|
| 0 `MouseMove` | movimiento | X, Y |
| 1 `MouseDown` / 2 `MouseUp` | botón | X, Y, Button (0 izq, 1 der, 2 central, 3 X1, 4 X2) |
| 3 `Wheel` / 4 `HWheel` | rueda vertical / horizontal | X, Y, Data = cantidad (±120 por muesca) |
| 5 `KeyDown` / 6 `KeyUp` | tecla | Data = vk + scan; Flags bit 0 = extendida |
| 7 `Wait` | espera explícita (editor) | solo DelayMs |

`Recording` = lista de eventos + `PlaybackOptions` (velocidad, modo de repetición, N, pausa entre
vueltas) + pantalla virtual con que se grabó + fecha. `TotalMs` = suma de esperas.

## 4. Formato `.soctask` (versión 1)

Todo en *little endian*. Se escribe y se lee por flujo.

```
Desplazamiento  Tamaño  Campo
0               8       Firma "SOCTASK" + 0x1A
8               2       Versión del formato (1). Mayor que la conocida → «versión más nueva».
10              2       Marcas (0; reservado)
12              4       Longitud de la cabecera de opciones (H), hoy 64
16              H       Opciones:
                          double  velocidad (0,1–1000)
                          byte    repetición (0 una vez, 1 N veces, 2 continuo)
                          int32   N
                          int32   pausa entre vueltas (ms)
                          int32×4 pantalla virtual al grabar (izq, arriba, ancho, alto)
                          int64   fecha de grabación (ticks UTC)
                          int64   número de eventos
                          int64   duración total (ms)
                          relleno hasta H (los lectores ignoran lo que no conocen)
16+H            4       Longitud del bloque comprimido (C)
20+H            C       Eventos comprimidos con Deflate (ver abajo)
20+H+C          4       CRC-32 (IEEE) de: los 16+H bytes de cabecera, los C bytes comprimidos y
                        la longitud C (en ese orden). Cualquier byte cambiado se nota.
```

Cada evento sin comprimir: `Kind` (1 byte) + `DelayMs` (varint) + según el tipo: X e Y (varint
zigzag), `Button` (1 byte), `Data` (varint zigzag), `Flags` (1 byte). Un millón de movimientos
ocupa unos 3 MB sin comprimir y menos de 1 MB comprimido.

**Validación al leer** (cada fallo es un `RecordingFormatException` con su `Reason`):
firma (`NotARecording`), versión (`NewerVersion`), cabecera corta o bloque más corto que C
(`Truncated`), CRC distinto, número de eventos o duración distintos (`Corrupt`), tipo de evento, botón o
velocidad imposibles (`Corrupt`). Los mensajes al usuario salen de `Loc` (§6.9).

**Guardar** escribe a `fichero.soctask.tmp` y lo cambia con `File.Replace`/`Move` al final.

## 5. Importar `.rec` (experimental)

El `.rec` de la utilidad clásica es la lista de estructuras `EVENTMSG` (de su gancho de diario) de
20 bytes: `message` (uint32), `paramL` (uint32), `paramH` (uint32), `time` (uint32, milisegundos de
`GetTickCount`, absoluto) y `hwnd` (uint32). Se interpreta:

- `0x0200` movimiento; `0x0201/0x0202` izq.; `0x0204/0x0205` der.; `0x0207/0x0208` central;
  `0x020B/0x020C` X (se toma X1: el diario no dice cuál); `0x020A` rueda y `0x020E` rueda
  horizontal (el diario no guarda la dirección: se importa una muesca hacia arriba o a la derecha,
  y se avisa al importar). `paramL`/`paramH` = X/Y.
- `0x0100/0x0104` tecla pulsada, `0x0101/0x0105` soltada: vk = byte bajo de `paramL`, scan = byte
  siguiente (o byte bajo de `paramH` si vale 0), extendida = bit 15 de `paramH`.
- Espera = diferencia de `time` con el anterior.

Se rechaza si el tamaño no es múltiplo de 20, si menos del 90 % de los registros son mensajes
conocidos, o si el tiempo retrocede más de 1 s. **No se ha probado con ficheros reales** de esa
utilidad (no se ejecutan programas de terceros en el equipo de desarrollo); por eso se presenta como
experimental. Fuente del formato: la estructura `EVENTMSG` documentada por Microsoft y descripciones
públicas del formato.

## 6. Compilar a exe

```
[ sOCAutoTaskPlayer.exe (Native AOT, ~1,5 MB) ][ .soctask completo ][ opciones del exe (32 B) ][ int64 longitud ][ "SOCTPAY1" ]
```

- La aplicación lleva el reproductor **dentro** como recurso incrustado
  (`AutoTask.Player.exe`, lo pone `tools\compilar-reproductor.ps1` antes de publicar). En Debug,
  si no está, Compilar avisa de que esta compilación no lo trae.
- Las **opciones del exe**: cuenta atrás, teclas de emergencia elegidas y tiempo de Esc mantenida.
- El reproductor abre su propio exe (`Environment.ProcessPath`), lee los 16 últimos bytes, comprueba
  la firma y la longitud, y lee la grabación con el **mismo código** de `AutoTask.Core` que la
  aplicación (enlazado en AOT). `--check` valida y sale sin reproducir.
- Firma de código: el catálogo no firma los exe (no hay certificado de firma de código; las
  tiendas firman sus paquetes). Aunque lo hubiera, añadir datos detrás de un exe firmado invalida
  su firma Authenticode, y firmar cada exe generado en el equipo del usuario exigiría repartir la
  clave. Conclusión: los exe compilados van **sin firma**, como los de cualquier otro grabador de
  macros; si un día hay certificado, se firmará la aplicación y el reproductor incrustado (para
  `--check` y para quien lo extraiga), no los exe generados.
- **Por qué es seguro** (para el aviso del antivirus): el reproductor no tiene red, no escribe en
  disco, no se copia ni se instala, no lee nada salvo a sí mismo, y solo envía los eventos que hay
  en su grabación; su código es este repositorio. Los antivirus desconfían de cualquier exe nuevo
  sin firma que simula teclado y ratón.

## 7. Reproducción

`PlaybackPlan` calcula, para cada evento, el instante absoluto `t = Σ esperas / velocidad`, y la
duración de la vuelta. `Player` (en un hilo) recorre las vueltas: espera hasta el instante de cada
evento con `IClock`, llama a `IInputSink.Send(evento)` y lleva el conjunto de teclas y botones que
siguen pulsados para soltarlos al acabar o al parar (RF-14). Los dobles de prueba son
`FakeClock` (avanza al pedir esperar) y `RecordingSink`.

`Win32InputSink` traduce a `SendInput`:

- Ratón: `MOUSEEVENTF_MOVE | ABSOLUTE | VIRTUALDESK` con la coordenada normalizada
  `n = ((x − izq) · 65536 + ancho − 1) / ancho` (sin pasar de 65535), que Windows devuelve al
  mismo píxel. Los puntos fuera de la pantalla actual se llevan al borde.
- Botones: el movimiento al punto y la pulsación en la misma entrada.
- Teclas: `wVk` + `wScan` + `KEYEVENTF_EXTENDEDKEY` si toca + `KEYEVENTF_KEYUP`.

## 8. Atajos y parada de emergencia

- Atajos globales con `RegisterHotKey` sobre la ventana principal (no hace falta gancho).
- Texto ↔ atajo (`Hotkey.Parse("Ctrl+Alt+Shift+R")`) en Core, probado. Se exige al menos un
  modificador y que no coincidan los dos.
- Que el atajo no quede grabado: `RecordingCleaner` quita las sueltas sin pulsación previa y las
  pulsaciones sin suelta al final (las del atajo que para), y el gancho ignora los clics dentro del
  rectángulo de la ventana de AutoTask.
- Parada de emergencia: `EmergencyStopWatcher` (gancho LL de teclado en su hilo, solo mientras se
  reproduce) con la lógica en `EmergencyStopLogic` (pura y probada): Pausa o Bloq Despl al
  pulsar, Esc tras mantenerla el tiempo elegido; se ignoran los eventos inyectados.

## 9. Ajustes, rutas e instancia única

- `AppPaths.Resolve`: si existe `portable.ini` junto al exe, la carpeta del exe; si no,
  `%LOCALAPPDATA%\sOCAutoTask`. `SOC_SANDBOX` (Debug) la cambia por una temporal.
- `settings.json` (System.Text.Json con generador de código): idioma, tema, atajos, emergencia,
  cuenta atrás, grabar teclado y movimientos, siempre encima, etiquetas, bandeja, opciones de
  reproducción por defecto, recientes, guía vista, última versión vista, asociación.
- Instancia única (§8.3): mutex `Local\sOCAutoTask.Instance` + tubería con nombre
  `sOCAutoTask.<sesión>.<usuario>`. La copia nueva manda `HELLO <versión>`; la vieja contesta
  `VERSION <suya> <pid>`. Si la vieja es de una versión anterior, la nueva le manda `QUIT` y espera a
  que salga (5 s, si no la mata); si es la misma, le manda `SHOW <fichero>` y espera `ACK` (3 s; si
  no llega, arranca igual). La comparación de versiones es pura y probada.
- Asociación de `.soctask`: `FileAssociation.Entries(exe)` da la lista de claves y valores (pura,
  probada) y `Apply/Remove` la escribe en `HKCU\Software\Classes`.

## 10. Interfaz

- Iconos: SVG en `src/sOCAutoTask/Assets/Icons/ic_<nombre>.svg` (24×24, `fill="none"`, trazo 1.8,
  puntas redondas). `SvgIcons` los lee del recurso y los convierte en `Geometry` (path, line,
  polyline, rect, circle); el control `Icon` los pinta con el color del texto del botón. Las
  banderas (`ic_flag_es`, `ic_flag_us`) se pintan con su color. Las pruebas comprueban que todos se
  leen.
- Textos: `Loc` en Core (tablas es/en). En XAML, `{loc:T Clave}` devuelve un enlace al indexador
  de `LocSource`, que avisa al cambiar de idioma: **todo** se retraduce en caliente.
- Tema: `ThemeManager` cambia los pinceles (`DynamicResource`) y la barra de título (DWM).
- Ventanas: `MainWindow` (barra), `SettingsWindow`, `EditorWindow`, `GuideWindow`,
  `WhatsNewWindow`, `AboutWindow`, `PromptWindow` (avisos y confirmaciones propias).
- `AutomationId` = `x:Name` en todo lo que pulsan las pruebas.

## 11. Pruebas

- `AutoTask.Tests` (xUnit, coverlet): formato y validación, importador `.rec`, plan y reproductor
  con reloj y sumidero falsos (velocidad, vueltas, pausas, parada, soltar teclas), limpieza de la
  grabación, editor, emergencia, atajos, compilación (escribir y encontrar la carga en un «exe»
  falso y en el reproductor real si está compilado), rutas portátiles, ajustes, asociación,
  versiones, textos es/en, iconos SVG.
- `AutoTask.UITests` (FlaUI, `SOC_SANDBOX`): arranque, ajustes, idioma, acerca de, novedades,
  guía, editor con una grabación de prueba. Nunca se reproduce sobre el escritorio en estas.
- E2E real (`AUTOTASK_E2E=1`): contra `AutoTask.TestTarget`, en ráfagas de pocos segundos y
  devolviendo el ratón a donde estaba.
