# Avisos de terceros — sOC AutoTask

La aplicación y el reproductor de los exe compilados **no usan bibliotecas de terceros**: solo
.NET y las API de Windows.

| Componente | Uso | Licencia | Titular |
|---|---|---|---|
| .NET 10 (runtime, WPF, Native AOT) | Base de la aplicación y del reproductor (va dentro del exe autocontenido). | MIT | .NET Foundation y colaboradores |
| API de Windows (`SetWindowsHookEx`, `SendInput`, `RegisterHotKey`, `Shell_NotifyIcon`, DWM) | Grabar, reproducir, atajos globales, área de notificación, barra de título oscura. | API del sistema operativo | Microsoft |

Solo para las pruebas (no van en lo que se distribuye):

| Componente | Uso | Licencia |
|---|---|---|
| xUnit, xunit.runner.visualstudio | Banco de pruebas | Apache 2.0 |
| coverlet.collector, ReportGenerator | Cobertura | MIT / Apache 2.0 |
| Microsoft.NET.Test.Sdk | Ejecutor de pruebas | MIT |
| FlaUI.UIA3 | Pruebas de interfaz | MIT |

Todo el código propio (`*.cs`, `*.xaml`, iconos SVG, logo) va bajo MIT (`LICENSE`). El formato
`.rec` que se importa es el de otra utilidad de macros; se lee a partir de la estructura
`EVENTMSG` documentada por Microsoft, sin usar código suyo.
