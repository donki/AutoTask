# sOC AutoTask

Grabador y reproductor de macros de ratón y teclado para Windows: pulsas **Grabar**, haces lo que
quieras, pulsas **Parar**, y **Reproducir** lo repite igual, más rápido o en bucle. Se inspira en
[TinyTask](https://tinytask.net/) (ventana diminuta, un solo exe, compilar a exe) y añade editor de
eventos, parada de emergencia, velocidad a medida, formato de fichero validado y modo claro/oscuro
en español e inglés.

## Dónde conseguirla

- **Releases de GitHub** (exe autocontenido y MSIX de cada versión): https://github.com/donki/AutoTask/releases
- **Microsoft Store:** aún no (ficha preparada en `store/microsoft/`).

## Qué hace

- **Grabar** (botón o **Ctrl+Alt+Mayús+R**, configurable): movimientos, clics de los cinco botones,
  rueda vertical y horizontal y teclas (pulsar y soltar, modificadores incluidos), en todo el
  sistema, con la espera entre eventos. Ganchos de bajo nivel en un hilo propio con su bucle de
  mensajes. El atajo, los clics sobre la propia ventana y lo inyectado por otros programas no se
  graban. Coordenadas de escritorio virtual en píxeles físicos (per-monitor DPI aware v2): sirve
  con monitores a la izquierda (x negativas) y escalados distintos.
- **Reproducir** (botón o **Ctrl+Alt+Mayús+P**; el mismo atajo para) con `SendInput` y reloj
  absoluto (los retrasos no se acumulan). **Parada de emergencia**: Pausa/Inter, Bloq Despl o Esc
  mantenida (1 s por defecto), cada una activable; solo cuentan las teclas pulsadas de verdad. Al
  parar se sueltan las teclas y botones que quedaran pulsados. Antes de empezar espera a que
  sueltes Ctrl/Alt/Mayús/Win; cuenta atrás opcional (0–10 s); tiempo restante a la vista.
- **Velocidad** 0,5×, 1×, 2×, 4×, 10×, 100× o personalizada (0,1–1000×). **Repetir** una vez,
  N veces o sin fin, con pausa entre vueltas. Van con la grabación.
- **Ficheros `.soctask`** (binario versionado con firma, Deflate y CRC-32; formato en
  [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md)), recientes, arrastrar y soltar, línea de órdenes
  (`sOCAutoTask.exe fichero.soctask [--play]`), asociación opcional por usuario (HKCU). Un fichero
  dañado, cortado o de una versión futura se explica en tu idioma.
- **Importar `.rec`** de TinyTask — **experimental**: se lee como lista de `EVENTMSG` de 20 bytes y
  se valida la forma; está probado con ficheros sintéticos, no con ficheros reales de TinyTask (no
  se ejecutan programas de terceros en el equipo de desarrollo). La rueda se importa siempre hacia
  arriba (el `.rec` no guarda la dirección).
- **Compilar a .exe**: un reproductor nativo (Native AOT, ~1,4 MB) con la grabación añadida al
  final; al abrirlo la reproduce con su velocidad, repeticiones, cuenta atrás y parada de
  emergencia. `macro.exe --check` valida sin reproducir.
- **Editor**: lista virtualizada con instante, espera, tipo y detalle; borrar (Supr), recortar
  inicio/final, simplificar movimientos (Ramer–Douglas–Peucker con tolerancia, o solo el último
  antes de cada clic), cambiar la espera, escalar esperas, insertar una espera, deshacer (Ctrl+Z).
  El tiempo total se conserva salvo al recortar.
- **Interfaz**: barra pequeña con iconos SVG planos y etiquetas ocultables, siempre encima
  opcional, área de notificación al minimizar (con Grabar/Reproducir en su menú), tema del sistema,
  claro u oscuro, español/inglés al momento, guía de configuración, novedades, «Acerca de»,
  «Reiniciar como administrador», instancia única (la versión nueva manda) y **modo portátil**
  (con un `portable.ini` junto al exe, los ajustes van ahí).

## Seguridad y honestidad

- Solo actúa sobre lo que hay en pantalla, como si fueras tú. Windows no deja enviar teclas o
  clics a programas que se ejecutan como administrador: AutoTask lo detecta, avisa y ofrece
  reiniciarse como administrador. Algunos juegos (entrada directa o antitrampas) ignoran la entrada
  simulada.
- **Una grabación guarda todo lo que tecleas**, contraseñas incluidas: se avisa en Ajustes y en la
  guía, y se puede grabar solo el ratón.
- **Sin red**: no hay ninguna conexión (tampoco comprobación de versión, como el resto de apps de
  escritorio del catálogo).
- **Antivirus y exe compilados**: un exe nuevo, sin firma, que mueve el ratón y el teclado puede
  hacer saltar un antivirus. Es seguro: el reproductor no usa la red, no escribe en disco, no se
  instala ni se copia, solo lee su propio exe y envía los eventos grabados; su código está aquí
  (`src/sOCAutoTaskPlayer`). Los exe generados no van firmados: añadir datos a un exe firmado rompe
  su firma (detalle en ARQUITECTURA §6).

## Dónde guarda las cosas y a qué accede

- `%LOCALAPPDATA%\sOCAutoTask\settings.json` (o junto al exe en modo portátil) y `errors.log`
  (sin nada de lo grabado). Las grabaciones, donde tú las guardes.
- Registro: solo si activas la asociación, `HKCU\Software\Classes\.soctask` y
  `HKCU\Software\Classes\sOCAutoTask.Recording`.

## Compilar

Necesita el SDK de .NET 10 y, para el reproductor de los exe compilados (Native AOT), las Build
Tools de Visual Studio con «Desarrollo para el escritorio con C++».

```
.\tools\compilar-reproductor.ps1          # artifacts\player\sOCAutoTaskPlayer.exe (se incrusta en la app)
dotnet build src\sOCAutoTask -m:1 -nodeReuse:false
.\tools\empaquetar-msix.ps1               # exe autocontenido + MSIX en bin\
.\tools\entregar.ps1 -Version 2026.10.01.0 -Mensaje "..."   # + OneDrive, commit, push y release
```

Estructura: `src/AutoTask.Core` (toda la lógica, sin interfaz), `src/sOCAutoTask` (WPF),
`src/sOCAutoTaskPlayer` (reproductor AOT), `tests/`. Especificación en
[docs/ESPECIFICACION.md](docs/ESPECIFICACION.md) y arquitectura en
[docs/ARQUITECTURA.md](docs/ARQUITECTURA.md).

## Pruebas

`tests/AutoTask.Tests` (xUnit): **301 pruebas, 301 pasan**. Lógica: formato `.soctask` (ida y
vuelta, cualquier byte cambiado, cortes en cada posición, versión futura, un millón de eventos),
importador `.rec`, plan y reproductor con reloj y `SendInput` falsos (velocidades, vueltas, pausas,
cuenta atrás, espera de modificadores, parada en cada fase, soltar teclas), limpieza del atajo,
editor, parada de emergencia, coordenadas, atajos, compilar a exe, ajustes, rutas portátiles,
instancia única, asociación en una rama de pruebas del registro, textos es/en e iconos SVG. Además,
desde la 2026.10.03.0, **las ventanas de verdad** (barra, ajustes, editor, guía, «Acerca de»,
novedades, diálogos, bandeja y atajos, arranque) en un hilo STA con una **plataforma falsa**
(`Services/Platform.cs`: sin ganchos, sin `SendInput`, sin diálogos de Windows, registro en una
rama de pruebas), los `INPUT` exactos que saldrían por `SendInput`, lo que hacen los ganchos con
cada mensaje y el reproductor de los exe compilados. Nada toca datos reales ni la red, y nada
mueve el ratón.

- Cobertura de lo instrumentado: **94,9 %** (3002 de 3162 líneas). Ahora se instrumenta la
  aplicación entera (núcleo, ventanas y reproductor), así que coincide con la de toda la app.
- Cobertura sobre toda la app: **94,9 %** (3002 de 3162 líneas ejecutables; antes 57,0 % con la
  misma medida).
- Tiempo del banco: **unos 20 s** (`dotnet test --no-build`; las ventanas se abren fuera de la
  pantalla y sin activarse). Fecha: 2026-10-03.

**Cómo se cuenta «toda la app»** (`tools/cobertura-app.py`, General §8.6): todos los `.cs` de
`src/` salvo `obj/`, `bin/` y generados; solo cuentan las líneas con sentencias (fuera llaves
sueltas, `using`, declaraciones sin cuerpo como las de `LibraryImport`, campos sin inicializar,
atributos, interfaces y comentarios). Un fichero que el banco compila cuenta lo que marca coverlet
(sin excluir `CompilerGeneratedAttribute`: los métodos async y las lambdas cuentan); uno que no
compila contaría todas sus sentencias como no cubiertas. Lo que queda sin cubrir: el menú
contextual nativo de la bandeja, los diálogos comunes de Windows y `Process.Start` de la
plataforma de verdad, el `Main` del reproductor, algunos fallos de Windows al poner los ganchos y
ramas de la instancia única que necesitan otro proceso.

`tests/AutoTask.UITests` (FlaUI, [README](tests/AutoTask.UITests/README.md)): **8 pruebas de
interfaz** en modo aislado (`SOC_SANDBOX`), unos **20 s**; y **3 pruebas reales** (con
`AUTOTASK_E2E=1`) que graban y reproducen texto y clics contra una ventana de prueba propia,
comprueban un clic en el monitor de la izquierda y que el exe compilado reproduce lo grabado
(unos 15 s, moviendo el ratón en ráfagas de pocos segundos y solo con el PC inactivo).

```
dotnet test tests\AutoTask.Tests
dotnet test tests\AutoTask.Tests --collect:"XPlat Code Coverage" --settings tests\AutoTask.Tests\coverage.runsettings
dotnet tool restore
dotnet tool run reportgenerator -reports:tests\AutoTask.Tests\TestResults\*\coverage.cobertura.xml -targetdir:cobertura -reporttypes:TextSummary
python tools\cobertura-app.py tests\AutoTask.Tests\TestResults --app src --detalle
```

## Qué puede romper

Una macro repite lo grabado aunque la pantalla haya cambiado: puede pulsar donde ya no toca. Para
eso está la parada de emergencia. Fuera de eso, nada: no borra ni modifica nada salvo los ficheros
que tú guardes.

## Licencia

MIT. Sin componentes de terceros en lo que se distribuye ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).
Privacidad: [PRIVACY.md](PRIVACY.md).
