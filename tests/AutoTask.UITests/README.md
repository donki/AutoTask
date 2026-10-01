# Pruebas de interfaz (FlaUI) y prueba real

## Pruebas de interfaz (8, unos 20 s)

Lanzan el **exe Debug** en modo aislado y lo manejan por UI Automation (patrones Invoke, Value,
SelectionItem: sin mover el ratón ni teclear): arranque y barra, abrir por línea de órdenes y
editar (simplificar, borrar, deshacer, aplicar), que en modo aislado no se graba ni se reproduce
(el cursor no se mueve), ajustes con validación, cambio de idioma en caliente desde «Acerca de»,
guía de 6 pasos y novedades, guardar como (diálogo común de Windows), y un fichero dañado
explicado en castellano.

```powershell
dotnet build src\sOCAutoTask -c Debug -m:1 -nodeReuse:false
dotnet build tests\AutoTask.UITests -m:1 -nodeReuse:false
dotnet test tests\AutoTask.UITests --no-build --filter "FullyQualifiedName~MainWindowTests"
```

Otro exe: variable `AUTOTASK_EXE` (tiene que ser Debug: en Release no hay modo aislado).

### Modo aislado (`SOC_SANDBOX`)

Cada prueba usa `SOC_SANDBOX=%TEMP%\sOCAutoTask-uitests\<prueba>-<guid>` (se borra al acabar) con
unos ajustes de partida (idioma español, guía vista). Solo en Debug, la aplicación entonces guarda
todo en esa carpeta, **no pone ganchos, no registra atajos globales, no crea icono en la bandeja,
no toca el registro (asociación), no tiene instancia única**, no activa sus ventanas y pone
`[SOC_SANDBOX]` en el título. **Grabar y Reproducir solo dejan un aviso**: una prueba de interfaz
nunca mueve el ratón de quien trabaja.

Capturas de cada paso (PrintWindow, sin robar el foco) en `artifacts/` (fuera de git).

## Prueba real (3, unos 15 s) — `AUTOTASK_E2E=1`

`EndToEndTests` graba con los ganchos de verdad y reproduce con `SendInput` de verdad contra una
ventana de prueba propia (`AutoTask.TestTarget`: una casilla y un botón contador, siempre encima):

1. Clic en la casilla, «hola» y tres clics en el botón (simulados con `SendInput`; el grabador
   acepta inyectados solo en esta prueba), se vacía la casilla y se reproduce a 2×: la casilla
   vuelve a decir «hola» y el contador llega a 6.
2. Clic en el monitor de la izquierda (x negativa): el cursor cae en el mismo píxel y el botón
   cuenta el clic. Solo si hay un monitor a la izquierda.
3. Lo mismo compilado a exe con el reproductor AOT: el exe reproduce el texto y los clics.

**Mueve el ratón y teclea.** Antes de cada ráfaga espera a que el PC lleve 4 s sin usarse (como
mucho un minuto), comprueba que la ventana de prueba está en primer plano antes de teclear, y al
acabar devuelve el ratón a donde estaba y cierra la ventana de prueba.

```powershell
dotnet build tests\AutoTask.UITests\AutoTask.TestTarget -m:1 -nodeReuse:false
.\tools\compilar-reproductor.ps1
$env:AUTOTASK_E2E = '1'; dotnet test tests\AutoTask.UITests --no-build --filter "FullyQualifiedName~EndToEndTests"
```
