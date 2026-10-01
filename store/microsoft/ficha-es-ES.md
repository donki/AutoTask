# Descripción de Store — Español (España)

Todo lo de aquí es para pegar tal cual en el formulario de Partner Center. **Sin enviar** (2026-10-01):
falta reservar el nombre «sOC AutoTask» en Partner Center y confirmar la identidad del paquete
(`tools\empaquetar-msix.ps1`, parámetros `-IdentityName` y `-DisplayName`; por defecto
`sOCratic.sOCAutoTask`).

---

## Nombre del producto

```
sOC AutoTask
```

## Descripción

```
sOC AutoTask graba lo que haces con el ratón y el teclado y lo repite cuando quieras: igual, más
rápido o en bucle. Pulsa Grabar, haz la tarea una vez, pulsa Parar y deja que AutoTask la repita.

GRABAR Y REPRODUCIR
• Graba movimientos, clics de todos los botones, la rueda y las teclas, en cualquier programa.
• Atajos globales: Ctrl+Alt+Mayús+R para grabar y Ctrl+Alt+Mayús+P para reproducir (configurables).
• Funciona con varios monitores y con la pantalla escalada.
• Parada de emergencia con Pausa, Bloq Despl o manteniendo Esc: se para al momento y suelta todas
  las teclas.

VELOCIDAD Y REPETICIONES
• De 0,5× a 100× o la velocidad que elijas.
• Una vez, un número de veces o sin fin, con pausa entre vueltas.
• Cuenta atrás opcional antes de empezar y tiempo restante a la vista.

FICHEROS Y EXE
• Guarda y abre grabaciones, con recientes y arrastrar y soltar.
• Convierte una grabación en un .exe pequeño que la reproduce en cualquier PC, sin instalar nada.

EDITOR
• Ve cada evento y retócalo: borra, recorta el inicio o el final, simplifica los movimientos del
  ratón, cambia o inserta esperas, deshaz.

Ventana pequeña que no estorba, siempre encima si quieres y junto al reloj al minimizar. Sin red,
sin cuenta, sin anuncios, sin rastreadores. Software libre bajo licencia MIT, en español y en
inglés, con modo claro y oscuro.
```

## Novedades de esta versión

Se deja **en blanco** en el primer envío.

## Características del producto

```
Graba ratón y teclado en cualquier programa
Reproduce con atajos globales configurables
Parada de emergencia con Pausa, Bloq Despl o Esc
Velocidad de 0,5× a 100× o personalizada
Repetir N veces o sin fin, con pausa
Convierte una grabación en un .exe autónomo
Editor de eventos con deshacer
Varios monitores y pantallas escaladas
Sin red ni cuenta; en español e inglés, claro y oscuro
```

## Palabras clave

```
macro, grabador, automatizar, ratón, teclado, repetir, clic automático, autoclicker, tareas
```

## Categoría

Productividad (o Utilidades y herramientas).

## Capturas de pantalla

Pendientes: se pueden sacar de `tests\AutoTask.UITests\artifacts\` (barra principal, editor,
ajustes, guía) tras una tanda de pruebas de interfaz, o rehacerlas a 1600×900 con datos de
demostración.

## Logotipos

En `logos/` (generados con `tools\logos.py`): icono 300/150/71, caja 1:1 (1080 y 2160) y póster
9:16 (720×1080 y 1440×2160).

## Dependencias de software (política 10.2.4.1)

Ninguna: todo va dentro del paquete (.NET autocontenido) y usa solo API de Windows.

## Notas para la certificación

La aplicación usa ganchos de teclado y ratón de bajo nivel y `SendInput` porque su función es
grabar y reproducir macros, siempre a petición del usuario (botón o atajo) y con aviso visible
mientras graba. No envía nada por la red.

## Declaración de privacidad

La política del catálogo, con el párrafo: «sOC AutoTask no recoge ningún dato ni usa la red. Las
grabaciones se guardan solo donde el usuario las guarde.» Mientras tanto, `PRIVACY.md` del
repositorio.
