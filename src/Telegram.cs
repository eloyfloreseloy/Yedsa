// Avisos al celular con un bot de Telegram.
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
    // Avisos al celular con un bot de Telegram (gratis). Si no hay internet, los mensajes esperan y se reintentan.
    static class Telegram
    {
        static readonly Queue<string> pendientes = new Queue<string>();
        static bool enviando;

        public static string Token { get { return Ajustes.Leer("telegram_token"); } }
        public static bool Activo { get { return Token.Length > 0 && Personas().Count > 0; } }

        // Quiénes reciben los avisos: "id=Nombre;id=Nombre" en ajustes.txt.
        public static List<KeyValuePair<string, string>> Personas()
        {
            var lista = new List<KeyValuePair<string, string>>();
            foreach (var par in Ajustes.Leer("telegram_personas").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = par.IndexOf('=');
                string id = i < 0 ? par : par.Substring(0, i);
                if (!lista.Any(x => x.Key == id)) lista.Add(new KeyValuePair<string, string>(id, i < 0 ? "" : par.Substring(i + 1)));
            }
            return lista;
        }

        public static void GuardarPersonas(List<KeyValuePair<string, string>> lista)
        {
            Ajustes.Poner("telegram_personas", string.Join(";", lista.Select(p =>
                p.Key + "=" + p.Value.Replace(";", ",").Replace("=", "-"))));
        }

        public static async Task<Dictionary<string, object>> Llamar(string token, string metodo, Dictionary<string, string> datos)
        {
            string url = "https://api.telegram.org/bot" + token + "/" + metodo;
            var r = datos == null ? await Red.Http.GetAsync(url) : await Red.Http.PostAsync(url, new FormUrlEncodedContent(datos));
            string json = await r.Content.ReadAsStringAsync();
            return new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
        }

        static string Encabezado { get { return Programa.NOMBRE + " · " + Environment.MachineName + "\n"; } }

        // conReintento = false: si falla ahora se descarta (sirve para avisos que pierden sentido después).
        public static void Avisar(string texto, bool conReintento)
        {
            if (!Activo) return;
            if (conReintento)
            {
                pendientes.Enqueue(Encabezado + texto);
                while (pendientes.Count > 50) pendientes.Dequeue();
                Procesar();
            }
            else
            {
                var sinEsperar = Enviar(Encabezado + texto);
            }
        }

        public static async void Procesar()
        {
            if (enviando || !Activo) return;
            enviando = true;
            try
            {
                while (pendientes.Count > 0)
                {
                    bool? r = await Enviar(pendientes.Peek());
                    if (r == null) break;          // sin internet: se reintenta más tarde
                    pendientes.Dequeue();          // enviado, o Telegram lo rechazó (no se reintenta)
                }
            }
            finally { enviando = false; }
        }

        // A todas las personas. true = enviado, false = Telegram lo rechazó, null = no se pudo conectar.
        public static async Task<bool?> Enviar(string texto)
        {
            bool todos = true;
            foreach (var p in Personas())
            {
                bool? r = await EnviarA(p.Key, texto);
                if (r == null) return null;
                todos &= r.Value;
            }
            return todos;
        }

        public static async Task<bool?> EnviarA(string chat, string texto)
        {
            try
            {
                var r = await Llamar(Token, "sendMessage", new Dictionary<string, string> { { "chat_id", chat }, { "text", texto } });
                return r != null && r.ContainsKey("ok") && true.Equals(r["ok"]);
            }
            catch { return null; }
        }

        public static string NombreDeChat(Dictionary<string, object> chat)
        {
            object v;
            string n = "";
            if (chat.TryGetValue("first_name", out v)) n = Convert.ToString(v);
            if (chat.TryGetValue("last_name", out v)) n += " " + v;
            if (chat.TryGetValue("title", out v) && n.Length == 0) n = Convert.ToString(v);
            if (chat.TryGetValue("username", out v)) n += " (@" + v + ")";
            return n.Trim().Length > 0 ? n.Trim() : "(sin nombre)";
        }
    }
}
