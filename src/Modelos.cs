// Datos con los que trabaja Yedsa: cada equipo vigilado, cada corte y cada terminal.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Yedsa
{
    enum Muestra : byte { Ok, Lento, Perdido }

    // Qué es cada equipo (columna "Qué es" de Equipos). Decide cómo se trata cuando deja de responder
    // y de quién es el corte: del local o del proveedor.
    enum Rol { Red, Pc, Fuera, Proveedor, Internet }

    static class Roles
    {
        public static readonly Rol[] Todos = { Rol.Red, Rol.Pc, Rol.Fuera, Rol.Proveedor, Rol.Internet };

        // Como se guarda en equipos.txt.
        public static string Clave(Rol r)
        {
            switch (r)
            {
                case Rol.Pc: return "pc";
                case Rol.Fuera: return "fuera";
                case Rol.Proveedor: return "proveedor";
                case Rol.Internet: return "internet";
                default: return "red";
            }
        }

        public static Rol? DeClave(string clave)
        {
            foreach (var r in Todos) if (Clave(r) == clave) return r;
            return null;
        }

        // Como se ve en la lista desplegable del editor.
        public static string Nombre(Rol r)
        {
            switch (r)
            {
                case Rol.Pc: return "PC o servidor (se apaga)";
                case Rol.Fuera: return "Fuera del camino";
                case Rol.Proveedor: return "Equipo del proveedor";
                case Rol.Internet: return "Destino en internet";
                default: return "Equipo de mi red";
            }
        }

        // Explicación que sale abajo en el editor al estar en la columna "Qué es".
        public const string AYUDA =
            "Qué es:  Equipo de mi red = tus routers, MikroTik o switch (si el corte empieza ahí, es del local).   " +
            "PC o servidor = se apaga a propósito; apagarse no es corte.   Fuera del camino = el internet no pasa por él " +
            "(otra red, una impresora); si falla, se avisa pero nunca es corte.   Equipo del proveedor = si el corte empieza ahí, " +
            "es del proveedor.   Destino en internet = los del final (Google, Cloudflare); solo es corte si TODOS dejan de responder.";
    }

    class Equipo
    {
        public string Nombre, Ip, Mensaje, Diagnostico; // Mensaje: el escrito por el usuario (puede estar vacío)
        public int Indice;
        public Rol Rol;
        public bool EnCurso, Caido;
        public bool Apagado;   // lleva mucho sin responder mientras los de después sí: no cuenta para los cortes hasta que vuelva
        public bool SeApaga { get { return Rol == Rol.Pc; } }        // apagarse no es un corte
        public bool EsInternet { get { return Rol == Rol.Internet; } }   // con varios, solo es corte si TODOS dejan de responder
        public bool EsFuera { get { return Rol == Rol.Fuera; } }      // el internet no pasa por él: nunca es parte de un corte
        public Queue<KeyValuePair<DateTime, long>> Latencias = new Queue<KeyValuePair<DateTime, long>>();   // ms del último minuto
        public bool AvisadoLargo;   // ya se avisó que lleva mucho sin responder (se vuelve a avisar si vuelve y se cae otra vez)
        public int FallosSeguidos, Cortes;
        public DateTime PrimerFallo;
        public long Enviados;
        public long UltimoMs = -1;
        public List<Muestra> Historial = new List<Muestra>();
        // Pings del último minuto que no son parte de un corte: sirven para notar si la red está inestable.
        public Queue<PingReciente> Recientes = new Queue<PingReciente>();
        public int PerdidosRecientes;
    }

    struct PingReciente
    {
        public DateTime Hora;
        public bool Perdido;
    }

    // Rato en que se perdieron pings sueltos sin llegar a ser un corte (fila INESTABLE del historial).
    class Inestabilidad
    {
        public DateTime Inicio, Fin, UltimaVez;   // Fin: último ping perdido; UltimaVez: último segundo en que seguía inestable
        public bool EnMantenimiento;
        public Dictionary<Equipo, int> Enviados = new Dictionary<Equipo, int>();
        public Dictionary<Equipo, int> Perdidos = new Dictionary<Equipo, int>();
        public Dictionary<Equipo, int> DespuesDelFin = new Dictionary<Equipo, int>();   // pings ok después del último perdido

        public void Sumar(Equipo e, DateTime hora, bool perdido)
        {
            int n;
            Enviados.TryGetValue(e, out n); Enviados[e] = n + 1;
            if (perdido)
            {
                Perdidos.TryGetValue(e, out n); Perdidos[e] = n + 1;
                if (hora > Fin)
                {
                    // El rato se alarga hasta este ping: lo que se contó "después del fin" ya quedó dentro.
                    Fin = hora;
                    DespuesDelFin.Clear();
                }
            }
            else if (hora > Fin)
            {
                DespuesDelFin.TryGetValue(e, out n); DespuesDelFin[e] = n + 1;
            }
        }

        // Porcentaje de pings perdidos de un equipo entre Inicio y Fin.
        public double Porcentaje(Equipo e)
        {
            int env, per, despues;
            Enviados.TryGetValue(e, out env);
            Perdidos.TryGetValue(e, out per);
            DespuesDelFin.TryGetValue(e, out despues);
            env -= despues;
            return env <= 0 ? 0 : 100.0 * per / env;
        }
    }

    // Tipos de fila del historial (primera columna de incidentes.csv), en el orden en que se ofrecen
    // para mostrar u ocultar en la pantalla y en el reporte.
    static class Tipos
    {
        // CORTE: empezó en un equipo del proveedor o en internet. CORTE LOCAL: empezó en un equipo del local.
        public const string Corte = "CORTE", CorteLocal = "CORTE LOCAL", NoFueCorte = "NO FUE CORTE", Inestable = "INESTABLE",
                            SinRespuesta = "SIN RESPUESTA", CambioIp = "CAMBIO IP", Anotado = "ANOTADO", Mantenimiento = "MANTENIMIENTO",
                            SinVigilar = "SIN VIGILAR", Camino = "CAMBIO DE CAMINO", Dns = "DNS", Lento = "LENTO",
                            HoraMal = "HORA MAL";
        public static readonly string[] Todos = { Corte, CorteLocal, NoFueCorte, Inestable, SinRespuesta, CambioIp, Anotado, Mantenimiento,
                                                   SinVigilar, Camino, Dns, Lento, HoraMal };
        // Lo que le sirve al proveedor como prueba. Lo demás muestra cosas de la red del local.
        public static readonly string[] ParaProveedor = { Corte, CambioIp, Anotado };

        // Para las casillas: "qué mostrar".
        public static string Plural(string tipo)
        {
            switch (tipo)
            {
                case Corte: return "Cortes del proveedor";
                case CorteLocal: return "Cortes del local";
                case NoFueCorte: return "Avisos que no fueron corte";
                case Inestable: return "Red inestable";
                case SinRespuesta: return "Equipos sin respuesta";
                case CambioIp: return "Cambios de IP pública";
                case Anotado: return "Caídas anotadas";
                case Mantenimiento: return "Mantenimiento";
                case SinVigilar: return "Ratos sin vigilar";
                case Camino: return "Cambios de camino";
                case Dns: return "Fallas de DNS";
                case Lento: return "Internet lento";
                case HoraMal: return "Hora de la PC mal";
                default: return tipo;
            }
        }

        // Para el título de la ventanita de detalle.
        public static string Titulo(string tipo)
        {
            switch (tipo)
            {
                case Corte: return "Corte";
                case CorteLocal: return "Corte en el local";
                case NoFueCorte: return "No fue un corte";
                case Inestable: return "Red inestable";
                case SinRespuesta: return "Equipo sin respuesta";
                case CambioIp: return "Cambio de IP pública";
                case Mantenimiento: return "Mantenimiento";
                case SinVigilar: return "Yedsa no estuvo vigilando";
                case Camino: return "Cambió el camino hacia internet";
                case Dns: return "Falla de DNS";
                case Lento: return "Internet lento";
                case HoraMal: return "Hora de la PC";
                default: return "Caída anotada";
            }
        }

        public static Color Color(string tipo)
        {
            switch (tipo)
            {
                case Corte: return Tema.RojoTexto;
                case CorteLocal: return Tema.Rosa;
                case NoFueCorte: return Tema.TextoSec;
                case Mantenimiento: return Tema.Cian;
                case SinVigilar: return Tema.TextoTenue;
                case Camino: return Tema.Morado;
                case Dns: return Tema.Lima;
                case Lento: return Tema.Dorado;
                case HoraMal: return Tema.Rojo;
                case Inestable: return Tema.Naranja;
                case SinRespuesta: return Tema.Amarillo;
                case CambioIp: return Tema.Morado;
                default: return Tema.Azul;
            }
        }

        // Clase CSS del reporte (los colores están en el <style> del reporte).
        public static string Clase(string tipo)
        {
            switch (tipo)
            {
                case Corte: return "corte";
                case CorteLocal: return "local";
                case NoFueCorte: return "nofue";
                case Mantenimiento: return "mant";
                case SinVigilar: return "hueco";
                case Camino: return "ip";
                case Dns: return "dns";
                case Lento: return "lento";
                case HoraMal: return "hora";
                case Inestable: return "inest";
                case SinRespuesta: return "aus";
                case CambioIp: return "ip";
                default: return "anot";
            }
        }

        // Lista guardada en ajustes.txt ("CORTE,CAMBIO IP,...") <-> conjunto de tipos.
        public static HashSet<string> Leer(string texto, string[] porDefecto)
        {
            if (string.IsNullOrEmpty(texto)) return new HashSet<string>(porDefecto);
            return new HashSet<string>(texto.Split(',').Where(x => Todos.Contains(x)));
        }

        public static string Escribir(IEnumerable<string> tipos)
        {
            return string.Join(",", Todos.Where(x => tipos.Contains(x)));
        }

        // Una fila que pasó durante un mantenimiento queda como MANTENIMIENTO, con el tipo que habría tenido
        // entre corchetes al inicio de "qué pasó" ("[CORTE] Proveedor (entrada) (35 s)"), para poder deshacerlo.
        public static string[] ComoMantenimiento(string[] campos)
        {
            var c = (string[])campos.Clone();
            if (c[0] == Mantenimiento) return c;
            c[4] = "[" + c[0] + "] " + c[4];
            c[0] = Mantenimiento;
            return c;
        }

        // Lo contrario: devuelve el tipo original. null si la fila no venía de otro tipo.
        public static string[] SinMantenimiento(string[] campos)
        {
            if (campos[0] != Mantenimiento || !campos[4].StartsWith("[")) return null;
            int fin = campos[4].IndexOf("] ");
            if (fin < 0 || !Todos.Contains(campos[4].Substring(1, fin - 1))) return null;
            var c = (string[])campos.Clone();
            c[0] = campos[4].Substring(1, fin - 1);
            c[4] = campos[4].Substring(fin + 2);
            return c;
        }
    }

    class Incidente
    {
        public DateTime Inicio;
        public HashSet<Equipo> Caidos = new HashSet<Equipo>();
        public bool AvisadoTelegram;
        public bool EnMantenimiento;   // pasó (aunque sea en parte) durante un mantenimiento: no cuenta para el proveedor
        public Dictionary<Equipo, double> Duraciones = new Dictionary<Equipo, double>();
        public List<CapturaHecha> Capturas = new List<CapturaHecha>();   // las del inicio: al cerrar se renombran según lo que fue
    }

    // Una imagen guardada por una captura automática ("inicio" o "fin"; "panel" o "terminales").
    class CapturaHecha
    {
        public string Ruta, Momento, Parte;
    }

    // Un "ping -t" real de Windows cuya salida se muestra dentro del programa.
    class Terminal
    {
        public Equipo Equipo;
        public Label Titulo;
        public Tarjeta Marco;
        public TextBox Caja;
        public Process Proc;
        public int Lineas;
        public StreamWriter Archivo;   // bloc de notas del día de este equipo
        public string ArchivoFecha;
    }
}
