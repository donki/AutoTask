# Changelog — sOC AutoTask

## 2026.10.03.0 — Pruebas de las ventanas / Window tests

**Español**

- Las ventanas (barra, ajustes, editor, guía, «Acerca de», novedades, diálogos, bandeja y atajos)
  y el reproductor de los exe compilados se prueban ahora con el banco automático: **301 pruebas**
  y **94,9 %** de cobertura sobre toda la aplicación (antes 191 pruebas y 57,0 % con la misma
  medida). Lo que la ventana pide al sistema (ganchos, `SendInput`, diálogos de abrir y guardar,
  registro, instancia única) pasa por una pieza que en las pruebas se cambia por un doble.
- Al cerrar la ventana principal o la de ajustes ya no se quedan enganchadas al cambio de idioma.
- Sin cambios en lo que se ve ni en cómo se usa.

**English**

- The windows (toolbar, settings, editor, guide, About, what's new, dialogs, tray and shortcuts)
  and the compiled-exe player are now covered by the automated test suite: **301 tests** and
  **94.9 %** line coverage of the whole app (was 191 tests and 57.0 % measured the same way).
  What the windows ask of the system (hooks, `SendInput`, open/save dialogs, registry, single
  instance) goes through one piece that tests replace with a double.
- Closing the main or settings window no longer leaves it subscribed to language changes.
- Nothing changes in what you see or how you use it.

## 2026.10.01.0 — Primera versión / First release

**Español**

- **Grabar** el ratón (movimientos, clics de los cinco botones, rueda vertical y horizontal) y el
  teclado en todo el sistema, con el botón o con **Ctrl+Alt+Mayús+R**. El atajo y los clics sobre
  la propia ventana no se graban. Funciona con varios monitores y con la pantalla escalada.
- **Reproducir** con el botón o **Ctrl+Alt+Mayús+P** (el mismo atajo para), con el tiempo que
  queda a la vista y cuenta atrás opcional. **Parada de emergencia** con Pausa, Bloq Despl o
  manteniendo Esc; al parar se sueltan todas las teclas.
- **Velocidad** 0,5×, 1×, 2×, 4×, 10×, 100× o la que elijas (0,1× a 1000×); **repetir** una vez,
  N veces o sin fin, con pausa entre vueltas. Se guardan con la grabación.
- **Ficheros** `.soctask` (comprobados al abrir: un fichero dañado se explica y no rompe nada),
  recientes, arrastrar y soltar, asociación opcional, e **importar `.rec`** (experimental).
- **Compilar a .exe**: un ejecutable de unos 1,4 MB que reproduce la grabación en cualquier PC,
  con su parada de emergencia.
- **Editor**: borrar, recortar inicio y final, simplificar movimientos, cambiar, escalar e insertar
  esperas, deshacer.
- Ventana pequeña con iconos y etiquetas ocultables, siempre encima opcional, área de
  notificación, claro/oscuro, español/inglés al momento, guía de configuración, novedades,
  reiniciar como administrador y modo portátil. Sin red.
- Pruebas: 191 de lógica, 8 de interfaz y 3 reales de grabar y reproducir.

**English**

- **Record** the mouse (moves, clicks of all five buttons, vertical and horizontal wheel) and the
  keyboard system-wide, with the button or **Ctrl+Alt+Shift+R**. The shortcut and clicks on the
  app's own window aren't recorded. Works with several monitors and scaled displays.
- **Play** with the button or **Ctrl+Alt+Shift+P** (the same shortcut stops), with the time left
  on screen and an optional countdown. **Emergency stop** with Pause, Scroll Lock or by holding Esc;
  stopping releases every key.
- **Speed** 0.5×, 1×, 2×, 4×, 10×, 100× or your own (0.1× to 1000×); **repeat** once, N times or
  forever, with a pause between loops. Saved with the recording.
- **Files** `.soctask` (checked when opened: a damaged file is explained and breaks nothing),
  recent files, drag and drop, optional association, and **.rec import** (experimental).
- **Compile to .exe**: a ~1.4 MB executable that plays the recording on any PC, with its
  emergency stop.
- **Editor**: delete, trim start and end, simplify mouse moves, change, scale and insert waits,
  undo.
- Small window with icons and hideable labels, optional always on top, notification area,
  light/dark, Spanish/English on the fly, setup guide, what's new, restart as administrator and
  portable mode. No network.
- Tests: 191 logic tests, 8 UI tests and 3 real record-and-play tests.
