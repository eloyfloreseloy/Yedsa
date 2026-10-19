# 7 · La hora de internet

**El problema.** Todo lo que se anota lleva hora. Si la PC tiene la hora mal, un corte "a las 12:49" no coincide con nada en los registros del proveedor y la prueba no sirve. Y si alguien mueve el reloj con Yedsa abierto, las horas saltan.

**Lo que decidí.**
- Cada minuto comparo la hora de la PC con la de internet. No agregué ningún servicio: las respuestas de la consulta de IP pública ya traen la hora del servidor (encabezado `Date`).
- Si la diferencia pasa de 2 minutos, Yedsa avisa y anota todo con la hora de internet. El reloj de Windows no lo toco: eso le corresponde a quien maneja la PC.
- Lo que mide "cada cuánto" (cada minuto, cada 30 minutos...) usa el contador de Windows (`GetTickCount64`), que no depende del reloj. Si alguien mueve la hora, el salto se compensa.

**Qué implica.**
- Las horas de las pruebas son confiables aunque la PC tenga la hora mal.
- La zona horaria no se puede comprobar así, porque la hora de internet viene en hora universal. Por eso el reporte dice arriba la zona horaria de la PC.
