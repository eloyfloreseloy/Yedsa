# Arquitectura

Cómo está armado Yedsa por dentro: qué hace cada parte y cómo se conectan. El detalle de cada regla está en el código, que tiene comentarios explicando el *porqué*. Las decisiones de diseño importantes están en [decisiones/](decisiones/).

## Las piezas

```
                 cada segundo
  ┌────────┐   ┌─────────────┐   resultado de cada ping   ┌──────────────────┐
  │ Timer  │──▶│   Pings     │──────────────────────────▶│  Detección       │
  └────────┘   │ (uno por    │                            │  cortes, no fue  │
               │  equipo)    │                            │  corte, inestable│
               └─────────────┘                            │  lento, DNS...   │
                                                          └────────┬─────────┘
  ┌──────────────┐  ┌────────────┐  ┌──────────────┐               │ filas
  │ ping.exe -t  │  │ IP pública │  │ tracert      │               ▼
  │ (terminales) │  │ y hora de  │  │ (camino)     │      ┌──────────────────┐
  └──────┬───────┘  │ internet   │  └──────┬───────┘      │  Historial       │──▶ pantalla
         │          └─────┬──────┘         │              │  incidentes.csv  │──▶ reporte HTML
         ▼                ▼                ▼              └────────┬─────────┘
   un .txt por equipo   avisos        avisos                       ▼
                                                              Telegram, globitos
```

- **Pings.** Cada segundo, `Tick` le hace un ping (`SendPingAsync`) a cada equipo que no tenga uno en curso. Cada respuesta pasa por `Resultado`, que lleva la cuenta de cada equipo: fallos seguidos, si está caído o apagado, sus pings del último minuto y sus milisegundos.
- **Detección.** Con 3 fallos seguidos un equipo queda caído y se abre (o se agranda) el corte en curso. El corte se cierra cuando todos vuelven a responder. Al cerrarse se decide qué fue: corte del proveedor, del local, "no fue corte" o mantenimiento. Lo que no es un corte (red inestable, internet lento, falla de DNS) se lleva aparte, como un "rato" con inicio y fin.
- **Historial.** Todo lo que pasa termina en una fila de `incidentes.csv`. La misma fila se ve en la pantalla, va al reporte y, si corresponde, a Telegram.
- **Terminales.** Además de los pings internos, cada equipo tiene un `ping.exe -t` real, cuya salida se muestra en la pantalla y se guarda en un `.txt` por día. Sirven como prueba "de Windows", para capturas. Si Yedsa se cierra, incluso a la fuerza, un *Job* de Windows cierra esos `ping.exe`.
- **Capturas automáticas.** Al empezar un corte se programa una captura para 10 segundos después, y otra se toma al terminar (también al terminar la red inestable, internet lento o la falla de DNS). Las imágenes se dibujan con las mismas funciones que la pantalla, sobre una imagen aparte, así que salen igual aunque la ventana esté escondida. Al cerrar el corte, las del inicio se renombran con lo que fue al final (o se borran, si fue algo que no se quería capturar). El nombre del archivo empieza con la hora de inicio y el tipo de la fila, y así el detalle encuentra las suyas.
- **Revisiones de fondo.** Cada minuto se consulta la IP pública: de esas mismas respuestas sale la hora de internet. Cada 30 minutos se hace un `tracert` para ver si cambió el camino, y cada 10 segundos se le pregunta algo al DNS. Todo esto corre fuera de la ventana, para no trabarla.

## Archivos del código

| Archivo | Qué hace |
|---|---|
| `src/Programa.cs` | Arranque: una sola copia por carpeta, tema y `errores.txt`. |
| `src/Modelos.cs` | Los datos: equipo, corte, rato inestable, terminal, qué es cada equipo (`Rol`) y los tipos de fila del historial (`Tipos`, con sus nombres y colores en un solo lugar). |
| `src/Configuracion.cs` | Lee y guarda `equipos.txt` y `ajustes.txt`, y arma los mensajes automáticos. |
| `src/Sistema.cs` | HTTPS, `tracert`, IPs privadas, el `Reloj` de Yedsa, el *Job* de los `ping.exe` y el sonido de la cámara. |
| `src/Telegram.cs` | Avisos al celular, con una cola que reintenta si no hay internet. |
| `src/Ventanas/VentanaPrincipal.cs` | La ventana: panel lateral, tarjetas, divisor, bandeja y cierre. |
| `src/Ventanas/VentanaPrincipal.Monitoreo.cs` | El ciclo de cada segundo, los pings, los cortes, la cadena y la red inestable. |
| `src/Ventanas/VentanaPrincipal.Calidad.cs` | Falla de DNS e internet lento. |
| `src/Ventanas/VentanaPrincipal.Vigilancia.cs` | Ratos sin vigilar, cambio de camino y equipos que llevan mucho sin responder. |
| `src/Ventanas/VentanaPrincipal.Mantenimiento.cs` | Modo mantenimiento y marcar filas como mantenimiento. |
| `src/Ventanas/VentanaPrincipal.Hora.cs` | Compara la hora de la PC con la de internet. |
| `src/Ventanas/VentanaPrincipal.IpPublica.cs` | IP pública (con locales que salen por varias). |
| `src/Ventanas/VentanaPrincipal.Registros.cs` | Archivos por día, historial, límites, reporte y anotar caídas. |
| `src/Ventanas/VentanaPrincipal.Terminales.cs` | Las terminales `ping -t` y "Capturar pantalla". |
| `src/Ventanas/VentanaPrincipal.Capturas.cs` | Las capturas automáticas: cuándo se toman y cómo se dibujan. |
| `src/Ventanas/VentanaPrincipal.Dibujo.cs` | El panel de equipos con su línea de tiempo. |
| `src/Ventanas/EditorEquipos.cs` | La lista de equipos, "Detectar mi red" y la ayuda de cada columna. |
| `src/Ventanas/VentanaTelegram.cs` | Conectar el bot y elegir quién recibe los avisos. |
| `src/Ventanas/Dialogos.cs` | Ventanitas: preguntas, detalle de una fila, elegir varias opciones, reporte y "Acerca de". |
| `src/Interfaz/` | Tema de colores, controles dibujados a mano (botones, casillas, franja, divisor, barra), panel lateral, ventana sin borde y logo. |

`VentanaPrincipal` está repartida en varios archivos (`partial class`) por tema, para que cada uno se pueda leer por separado.

## Lo que guarda

Todo queda junto a `Yedsa.exe`. No se instala nada ni se escribe en ningún otro lugar, salvo la entrada de "Iniciar con Windows" en el registro del usuario.

| Archivo | Qué es | Límite |
|---|---|---|
| `equipos.txt` | La lista de equipos: `Nombre;IP;qué es;Mensaje` | 30 equipos |
| `ajustes.txt` | Opciones, incluido el código del bot de Telegram (no compartir) | — |
| `registros\dias\AAAA-MM-DD\todos_los_pings.csv` | Cada ping de cada equipo. Si un día pasa del millón de filas, sigue en `(parte 2)`, `(parte 3)`... | 7 días o 1 GB en total (nunca el día de hoy) |
| `registros\dias\AAAA-MM-DD\<equipo>.txt` | La salida de su terminal `ping -t` | con su carpeta de día |
| `registros\incidentes.csv` | El historial | 10.000 filas |
| `registros\capturas\`, `registros\reportes\` | Capturas ("Capturar pantalla") y reportes | 100 y 30 |
| `registros\capturas\automaticas\` | Capturas automáticas: `AAAA-MM-DD_HH-MM-SS_<tipo>_<inicio o fin>_<panel o terminales>.png` | 7 días |
| `registros\latido.txt` | Última hora en que vigilaba (para los ratos sin vigilar) | — |
| `errores.txt` | Errores para revisar después | 1 MB |

Los `.csv` usan `;` y UTF-8 con BOM, para que Excel en español los abra bien. Se abren con `FileShare.ReadWrite`, así se pueden mirar mientras Yedsa escribe. Si alguien deja `incidentes.csv` abierto en Excel y Windows no deja escribirlo, las filas esperan en memoria y Yedsa avisa.

## Compilar

`compilar.bat` usa el compilador de C# que trae Windows (`csc.exe` de .NET Framework 4) y deja el programa en `programa\Yedsa.exe`. No hace falta Visual Studio ni descargar nada.
