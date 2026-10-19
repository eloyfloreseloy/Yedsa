# 5 · ¿Corte del local o del proveedor?

**El problema.** Si el reporte para el proveedor lleva todos los cortes, incluso los que empezaron en un router del local, se cae ante el primer reclamo. Lo mismo pasa con los "cortes" de cuando alguien está configurando los routers.

**Lo que decidí.**
- Cada equipo tiene un **qué es**: de mi red, PC que se apaga, fuera del camino, del proveedor o destino en internet. Si el corte empieza en uno del local, es **CORTE LOCAL**. Si empieza en uno del proveedor o en internet, es **CORTE**.
- Si en el minuto anterior un equipo del local venía perdiendo pings (3 o más), un corte que "parece" del proveedor se anota como del local: cuando un router pierde uno de cada cuatro pings, a veces justo se pierden tres seguidos de Google mientras el router alcanza a contestar.
- **Modo mantenimiento**: mientras alguien configura la red, todo se anota como mantenimiento. Siempre tiene hora de fin y se apaga solo, porque si dependiera de que alguien lo apague, un olvido escondería cortes de verdad.

**Qué implica.**
- El reporte "para el proveedor" lleva solo lo que es suyo.
- Esta regla la probé con los cortes reales de varios días de un local: los cortes de verdad del proveedor siguieron siendo suyos y los causados por un router inestable pasaron al local.
- Ante la duda, un corte se cuenta como del local. Prefiero no reclamar algo que no es del proveedor.
