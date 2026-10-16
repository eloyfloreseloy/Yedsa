// Lectura y guardado de equipos.txt (equipos vigilados) y ajustes.txt (opciones del programa).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace Yedsa
{
    // Lee y guarda equipos.txt (Nombre;IP;qué es;Mensaje opcional), junto al .exe.
    static class Config
    {
        public static string Ruta { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "equipos.txt"); } }

        public static List<Equipo> Leer()
        {
            if (!File.Exists(Ruta)) Guardar(PorDefecto());
            var lista = new List<Equipo>();
            foreach (var linea in File.ReadAllLines(Ruta))
            {
                var l = linea.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                if (lista.Count >= VentanaPrincipal.MAX_EQUIPOS) break;   // por si alguien edita equipos.txt a mano
                // Formato: Nombre;IP;qué es (red, pc, fuera, proveedor o internet);Mensaje.
                // "Qué es" se puede omitir (Nombre;IP;Mensaje), por si alguien escribe la lista a mano: queda como "red".
                var p = l.Split(';');
                if (p.Length < 2 || p[0].Trim().Length == 0 || p[1].Trim().Length == 0) continue;
                var e = new Equipo();
                e.Nombre = p[0].Trim();
                e.Ip = p[1].Trim();
                var rol = p.Length > 2 ? Roles.DeClave(p[2].Trim()) : null;
                e.Rol = rol ?? Rol.Red;
                e.Mensaje = string.Join(";", p.Skip(rol == null ? 2 : 3)).Trim();
                e.Indice = lista.Count;
                var anterior = Anterior(lista.Select(x => x.Rol).ToList(), e.Rol);
                e.Diagnostico = e.Mensaje.Length > 0 ? e.Mensaje
                    : MensajeAuto(e.Nombre, e.Rol, anterior < 0 ? null : lista[anterior].Nombre, anterior < 0 ? (Rol?)null : lista[anterior].Rol);
                lista.Add(e);
            }
            return lista;
        }

        public static void Guardar(List<string[]> filas)
        {
            var sb = new StringBuilder();
            sb.Append("# Equipos a vigilar, EN ORDEN desde esta PC hacia internet.\r\n");
            sb.Append("# Se editan desde la opción \"Equipos\" de " + Programa.NOMBRE + ".\r\n");
            sb.Append("# Formato:  Nombre;IP;Qué es (red, pc, fuera, proveedor o internet);Mensaje si el corte empieza en este equipo (opcional)\r\n");
            foreach (var f in filas) sb.Append(string.Join(";", f) + "\r\n");
            File.WriteAllText(Ruta, sb.ToString(), new UTF8Encoding(true));
        }

        // Posición del equipo "anterior" para el mensaje automático (-1 si no hay): el último de arriba,
        // saltando los que están fuera del camino (el internet no pasa por ellos). Para un destino en internet
        // también se saltan los otros destinos: no van uno detrás de otro, son caminos paralelos.
        public static int Anterior(List<Rol> arriba, Rol rol)
        {
            for (int i = arriba.Count - 1; i >= 0; i--)
                if (arriba[i] != Rol.Fuera && !(rol == Rol.Internet && arriba[i] == Rol.Internet)) return i;
            return -1;
        }

        // El mensaje que se usa cuando el usuario no escribe uno.
        public static string MensajeAuto(string nombre, Rol rol, string anterior, Rol? rolAnterior)
        {
            switch (rol)
            {
                case Rol.Internet:
                    return (anterior == null ? "Ningún destino en internet responde" : anterior + " responde, pero ningún destino en internet") +
                           ": el problema está en la salida a internet del PROVEEDOR.";
                case Rol.Fuera:
                    // No está en la cadena: el equipo de arriba en la lista no tiene por qué estar conectado a él.
                    return nombre + " no responde: puede estar apagado, fallando o sin cable. El internet no pasa por él, así que no es un corte.";
                case Rol.Proveedor:
                    if (rolAnterior == Rol.Proveedor)
                        return anterior + " responde pero " + nombre + " no: el problema está DENTRO de la red del PROVEEDOR.";
                    if (anterior == null)
                        return "Ningún equipo responde, ni siquiera " + nombre + ": se cortó la conexión con el PROVEEDOR, o falla esta PC (su cable o tarjeta de red).";
                    return anterior + " responde pero " + nombre + " no: se cortó la conexión con el PROVEEDOR (su cable o fibra, su equipo, o " + anterior + ").";
                default:
                    if (anterior == null)
                        return "Ningún equipo responde, ni siquiera " + nombre + ": falla esta PC (su cable o tarjeta de red), el switch o " + nombre + ".";
                    return anterior + " responde pero " + nombre + " no: puede estar fallando " + nombre + " o el cable entre " + anterior + " y " + nombre + ".";
            }
        }

        // Primera vez en una PC nueva: el router de esa red (puerta de enlace) y dos destinos en internet
        // de empresas distintas, para que un corte se confirme con los dos.
        static List<string[]> PorDefecto()
        {
            var filas = new List<string[]>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var gw = ni.GetIPProperties().GatewayAddresses
                               .Select(g => g.Address)
                               .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                    if (gw != null) { filas.Add(new[] { "Router", gw.ToString(), "red", "" }); break; }
                }
            }
            catch { }
            filas.Add(new[] { "Google", "8.8.8.8", "internet", "" });
            filas.Add(new[] { "Cloudflare", "1.1.1.1", "internet", "" });
            return filas;
        }
    }

    // Ajustes sueltos (clave=valor) en ajustes.txt junto al .exe. Contiene el código de Telegram: no compartir.
    static class Ajustes
    {
        static Dictionary<string, string> datos;
        static string Ruta { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ajustes.txt"); } }

        static void Cargar()
        {
            if (datos != null) return;
            datos = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(Ruta)) return;
                foreach (var l in File.ReadAllLines(Ruta))
                {
                    int i = l.IndexOf('=');
                    if (i > 0 && !l.StartsWith("#")) datos[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                }
            }
            catch { }
        }

        public static string Leer(string clave)
        {
            Cargar();
            string v;
            return datos.TryGetValue(clave, out v) ? v : "";
        }

        public static void Poner(string clave, string valor)
        {
            Cargar();
            datos[clave] = valor;
            try
            {
                File.WriteAllLines(Ruta, new[] { "# Ajustes de " + Programa.NOMBRE + ". Contiene el código de tu bot de Telegram: no compartas este archivo." }
                    .Concat(datos.Select(kv => kv.Key + "=" + kv.Value)), new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
