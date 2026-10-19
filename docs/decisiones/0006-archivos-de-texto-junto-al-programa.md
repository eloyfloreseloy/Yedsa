# 6 · Todo en archivos de texto, junto al programa

**El problema.** Las pruebas tienen que poder abrirse y mostrarse sin Yedsa, por ejemplo al proveedor, en Excel o en el Bloc de notas. Y el programa queda encendido meses, así que nada puede crecer sin fin.

**Lo que decidí.**
- Nada de base de datos: `.csv` (con `;` y UTF-8 con BOM, que Excel en español abre bien) y `.txt`, en la misma carpeta que `Yedsa.exe`. Es "portable": se mueve la carpeta y se lleva todo.
- Todo tiene un límite y lo más viejo se borra solo: 7 días o 1 GB de pings, 10.000 filas de historial, 100 capturas y 30 reportes.
- El archivo de pings de cada día se parte al llegar a un millón de filas, porque Excel no abre más de 1.048.576.
- Se escribe cada segundo (si se va la luz, queda todo hasta ese segundo) y los archivos se abren compartidos, para poder mirarlos con Yedsa abierto.
- Si un archivo no se puede escribir (por ejemplo, el historial abierto en Excel), Yedsa avisa: nunca falla en silencio.

**Qué implica.**
- No hay que instalar ni mantener nada, y cualquiera puede revisar los datos.
- Buscar en meses de historial es más lento que con una base de datos, pero el historial que importa es el de los últimos días.
