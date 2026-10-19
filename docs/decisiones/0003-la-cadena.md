# 3 · La cadena: el primero que deja de responder

**El problema.** Saber *que* se cortó no alcanza: hay que saber *dónde*, porque eso dice si el problema es del local o del proveedor.

**Lo que decidí.** Los equipos van en una lista ordenada como va el internet, desde la PC hacia afuera. Para llegar a un equipo, el tráfico pasa por todos los de arriba. Entonces:

- El **primer equipo que deja de responder** marca dónde empieza el problema, y su mensaje es el diagnóstico.
- Si un equipo no responde pero **otro de más abajo sí**, el internet pasó por él: no fue un corte, solo no contestó pings (los routers lo hacen cuando están ocupados). Se anota como **NO FUE CORTE** y no se avisa por Telegram.
- Si uno de esos queda "callado" más de 5 minutos, se da por apagado para que no deje un corte abierto para siempre. Si sigue así 30 minutos, se avisa, por si cambió de IP.

**Qué implica.**
- El orden importa. Para no tener que armarlo a mano está **Detectar mi red** (un `tracert`), y el editor no deja guardar un orden imposible.
- Lo que no está en el camino (otra red, una impresora) se marca como **fuera del camino**: se vigila, pero no prueba nada sobre los demás.
