# Genera recursos\yedsa.ico (el ícono del .exe) a partir del logo de src\Interfaz\Logo.cs.
# Solo hace falta correrlo si cambia el logo:  clic derecho en este archivo -> "Ejecutar con PowerShell".
$raiz = Split-Path $PSScriptRoot
Add-Type -Path "$raiz\src\Interfaz\Logo.cs" -ReferencedAssemblies System.Drawing
$verde = [System.Drawing.Color]::FromArgb(99, 186, 72)
[Yedsa.Logo]::GuardarIco("$raiz\recursos\yedsa.ico", $verde)
Write-Host "Listo: recursos\yedsa.ico"
