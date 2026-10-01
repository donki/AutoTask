<#
.SYNOPSIS
    Entrega de sOC AutoTask: version, pruebas, exe autocontenido + MSIX, OneDrive con LEEME, commit,
    push y release de GitHub.
.DESCRIPTION
    El CHANGELOG se escribe antes a mano (la primera seccion son las notas de la release). Mismo
    guion que el de RC Manager y Lucia (tools\entregar.ps1 alli): si se toca uno, mirar el otro.
    Si la aplicacion de OneDrive estaba abierta, al acabar se vuelve a abrir la nueva y se
    comprueba que es la que corre (constitucion general 8.3).
.EXAMPLE
    .\tools\entregar.ps1 -Version 2026.10.01.0 -Mensaje "2026.10.01.0: primera version"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Mensaje,
    [switch] $SinRelease
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
[IO.Directory]::SetCurrentDirectory($raiz)

# 1. Version (unica, en Directory.Build.props). El MSIX usa 2026.10.10.0 (lo calcula su script).
$p = 'Directory.Build.props'; $x = Get-Content $p -Raw
$x = [regex]::Replace($x, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($p, $x)
$partes = $Version.Split('.') | ForEach-Object { [int]$_ }
$msixVer = '{0}.{1}.{2}{3}.0' -f $partes[0], $partes[1], $partes[2], $partes[3]

# 2. Banco de pruebas en verde (constitucion general 8.6).
dotnet build tests\AutoTask.Tests -m:1 -nodeReuse:false -v q --nologo | Out-Null
dotnet test tests\AutoTask.Tests --no-build 2>&1 | Select-String 'Passed!|Failed!'
if ($LASTEXITCODE -ne 0) { throw 'Las pruebas no pasan: no se entrega.' }

# 3. Exe + MSIX (el script compila antes el reproductor AOT).
.\tools\empaquetar-msix.ps1 2>&1 | Select-String 'Paquete:|Reproductor:|error|fall'
$exe = 'src\sOCAutoTask\bin\Release\net10.0-windows\win-x64\publish\sOCAutoTask.exe'
$msix = 'bin\sOCAutoTask.msix'
if (-not (Test-Path $exe)) { throw 'No hay exe publicado.' }

# 4. OneDrive (constitucion general 8). Si la de OneDrive esta abierta se cierra y se reabre al final.
$d = 'C:\ID\OneDrive\AutoTask'
New-Item -ItemType Directory -Force $d | Out-Null
$abierta = Get-Process sOCAutoTask -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$d*" }
$abierta | Stop-Process -Force
Start-Sleep -Milliseconds 500
Get-ChildItem $d -Filter *.msix | Remove-Item -Force
Copy-Item $exe $d -Force
Copy-Item $msix "$d\sOCAutoTask-$msixVer.msix" -Force
$leeme = @'
sOC AutoTask
============

Version @@VERSION@@

Que es
------
Un grabador y reproductor de macros de raton y teclado para Windows: pulsas Grabar, haces lo que
quieras, pulsas Parar, y Reproducir lo repite igual, mas rapido o en bucle. Tambien convierte una
grabacion en un .exe que la reproduce en cualquier PC.

Atajos: Ctrl+Alt+Mayus+R graba y para; Ctrl+Alt+Mayus+P reproduce y para. Parada de emergencia:
Pausa, Bloq Despl o mantener Esc un segundo.

Que hay en esta carpeta
-----------------------
- sOCAutoTask.exe: la aplicacion. No necesita instalacion: copialo donde quieras y abrelo. Los
  ajustes van a %LOCALAPPDATA%\sOCAutoTask; si pones un fichero vacio llamado portable.ini al lado
  del exe, van a esta misma carpeta (modo portatil).
- sOCAutoTask-@@MSIX@@.msix: el paquete de instalacion para la Microsoft Store. Sin firmar, Windows
  no lo instala; para usar la aplicacion basta con el exe.
- LEEME.txt: esto.

Windows puede avisar de que el editor es desconocido ("Windows protegio su PC"): pulsa "Mas
informacion" y "Ejecutar de todas formas". Los .exe que compiles con AutoTask pueden hacer saltar
un antivirus (son nuevos, sin firma y mueven el raton): son seguros, no usan la red ni se instalan.

Sin red, sin cuenta, sin anuncios. Software libre bajo licencia MIT. En español y en ingles, claro y
oscuro.
'@
$leeme = $leeme.Replace('@@VERSION@@', $Version).Replace('@@MSIX@@', $msixVer)
[IO.File]::WriteAllText("$d\LEEME.txt", $leeme, (New-Object Text.UTF8Encoding $false))
'OneDrive: ' + ((Get-ChildItem $d | ForEach-Object { $_.Name }) -join ', ')

# 5. Git y release.
git add -A
git commit -q -m $Mensaje
git push -q origin HEAD 2>&1 | Select-Object -Last 1
if (-not $SinRelease) {
    python 'D:\sOCProjects\Mobile\Shared\release-github.py' "v$Version" $exe $msix 2>&1 | Select-Object -Last 1
}

# 6. Si estaba abierta, se vuelve a abrir la nueva y se comprueba que corre esa (8.3).
if ($abierta) {
    Start-Process "$d\sOCAutoTask.exe"
    Start-Sleep -Seconds 3
    $corre = Get-Process sOCAutoTask -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$d*" } | Select-Object -First 1
    if ($corre -and $corre.MainModule.FileVersionInfo.ProductVersion -like "$Version*") { "Abierta de nuevo: v$Version" }
    else { Write-Warning 'No se ha podido comprobar que corre la version nueva.' }
}
