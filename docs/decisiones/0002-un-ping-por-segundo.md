# 2 · Un ping por segundo y 3 seguidos para un corte

**El problema.** Los cortes que tumban las partidas duran a veces 20 o 30 segundos. Un programa que revisa cada minuto no los ve. Y un solo ping perdido no es un corte: pasa a cada rato en cualquier red.

**Lo que decidí.** Un ping por segundo a cada equipo, con 1 segundo de espera. Un equipo está caído cuando pierde **3 seguidos**. Los pings sueltos perdidos no son corte, pero si se juntan muchos (5 o más en un minuto) se anota un rato de **red inestable**, porque en los juegos eso se siente como lag.

**Qué implica.**
- El corte más corto que se anota dura unos 6 segundos. Uno más corto puede aparecer como red inestable, pero no como corte.
- Son 30 pings por segundo como mucho (30 equipos): no se nota en la red ni en la PC.
- Además de los pings internos, cada equipo tiene su `ping -t` real de Windows, que es lo que se le puede mostrar al proveedor en una captura.
