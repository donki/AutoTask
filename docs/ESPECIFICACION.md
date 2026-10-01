# sOC AutoTask — Especificación

> Método SDD: este documento se escribe **antes** que el código y manda sobre él. Cada requisito
> lleva un identificador (`RF-…`, `RNF-…`) que citan las pruebas y el código cuando hace falta.
> Arquitectura y formato de fichero: [ARQUITECTURA.md](ARQUITECTURA.md).
>
> **Versión de la especificación:** 2026-10-01 (corresponde a la 2026.10.01.0).

## 1. Qué es

Grabador y reproductor de macros de ratón y teclado para Windows: pulsas **Grabar**, haces lo que
quieras con el ratón y el teclado, pulsas **Parar**, y **Reproducir** lo repite igual, más rápido o
en bucle. Ventana diminuta tipo barra de herramientas, portátil (un solo exe), sin red, sin cuenta.
Se inspira en una utilidad clásica del mismo tipo (*TinyTask*), con todas sus funciones y algunas
más (editor, parada de emergencia, velocidad a medida, formato con validación).

Público: cualquiera que repita a mano la misma secuencia de clics y teclas (rellenar un formulario,
probar una aplicación, tareas repetitivas de oficina, juegos que lo permitan).

## 2. Historias de usuario

| Id | Como… | Quiero… | Para… |
|---|---|---|---|
| HU-01 | usuario | grabar lo que hago con el ratón y el teclado con un botón o un atajo | no tener que programar nada |
| HU-02 | usuario | reproducirlo con un botón o un atajo, y pararlo cuando quiera | repetir la tarea sin hacerla yo |
| HU-03 | usuario | pararlo todo de golpe con una tecla aunque la macro se haya desmadrado | no perder el control del PC |
| HU-04 | usuario | elegir la velocidad (de 0,5× a 100× o la que yo diga) | ir más rápido que a mano |
| HU-05 | usuario | repetirlo N veces o sin fin, con una pausa entre vueltas | dejarlo trabajando |
| HU-06 | usuario | guardar y abrir grabaciones, y tener las recientes a mano | reutilizarlas otro día |
| HU-07 | usuario | convertir una grabación en un .exe que la reproduce al abrirlo | pasarla a otro PC o lanzarla sin la aplicación |
| HU-08 | usuario | ver los eventos y retocarlos (borrar, recortar, simplificar, cambiar esperas) | arreglar una grabación sin repetirla |
| HU-09 | usuario | una ventana pequeña que no estorbe, siempre encima si quiero, y que se esconda junto al reloj | trabajar con ella al lado |
| HU-10 | usuario preocupado por su privacidad | saber qué se graba y poder no grabar el teclado | no guardar contraseñas sin darme cuenta |
| HU-11 | usuario de dos monitores o con la pantalla escalada | que los clics caigan donde los hice | que funcione en mi equipo |
| HU-12 | usuario de otra utilidad parecida | abrir sus grabaciones `.rec` | no empezar de cero |

## 3. Requisitos funcionales

### 3.1 Grabar (HU-01, HU-10, HU-11)

- **RF-01** Se graban, en todo el sistema, los **movimientos** del ratón, la **pulsación y
  suelta** de los cinco botones (izquierdo, derecho, central, X1, X2), la **rueda vertical y
  horizontal** (con su cantidad), y la **pulsación y suelta de cada tecla** (código virtual, código
  de exploración y marca de tecla extendida), con lo que los modificadores quedan grabados como
  teclas propias.
- **RF-02** Cada evento guarda la **espera desde el anterior** en milisegundos. El primero, 0.
- **RF-03** Se graba con ganchos de bajo nivel (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) en un **hilo
  propio con su bucle de mensajes**, nunca en el de la interfaz. El gancho solo copia el evento a
  una cola y devuelve enseguida (Windows quita el gancho si tarda).
- **RF-04** Las coordenadas son **de escritorio virtual en píxeles físicos** (la aplicación es
  *per-monitor DPI aware v2*): sirven con varios monitores, monitores a la izquierda o encima del
  principal (coordenadas negativas) y escalados distintos por monitor.
- **RF-05** Se empieza y se para con el botón **Grabar** o con el atajo global (por defecto
  **Ctrl+Alt+Mayús+R**, configurable). **El atajo no queda en la grabación**, ni los clics hechos
  sobre la propia ventana de AutoTask, ni las sueltas de teclas o botones que ya estaban pulsados
  al empezar, ni las pulsaciones que siguen sin soltar al parar.
- **RF-06** Se ignoran los eventos **inyectados** por programas (incluida la propia reproducción).
- **RF-07** Opción **«Grabar el teclado»** (activada por defecto) y opción **«Grabar los
  movimientos del ratón»** (activada). Con el teclado desactivado solo se graba el ratón.
- **RF-08** Grabar estando ya cargada una grabación sin guardar pide confirmación (§6.8 de la
  constitución general).
- **RF-09** Mientras se graba, la ventana dice «Grabando» con el tiempo y el número de eventos.
- **RF-10** Una grabación sin eventos (se para nada más empezar) no sustituye a la que había.

### 3.2 Reproducir (HU-02, HU-03)

- **RF-11** Reproduce con `SendInput` respetando las esperas divididas por la velocidad. El
  reloj es absoluto (se calcula el instante de cada evento desde el inicio): los retrasos del
  sistema no se acumulan.
- **RF-12** Se empieza con **Reproducir** o con el atajo global (por defecto **Ctrl+Alt+Mayús+P**,
  configurable); **el mismo atajo o el mismo botón paran**.
- **RF-13** **Parada de emergencia** con **Pausa/Inter**, **Bloq Despl** o **Esc mantenida**
  (por defecto 1 s); cada una se activa o desactiva en Ajustes. Solo cuentan las teclas pulsadas de
  verdad, no las que inyecta la reproducción.
- **RF-14** Al parar (por fin, atajo, botón o emergencia) se **sueltan** todas las teclas y botones
  que la reproducción dejó pulsados: nunca queda un Ctrl o un clic «enganchado».
- **RF-15** Antes de empezar se espera a que el usuario **suelte los modificadores** del atajo
  (Ctrl, Alt, Mayús, Windows), para que no se mezclen con lo reproducido.
- **RF-16** **Cuenta atrás previa** opcional (0–10 s, por defecto 0) visible en la ventana.
- **RF-17** Durante la reproducción se ve el **tiempo restante** (de la vuelta y del total si se
  conoce) y la vuelta actual.
- **RF-18** Si la ventana en primer plano pertenece a un programa **elevado** (administrador) y
  AutoTask no lo está, se avisa: Windows descarta en silencio lo que se le envía. Se ofrece
  **«Reiniciar como administrador»**.

### 3.3 Velocidad y repeticiones (HU-04, HU-05)

- **RF-19** Velocidades 0,5×, 1×, 2×, 4×, 10×, 100× y **personalizada** (0,1× a 1000×).
- **RF-20** Repetir **una vez**, **N veces** (1–100 000) o **continuo** hasta parar, con **pausa
  opcional entre repeticiones** (0–3 600 s).
- **RF-21** Velocidad y repeticiones **se guardan con la grabación** y se recuperan al abrirla. Una
  grabación nueva toma las últimas usadas.

### 3.4 Ficheros (HU-06, HU-12)

- **RF-22** Formato propio **`.soctask`** (binario versionado con cabecera, compresión y suma de
  comprobación; descrito en ARQUITECTURA §4). Se escribe y se lee **por flujo**, sin copias
  intermedias de toda la grabación.
- **RF-23** Al abrir se valida: firma, versión, longitud, número de eventos, CRC y que cada evento
  tenga valores posibles. Un fichero malo **no rompe nada** y se explica en el idioma del usuario
  con la razón y qué hacer (§6.9).
- **RF-24** Guardar escribe primero a un temporal y lo cambia por el definitivo: un fallo a mitad
  no deja el fichero anterior destrozado.
- **RF-25** **Recientes**: los 8 últimos, en el menú; los que ya no existen se quitan.
- **RF-26** Se abre un fichero **arrastrándolo** a la ventana o pasándolo en la línea de órdenes
  (`sOCAutoTask.exe fichero.soctask [--play]`).
- **RF-27** **Asociación de `.soctask`** opcional, por usuario (HKCU), activable y desactivable
  desde Ajustes y la guía.
- **RF-28** **Importar `.rec`** (el formato de la utilidad clásica): una lista de registros
  `EVENTMSG` de 20 bytes. Se marca como **experimental**: se valida que el tamaño cuadre, que los
  mensajes sean de ratón o teclado y que los tiempos no retrocedan; si no, se rechaza con un
  mensaje claro. Lo importado se guarda como `.soctask`.

### 3.5 Compilar a EXE (HU-07)

- **RF-29** **Compilar** genera un `.exe` autónomo (no necesita .NET instalado ni AutoTask) que al
  abrirse reproduce la grabación con su velocidad y repeticiones.
- **RF-30** El exe es un **reproductor mínimo** (*stub*, compilado a código nativo) seguido de la
  grabación y una cola con firma y longitud. El reproductor se lee a sí mismo, encuentra la
  grabación, la valida y la reproduce. Tamaño objetivo: < 3 MB.
- **RF-31** El exe generado tiene **parada de emergencia** (las mismas teclas elegidas al
  compilar) y suelta las teclas al parar. Con `--check` solo valida su grabación y sale (código 0
  si está bien), sin reproducir.
- **RF-32** Al compilar se avisa de que **los antivirus pueden desconfiar** de un exe generado que
  mueve el ratón, y la documentación explica por qué es seguro (no tiene red, no se instala, no se
  copia, solo hace lo grabado).
- **RF-33** Si hay firma de código configurada, el exe se firma igual que el resto; hoy el catálogo
  no firma los exe (decisión documentada en ARQUITECTURA §6).

### 3.6 Editor (HU-08)

- **RF-34** Lista de eventos con número, instante, espera, tipo y detalle legible («Clic izquierdo
  en 812, 340», «Tecla A pulsada», «Rueda −120»). Virtualizada: sirve con cientos de miles.
- **RF-35** **Borrar** los eventos seleccionados (su espera pasa al siguiente para no mover el
  resto en el tiempo).
- **RF-36** **Recortar el inicio** (todo lo anterior a la selección) y **el final** (todo lo
  posterior).
- **RF-37** **Simplificar movimientos**: quita los movimientos del ratón que sobran (los que
  quedan en línea con sus vecinos con una tolerancia en píxeles, por defecto 3) **sin tocar** el
  último movimiento antes de un clic ni el tiempo total. Opción **«dejar solo el último movimiento
  antes de cada clic»**.
- **RF-38** **Cambiar la espera** de los seleccionados (valor fijo) y **multiplicar** las esperas
  (p. ej. ×0,5).
- **RF-39** **Insertar una espera** (evento propio de N ms) antes del seleccionado.
- **RF-40** **Deshacer** (varios pasos) y **Aplicar** / **Cancelar**: hasta Aplicar no se toca la
  grabación.

### 3.7 Interfaz (HU-09)

- **RF-41** Ventana pequeña tipo barra con botones de **icono SVG plano** (§6.1-6.2): Abrir,
  Guardar, Grabar, Reproducir, Compilar, Editor, Ajustes y un menú «Más» (recientes, importar
  `.rec`, guía, novedades, reiniciar como administrador, acerca de, salir). Cada botón con
  *tooltip* y **etiqueta** que se puede mostrar u ocultar.
- **RF-42** **Siempre encima** opcional.
- **RF-43** Al minimizar se queda en la **bandeja** (opción, activada): clic para volver; botón
  derecho: Abrir, Grabar, Reproducir, Salir.
- **RF-44** Tema **claro/oscuro** (sistema, claro u oscuro) y **español/inglés** con cambio en
  caliente, sin reiniciar.
- **RF-45** **Guía de configuración** (§6.10) que sale sola en el primer arranque y luego desde el
  menú, con el estado de cada paso comprobado al momento.
- **RF-46** **Novedades** (§6.7) de las cinco últimas versiones; salen solas al abrir una versión
  nueva por primera vez (no en la primera instalación, donde sale la guía).
- **RF-47** **Acerca de** canónico con versión, contacto, idioma, privacidad, licencias y aviso
  legal.
- **RF-48** **Portátil**: si junto al exe hay un `portable.ini`, los ajustes, recientes y registro
  van a esa carpeta; si no, a `%LOCALAPPDATA%\sOCAutoTask`.
- **RF-49** **Instancia única** (§8.3): una segunda copia de la misma versión pide a la primera que
  se enseñe (y le pasa el fichero a abrir) y espera su acuse; una versión nueva cierra la vieja.
- **RF-50** Cerrar con una grabación sin guardar pregunta.

### 3.8 Seguridad y honestidad

- **RF-51** Sin red: la aplicación no abre ninguna conexión (el resto del catálogo de escritorio
  tampoco comprueba versiones por red; si se añade, será la misma para todas).
- **RF-52** Avisos visibles: una grabación guarda lo que se teclea (contraseñas incluidas); el
  exe compilado puede alarmar al antivirus; los programas elevados y algunos juegos no aceptan
  entrada simulada.
- **RF-53** Errores inesperados: gestor global (§6.12), registro con la traza y aviso localizado;
  la aplicación no se cierra.

## 4. Requisitos no funcionales

- **RNF-01** WPF sobre .NET 10, Windows 10 2004+ x64. Exe autocontenido de un solo fichero y MSIX.
- **RNF-02** Licencia MIT; ninguna dependencia de terceros en la aplicación ni en el reproductor
  (solo .NET y Windows). Pruebas: xUnit, coverlet, FlaUI (Apache 2.0 / MIT).
- **RNF-03** Gancho: < 1 ms por evento; la cola no se bloquea.
- **RNF-04** Precisión de reproducción a 1×: error medio < 5 ms por evento en un PC normal.
- **RNF-05** Memoria: 1 h de grabación continua (~500 000 eventos) cabe en < 50 MB.
- **RNF-06** Textos solo a través de `Loc` (es/en con las mismas claves, probado).
- **RNF-07** Modo aislado `SOC_SANDBOX` solo en Debug: datos en carpeta temporal, sin instancia
  única, sin bandeja, sin atajos globales, sin asociación de ficheros y ventanas sin activarse.

## 5. Casos límite

| Caso | Comportamiento |
|---|---|
| Se pulsa el atajo de grabar mientras se reproduce (o al revés) | Se ignora; un estado cada vez. |
| El atajo elegido ya lo usa otro programa | Aviso al guardar Ajustes y en la guía (paso pendiente); el botón sigue funcionando. |
| Atajo sin modificadores o igual al otro atajo | No se acepta; se explica. |
| Velocidad 1000× con miles de eventos | Ráfaga sin esperas; la interfaz sigue respondiendo. |
| Esperas enormes (horas) en una grabación | Se respetan; la parada sigue funcionando durante la espera. |
| Monitor desconectado desde que se grabó | Se avisa al reproducir («la pantalla ha cambiado»), y los puntos fuera se llevan al borde. |
| Fichero `.soctask` de una versión futura | «Hecho con una versión más nueva: actualiza AutoTask». |
| Fichero truncado o modificado | «Está incompleto o dañado»; no se carga nada. |
| `.rec` con tamaño no múltiplo de 20 o mensajes raros | «No parece una grabación .rec válida». |
| Grabación con 0 eventos | No se guarda ni se compila ni se reproduce; botones desactivados. |
| Parar a mitad con teclas pulsadas | RF-14: se sueltan. |
| Se cierra la aplicación reproduciendo | Se para (y se sueltan las teclas) antes de salir. |
| Esc mantenida dentro de la grabación | No para: los eventos inyectados no cuentan (RF-13). |
| Ventana elevada en primer plano | RF-18. |
| Exe compilado copiado a otro PC con otra resolución | Reproduce igual; los puntos fuera de pantalla se llevan al borde. |
| Disco lleno o sin permiso al guardar o compilar | Aviso con la razón; el fichero anterior intacto. |

## 6. Criterios de aceptación

1. **CA-01** (RF-01..06) Con una ventana de prueba propia: grabar un clic en una casilla, escribir
   «hola» y tres clics en un botón; tras vaciar la casilla y reproducir, la casilla dice «hola» y el
   contador vale 3. Probado de verdad (prueba E2E con eventos inyectados y grabación que admite
   inyectados solo en esa prueba).
2. **CA-02** (RF-04) Un clic grabado en el monitor de la izquierda (x < 0) se reproduce en el
   mismo píxel (±1).
3. **CA-03** (RF-05) Grabar y parar con el atajo no deja ninguna pulsación de Ctrl, Alt, Mayús ni
   R en la grabación (prueba de lógica).
4. **CA-04** (RF-11, RF-19, RF-20) A 2× una grabación de 1 s dura 0,5 s; 3 vueltas con 200 ms de
   pausa duran 3 × d + 2 × 200 ms (reloj falso).
5. **CA-05** (RF-13, RF-14) La parada de emergencia detiene la reproducción y suelta las teclas
   pulsadas (doble de `SendInput`).
6. **CA-06** (RF-22..24) Guardar y abrir devuelve exactamente los mismos eventos y opciones;
   cualquier byte cambiado da «dañado»; un fichero de otra versión da su mensaje.
7. **CA-07** (RF-29..31) El exe compilado contiene la grabación, el reproductor la encuentra con
   `--check` y, contra la ventana de prueba, reproduce el texto y los clics.
8. **CA-08** (RF-35..40) Cada operación del editor conserva el tiempo total cuando debe y se
   deshace.
9. **CA-09** (RF-44) es y en tienen las mismas claves y ninguna vacía; el cambio de idioma se ve en
   la ventana sin reiniciar (prueba de interfaz).
10. **CA-10** (RNF-07) Las pruebas de interfaz arrancan en modo aislado y nunca reproducen sobre el
    escritorio real.
