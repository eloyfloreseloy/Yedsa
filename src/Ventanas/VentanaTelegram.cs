// Ventana para conectar el bot de Telegram y elegir quién recibe los avisos.
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
    class VentanaTelegram : Form
    {
        readonly TextBox txtToken = new TextBox();
        readonly Label lblEstado = new Label();
        readonly ListBox lstPersonas = new ListBox();
        readonly Button btnConectar, btnQuitarPersona, btnDesconectar;

        public VentanaTelegram()
        {
            Text = "Avisos por Telegram";
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(680, 690);

            var pasos = new Label();
            pasos.Location = new Point(18, 14);
            pasos.Size = new Size(650, 330);
            pasos.Text =
                "Es gratis y se hace una sola vez. BotFather responde en inglés; aquí va qué significa:\n\n" +
                "1.  En Telegram toca la lupa, busca  BotFather , ábrelo y escríbele  /newbot\n" +
                "2.  Te pregunta el NOMBRE del bot (\"Alright, a new bot...\"): escribe cualquiera,\n" +
                "     por ejemplo  Monitor del local\n" +
                "3.  Te pregunta el USUARIO del bot (\"...choose a username\"): tiene que terminar en \"bot\",\n" +
                "     por ejemplo  MonitorDelLocalBot  o  monitor_del_local_bot  (si ya existe, prueba otro)\n" +
                "4.  Te responde \"Done! Congratulations...\" y, debajo de  \"Use this token to access the\n" +
                "     HTTP API:\", hay un código así:  1234567890:AAHk3x...  Copia TODO ese código\n" +
                "     (el número, los dos puntos y las letras) y pégalo AQUÍ ABAJO, no en Telegram.\n" +
                "5.  Sal del chat de BotFather. Toca la lupa, busca el USUARIO de tu bot (el del paso 3),\n" +
                "     ábrelo y toca el botón grande  INICIAR  de abajo.\n" +
                "6.  Aprieta  Conectar  aquí. Te llegará un mensaje de prueba.\n\n" +
                "¿Perdiste el código? En el chat de BotFather toca  Abrir / Open  → My bots → tu bot → Copy.\n" +
                "El código es como la contraseña de tu bot: no se lo pases a nadie.";

            var lbl = new Label();
            lbl.Text = "Código de tu bot (también puedes pegar el mensaje entero de BotFather):";
            lbl.Location = new Point(18, 350);
            lbl.AutoSize = true;
            txtToken.Location = new Point(18, 374);
            txtToken.Width = 644;
            txtToken.Text = Telegram.Token;

            var lblPersonas = new Label();
            lblPersonas.Location = new Point(18, 414);
            lblPersonas.Size = new Size(650, 44);
            lblPersonas.Text = "Reciben los avisos:   (para sumar a otra persona, por ejemplo tu jefe: que busque tu bot\n" +
                               "en Telegram y toque INICIAR; luego aprieta  Conectar / Agregar persona )";
            lstPersonas.Location = new Point(18, 460);
            lstPersonas.Size = new Size(500, 80);
            btnQuitarPersona = new BotonPlano();
            btnQuitarPersona.Text = "Quitar persona";
            btnQuitarPersona.AutoSize = true;
            btnQuitarPersona.Location = new Point(530, 459);
            btnQuitarPersona.Click += delegate { QuitarPersona(); };

            lblEstado.Location = new Point(18, 550);
            lblEstado.Size = new Size(650, 80);

            btnConectar = new BotonPlano();
            btnConectar.Text = "Conectar / Agregar persona";
            btnConectar.Font = new Font(Font, FontStyle.Bold);
            btnConectar.AutoSize = true;
            btnConectar.Location = new Point(18, 642);
            btnConectar.Click += delegate { Conectar(); };

            btnDesconectar = new BotonPlano();
            btnDesconectar.Text = "Desconectar todo";
            btnDesconectar.AutoSize = true;
            btnDesconectar.Location = new Point(250, 642);
            btnDesconectar.Click += delegate
            {
                if (!VentanaPrincipal.Preguntar(this, "Desconectar Telegram", "Nadie va a recibir avisos y se borra el código del bot. ¿Seguro?",
                                        "Desconectar", "Cancelar")) return;
                Ajustes.Poner("telegram_token", "");
                Telegram.GuardarPersonas(new List<KeyValuePair<string, string>>());
                txtToken.Text = "";
                MostrarEstado();
                Estado("Desconectado.", null);
            };

            var cerrar = new BotonPlano();
            cerrar.Text = "Cerrar";
            cerrar.AutoSize = true;
            cerrar.Location = new Point(580, 642);
            cerrar.DialogResult = DialogResult.OK;
            CancelButton = cerrar;

            Controls.AddRange(new Control[] { pasos, lbl, txtToken, lblPersonas, lstPersonas, btnQuitarPersona,
                                              lblEstado, btnConectar, btnDesconectar, cerrar });
            Tema.AplicarA(this);
            MostrarEstado();
        }

        void MostrarEstado()
        {
            lstPersonas.Items.Clear();
            foreach (var p in Telegram.Personas())
                lstPersonas.Items.Add(p.Value.Length > 0 ? p.Value : "(chat " + p.Key + ")");
            if (lstPersonas.Items.Count == 0) lstPersonas.Items.Add("— nadie todavía —");
            bool activo = Telegram.Activo;
            lstPersonas.Enabled = btnQuitarPersona.Enabled = btnDesconectar.Enabled = activo;
            Estado(activo ? "✔  Conectado." : "Sin conectar.", activo ? (bool?)true : null);
        }

        void QuitarPersona()
        {
            var personas = Telegram.Personas();
            int i = lstPersonas.SelectedIndex;
            if (i < 0 || i >= personas.Count) { Estado("Primero toca en la lista a la persona que quieres quitar.", false); return; }
            if (!VentanaPrincipal.Preguntar(this, "Quitar persona", "¿Dejar de enviarle avisos a " + lstPersonas.Items[i] + "?", "Quitar", "Cancelar")) return;
            personas.RemoveAt(i);
            Telegram.GuardarPersonas(personas);
            MostrarEstado();
        }

        async void Conectar()
        {
            // Acepta el código solo o el mensaje entero de BotFather: busca "número:letras".
            var coincide = System.Text.RegularExpressions.Regex.Match(txtToken.Text, @"\d{5,}:[A-Za-z0-9_-]{20,}");
            if (!coincide.Success)
            {
                Estado(txtToken.Text.Trim().Length == 0
                    ? "Pega primero el código que te dio BotFather."
                    : "No encuentro el código. Tiene que ser TODO junto: número, dos puntos y letras (ej. 1234567890:AAHk3x...).", false);
                return;
            }
            string token = coincide.Value;
            txtToken.Text = token;
            btnConectar.Enabled = false;
            Estado("Conectando con Telegram...", null);
            try
            {
                var yo = await Telegram.Llamar(token, "getMe", null);
                if (yo == null || !true.Equals(yo["ok"]))
                {
                    Estado("Ese código no es válido. Cópialo otra vez completo desde el mensaje de BotFather.", false);
                    return;
                }
                string bot = "@" + ((Dictionary<string, object>)yo["result"])["username"];
                if (token != Telegram.Token)
                {
                    // Bot nuevo: la lista de personas era del bot anterior.
                    Ajustes.Poner("telegram_token", token);
                    Telegram.GuardarPersonas(new List<KeyValuePair<string, string>>());
                }

                // Personas que tocaron INICIAR o le escribieron al bot (Telegram guarda las últimas 24 h).
                var personas = Telegram.Personas();
                var nuevas = new List<KeyValuePair<string, string>>();
                var upd = await Telegram.Llamar(token, "getUpdates", null);
                foreach (var u in (object[])upd["result"])
                {
                    var d = (Dictionary<string, object>)u;
                    object m;
                    if (!d.TryGetValue("message", out m) && !d.TryGetValue("my_chat_member", out m)) continue;
                    var chat = (Dictionary<string, object>)((Dictionary<string, object>)m)["chat"];
                    string id = Convert.ToString(chat["id"], CultureInfo.InvariantCulture);
                    if (personas.Any(p => p.Key == id) || nuevas.Any(p => p.Key == id)) continue;
                    nuevas.Add(new KeyValuePair<string, string>(id, Telegram.NombreDeChat(chat)));
                }

                if (nuevas.Count == 0)
                {
                    MostrarEstado();
                    Estado(personas.Count == 0
                        ? "El código está bien. Falta un paso: sal de BotFather, toca la lupa, busca " + bot +
                          ", ábrelo y toca INICIAR abajo. Luego vuelve a apretar Conectar."
                        : "No hay personas nuevas. Para sumar a alguien: que busque " + bot +
                          " en Telegram y toque INICIAR; luego aprieta otra vez Conectar / Agregar persona.", personas.Count == 0 ? (bool?)false : null);
                    return;
                }

                // Nunca se agrega a nadie sin preguntar: cualquiera puede encontrar el bot y tocar INICIAR.
                var agregadas = new List<string>();
                foreach (var n in nuevas)
                {
                    if (!VentanaPrincipal.Preguntar(this, "¿Agregar persona?",
                            "Encontré a  " + n.Value + "  (tocó INICIAR en " + bot + ").\n\n¿Quieres que reciba los avisos de " + Programa.NOMBRE + "?\n" +
                            "Si no sabes quién es, elige \"No\".", "Sí, agregar", "No")) continue;
                    personas.Add(n);
                    Telegram.GuardarPersonas(personas);
                    await Telegram.EnviarA(n.Key, Programa.NOMBRE + " · " + Environment.MachineName +
                                                  "\n✅ Conectado. Desde ahora te aviso aquí de los cortes de red y cambios de IP pública.");
                    agregadas.Add(n.Value);
                }
                MostrarEstado();
                if (agregadas.Count > 0)
                    Estado("✔  Agregado: " + string.Join(", ", agregadas) + ". Le llegó un mensaje de prueba.", true);
            }
            catch (Exception ex)
            {
                Estado("No se pudo conectar con Telegram (¿hay internet?). " + ex.Message, false);
            }
            finally
            {
                btnConectar.Enabled = true;
            }
        }

        void Estado(string texto, bool? bien)
        {
            lblEstado.Text = texto;
            lblEstado.ForeColor = bien == true ? Tema.VerdeTexto : bien == false ? Tema.RojoTexto : Tema.TextoSec;
        }
    }
}
