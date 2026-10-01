<#
.SYNOPSIS
    Compila el reproductor de los exe generados (sOCAutoTaskPlayer, Native AOT) en artifacts\player.
.DESCRIPTION
    La aplicacion lo lleva incrustado (sOCAutoTask.csproj lo recoge de ahi). Native AOT necesita el
    enlazador de C++ de Visual Studio (Build Tools con «Desarrollo para el escritorio con C++»);
    el SDK lo busca con vswhere, que no esta en el PATH: se añade aqui.
.EXAMPLE
    .\tools\compilar-reproductor.ps1
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$installer = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
if (Test-Path $installer) { $env:PATH = "$installer;$env:PATH" }
$salida = Join-Path $raiz 'artifacts\player'
dotnet publish (Join-Path $raiz 'src\sOCAutoTaskPlayer\sOCAutoTaskPlayer.csproj') -c Release -o $salida -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Ha fallado la compilacion del reproductor (Native AOT).' }
Get-ChildItem $salida -Filter *.pdb | Remove-Item -Force
$exe = Join-Path $salida 'sOCAutoTaskPlayer.exe'
"Reproductor: $exe ({0:N0} KB)" -f ((Get-Item $exe).Length / 1KB)
