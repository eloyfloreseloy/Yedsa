# 1 · C# y Windows Forms, sin instalar nada

**El problema.** Yedsa tiene que correr en la PC de cualquier local: muchas veces es vieja, sin permisos de administrador y sin nadie que sepa instalar cosas.

**Lo que decidí.** C# con Windows Forms sobre .NET Framework 4, compilado con el `csc.exe` que ya trae Windows (`compilar.bat`). Sin librerías de afuera: el ping, el HTTPS, la consulta al DNS y el `tracert` usan lo que ya trae Windows. Todo lo visual (tema oscuro, esquinas redondeadas, la barra de título) está dibujado a mano con GDI+.

**Qué implica.**
- Es un solo `.exe` de unos 250 KB que se descomprime y se abre. Compilarlo tampoco necesita Visual Studio.
- Funciona solo en Windows. Para este problema está bien: las PCs de los locales usan Windows.
- El compilador de Windows entiende C# 5 (2012), así que no hay funciones nuevas del lenguaje. Dibujar todo a mano es más código, pero no depende de nada.
