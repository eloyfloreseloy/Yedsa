@echo off
rem Crea programa\Yedsa.exe con el compilador de C# que ya viene con Windows (.NET Framework 4).
rem No hace falta instalar nada: solo hacer doble clic en este archivo.
cd /d "%~dp0"
if not exist programa mkdir programa
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /codepage:65001 /optimize+ ^
  /out:programa\Yedsa.exe /win32icon:recursos\yedsa.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll ^
  /recurse:src\*.cs
if %errorlevel%==0 (echo Listo: programa\Yedsa.exe) else (echo Hubo errores al compilar.)
pause
