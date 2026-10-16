// Ventana para agregar, quitar, ordenar y editar los equipos vigilados.
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
    // Ventana para agregar, quitar, ordenar y editar los equipos vigilados.
    class EditorEquipos : Form
    {
        const int C_NUM = 0, C_NOMBRE = 1, C_IP = 2, C_ROL = 3, C_MSG = 4;

        const string AYUDA_LISTA =
            "Yedsa le hace ping a cada equipo de la lista y, cuando algo falla, busca el PRIMERO que dejó de responder. " +
            "Por eso el orden importa: tiene que ser el mismo camino que sigue el internet, desde esta PC hacia afuera.\n\n" +
            "CÓMO SABER EL ORDEN\n" +
            "Lo más fácil: toca \"Detectar mi red\" abajo. Yedsa ve el camino solo y arma la lista; tú solo revisas \"Qué es\" y los nombres.\n" +
            "A mano: abre CMD y escribe      tracert -d 8.8.8.8\n" +
            "Cada línea es un equipo por el que pasa el internet, en orden. Los primeros son tuyos (routers, MikroTik); " +
            "después vienen los del proveedor.\n\n" +
            "QUÉ PONER, EN ESTE ORDEN\n" +
            "      1.  Tus equipos del camino (los primeros saltos del tracert), como \"Equipo de mi red\".\n" +
            "      2.  Uno o dos equipos del proveedor (los saltos que siguen, si responden a \"Probar ping\"), como \"Equipo del proveedor\".\n" +
            "      3.  Al final, 2 o 3 destinos en internet de empresas distintas, como \"Destino en internet\". " +
            "Por ejemplo: Google 8.8.8.8 y Cloudflare 1.1.1.1.\n\n" +
            "DE QUIÉN ES EL CORTE\n" +
            "Si el corte empieza en un equipo de tu red o en una PC, es del local (CORTE LOCAL). Si empieza en un equipo " +
            "del proveedor o en los destinos en internet, es del proveedor (CORTE). El reporte para el proveedor solo lleva los suyos.\n\n" +
            "POR QUÉ VARIOS DESTINOS EN INTERNET\n" +
            "Cualquier servidor de internet puede dejar de contestar pings un momento aunque el internet funcione. " +
            "Si solo vigilas Google y Google no contesta, parecería un corte. Con varios destinos, Yedsa solo anota CORTE " +
            "si TODOS dejan de responder a la vez; si alguno responde, hay internet y anota NO FUE CORTE.\n\n" +
            "EQUIPOS QUE NO ESTÁN EN EL CAMINO\n" +
            "Si quieres vigilar algo por donde no pasa el internet (otra red, una impresora...), ponlo justo después " +
            "del equipo al que está conectado, como \"Fuera del camino\". Si falla, Yedsa avisa, pero nunca lo cuenta como corte.";

        const string AYUDA_MENSAJE =
            "Cada equipo puede tener un mensaje. Yedsa lo muestra cuando ese equipo es el PRIMERO de la lista " +
            "que deja de responder: todos los de arriba responden y este no.\n\n" +
            "Tiene dos partes, separadas por dos puntos:\n" +
            "      lo que se ve   :   qué puede estar fallando\n\n" +
            "Para escribirlo, pregúntate: si todos los de arriba responden y este no, ¿qué puede estar fallando? " +
            "Casi siempre es una de estas dos cosas:\n" +
            "      1.  Este equipo (apagado, colgado o dañado).\n" +
            "      2.  Lo que lo une con el anterior: el cable, o el puerto de alguno de los dos.\n\n" +
            "Plantilla:\n" +
            "      [Anterior] responde pero [Este] no: puede estar fallando [Este] o el cable entre [Anterior] y [Este].\n\n" +
            "Ejemplo, con un router nuevo después del MikroTik 2:\n" +
            "      El MikroTik 2 responde pero el Router Wifi no: puede estar fallando el Router Wifi o el cable " +
            "entre el MikroTik 2 y el Router Wifi.\n\n" +
            "Para el primero de la lista no hay anterior: lo que puede fallar es esta misma PC (su cable o tarjeta de red) o el switch.\n\n" +
            "Si lo dejas vacío, Yedsa arma uno según lo que elegiste en \"Qué es\": lo ves en gris en la tabla. " +
            "Casi siempre alcanza con ese.";

        // Lo que dice la línea de abajo según la columna en la que estás.
        static readonly string[] AYUDA_COLUMNA =
        {
            "",
            "Nombre: como quieras llamarlo (ej. MikroTik 1, Proveedor, Google). Para agregar un equipo, escribe en la última fila vacía (la que tiene *).",
            "IP: la dirección a la que se hace ping (ej. 192.168.1.1 o 8.8.8.8), o un nombre como www.youtube.com. " +
            "Para ver si responde, selecciona la fila y toca \"Probar ping\".",
            Roles.AYUDA,
            "Mensaje (opcional): lo que " + Programa.NOMBRE + " te dirá si el corte EMPIEZA en este equipo. Si lo dejas vacío, " +
            "se usa el automático que ves en gris. Ayuda arriba: \"¿Cómo escribo un buen mensaje?\"."
        };
        const string AYUDA_GENERAL = "Haz clic en una columna para ver aquí qué significa. Para agregar un equipo, escribe en la última fila vacía (la que tiene *).";

        readonly DataGridView grid = new DataGridView();
        readonly Label pista = new Label();
        Button botonDetectar;
        readonly Font fAuto;

        public EditorEquipos()
        {
            Text = "Editar equipos";
            Font = new Font("Segoe UI", 9.5f);
            fAuto = new Font(Font, FontStyle.Italic);
            Size = new Size(1180, 600);
            MinimumSize = new Size(900, 440);
            StartPosition = FormStartPosition.CenterParent;
            ShowIcon = false;
            MinimizeBox = false;

            var ayuda = new Label();
            ayuda.Dock = DockStyle.Top;
            ayuda.Height = 112;
            // Debajo de la explicación, los enlaces a la ayuda completa: cómo armar la lista y cómo escribir los mensajes.
            var comoArmar = Enlace("¿Cómo armo mi lista? (qué equipos poner y en qué orden)", 16, 86,
                                   delegate { Dialogos.Informar(this, "Cómo armar la lista de equipos", AYUDA_LISTA); });
            Enlace("¿Cómo escribo un buen mensaje?", comoArmar.Right + 24, 86,
                   delegate { Dialogos.Informar(this, "Cómo escribir el mensaje", AYUDA_MENSAJE); });
            ayuda.Controls.AddRange(enlaces.ToArray());
            ayuda.Padding = new Padding(12, 10, 12, 0);
            ayuda.Text =
                "Pon los equipos EN ORDEN, como va el internet: el 1 es el más cercano a esta PC y al final van los destinos en internet.\n" +
                "•  En \"Qué es\" eliges si es tuyo, del proveedor, un destino en internet... Eso decide de quién es el corte.\n" +
                "•  Haz clic en cualquier columna y abajo verás qué significa.";

            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Color.White;
            grid.RowHeadersVisible = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToAddRows = true;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.RowTemplate.Height = 28;
            grid.ColumnHeadersHeight = 30;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ShowCellToolTips = false;
            grid.Columns.Add("num", "#");
            grid.Columns.Add("nombre", "Nombre");
            grid.Columns.Add("ip", "IP");
            var rol = new DataGridViewComboBoxColumn();
            rol.Name = "rol";
            rol.HeaderText = "Qué es";
            rol.Items.AddRange(Roles.Todos.Select(Roles.Nombre).Cast<object>().ToArray());
            rol.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;   // la flecha aparece al editar: así se ve con el tema
            rol.FlatStyle = FlatStyle.Flat;
            rol.Width = 190;
            grid.Columns.Add(rol);
            grid.Columns.Add("msg", "Mensaje si el corte empieza aquí (opcional)");
            grid.Columns[C_NUM].Width = 36;
            grid.Columns[C_NUM].ReadOnly = true;
            grid.Columns[C_NUM].DefaultCellStyle.ForeColor = Color.DimGray;
            grid.Columns[C_NUM].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns[C_NOMBRE].Width = 170;
            grid.Columns[C_IP].Width = 150;
            grid.Columns[C_MSG].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            grid.CellFormatting += (s, e) =>
            {
                if (e.ColumnIndex == C_NUM && e.RowIndex >= 0 && !grid.Rows[e.RowIndex].IsNewRow)
                    e.Value = (e.RowIndex + 1).ToString();
            };
            grid.CellPainting += PintarMensajeAuto;
            grid.CellValueChanged += delegate { grid.Invalidate(); };
            grid.RowsRemoved += delegate { grid.Invalidate(); };
            // Un equipo nuevo empieza como "Equipo de mi red".
            grid.DefaultValuesNeeded += (s, e) => { e.Row.Cells[C_ROL].Value = Roles.Nombre(Rol.Red); };
            // Lo elegido en "Qué es" se aplica al momento (para que el mensaje automático cambie enseguida).
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == C_ROL) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            // La lista desplegable con los colores del tema.
            grid.EditingControlShowing += (s, e) =>
            {
                var combo = e.Control as ComboBox;
                if (combo == null) return;
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = Tema.Superficie2;
                combo.ForeColor = Tema.Texto;
            };
            // La línea de abajo explica la columna en la que estás.
            grid.CurrentCellChanged += delegate
            {
                int col = grid.CurrentCell == null ? -1 : grid.CurrentCell.ColumnIndex;
                pista.Text = col > 0 && col < AYUDA_COLUMNA.Length ? AYUDA_COLUMNA[col] : AYUDA_GENERAL;
            };

            foreach (var e in Config.Leer()) grid.Rows.Add("", e.Nombre, e.Ip, Roles.Nombre(e.Rol), e.Mensaje);

            var botones = new FlowLayoutPanel();
            botones.Dock = DockStyle.Bottom;
            botones.AutoSize = true;
            botones.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            botones.Padding = new Padding(8, 8, 8, 8);
            botones.Controls.Add(Boton("▲  Subir", delegate { Mover(-1); }));
            botones.Controls.Add(Boton("▼  Bajar", delegate { Mover(1); }));
            botones.Controls.Add(Boton("Quitar", Quitar));
            botones.Controls.Add(Boton("Probar ping", Probar));
            botonDetectar = Boton("Detectar mi red", Detectar);
            botones.Controls.Add(botonDetectar);
            var guardar = Boton("Guardar", Guardar);
            guardar.Font = new Font(Font, FontStyle.Bold);
            guardar.Margin = new Padding(40, 3, 3, 3);
            botones.Controls.Add(guardar);
            var cancelar = Boton("Cancelar", delegate { DialogResult = DialogResult.Cancel; });
            botones.Controls.Add(cancelar);
            CancelButton = cancelar;

            pista.Dock = DockStyle.Bottom;
            pista.Height = 66;   // hasta 3 líneas (la de "Qué es" es la más larga)
            pista.Padding = new Padding(12, 6, 12, 0);
            pista.ForeColor = Color.DimGray;
            pista.Text = AYUDA_GENERAL;

            Controls.Add(grid);
            Controls.Add(pista);
            Controls.Add(botones);
            Controls.Add(ayuda);
            Tema.AplicarA(this);
        }

        readonly List<LinkLabel> enlaces = new List<LinkLabel>();

        LinkLabel Enlace(string texto, int x, int y, Action accion)
        {
            var l = new LinkLabel();
            l.Text = texto;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            l.Width = TextRenderer.MeasureText(texto, Font).Width;   // con la letra de la ventana, para ubicar el siguiente a su derecha
            l.LinkColor = l.ActiveLinkColor = l.VisitedLinkColor = Tema.Azul;
            l.LinkClicked += delegate { accion(); };
            enlaces.Add(l);
            return l;
        }

        static Button Boton(string texto, Action accion)
        {
            var b = new BotonPlano();
            b.Text = texto;
            b.AutoSize = true;
            b.Height = 30;
            b.Padding = new Padding(6, 0, 6, 0);
            b.Click += delegate { accion(); };
            return b;
        }

        static string Celda(DataGridViewRow r, int c)
        {
            var v = r.Cells[c].Value as string;
            return v == null ? "" : v.Trim();
        }

        static Rol RolDe(DataGridViewRow r)
        {
            string texto = Celda(r, C_ROL);
            foreach (var rol in Roles.Todos) if (Roles.Nombre(rol) == texto) return rol;
            return Rol.Red;
        }

        void PintarMensajeAuto(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != C_MSG) return;
            var row = grid.Rows[e.RowIndex];
            if (row.IsNewRow || Celda(row, C_MSG).Length > 0) return;
            if (grid.IsCurrentCellInEditMode && grid.CurrentCell.RowIndex == e.RowIndex && grid.CurrentCell.ColumnIndex == C_MSG) return;

            string nombre = Celda(row, C_NOMBRE);
            if (nombre.Length == 0) return;
            // El mismo "anterior" que usa Yedsa al vigilar (salta los de fuera del camino y, para internet, los otros destinos).
            var arriba = Enumerable.Range(0, e.RowIndex).Select(i => RolDe(grid.Rows[i])).ToList();
            var rol = RolDe(row);
            int a = Config.Anterior(arriba, rol);
            string mensaje = Config.MensajeAuto(nombre, rol, a < 0 ? null : Celda(grid.Rows[a], C_NOMBRE), a < 0 ? (Rol?)null : arriba[a]);
            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
            var rect = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y, e.CellBounds.Width - 8, e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, "(automático)  " + mensaje, fAuto, rect,
                                  (e.State & DataGridViewElementStates.Selected) != 0 ? Color.Gainsboro : Color.Gray,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }

        void Mover(int d)
        {
            if (grid.CurrentCell == null) return;
            grid.EndEdit();
            int i = grid.CurrentCell.RowIndex, j = i + d, col = grid.CurrentCell.ColumnIndex;
            if (j < 0 || j >= grid.Rows.Count || grid.Rows[i].IsNewRow || grid.Rows[j].IsNewRow) return;
            for (int c = C_NOMBRE; c <= C_MSG; c++)
            {
                var tmp = grid.Rows[i].Cells[c].Value;
                grid.Rows[i].Cells[c].Value = grid.Rows[j].Cells[c].Value;
                grid.Rows[j].Cells[c].Value = tmp;
            }
            grid.CurrentCell = grid.Rows[j].Cells[col];
        }

        void Quitar()
        {
            if (grid.CurrentCell == null) return;
            var row = grid.Rows[grid.CurrentCell.RowIndex];
            if (!row.IsNewRow) grid.Rows.Remove(row);
        }

        void Probar()
        {
            if (grid.CurrentCell == null) return;
            grid.EndEdit();
            var row = grid.Rows[grid.CurrentCell.RowIndex];
            string ip = Celda(row, C_IP);
            if (ip.Length == 0) { MessageBox.Show("Esa fila no tiene IP.", Text); return; }
            Cursor = Cursors.WaitCursor;
            string resultado;
            try
            {
                using (var p = new Ping())
                {
                    var r = p.Send(ip, 2000);
                    resultado = r.Status == IPStatus.Success
                        ? "✔  " + ip + " responde (" + r.RoundtripTime + " ms)."
                        : "✖  " + ip + " NO responde.";
                }
            }
            catch { resultado = "✖  \"" + ip + "\" no es una IP o dirección válida."; }
            Cursor = Cursors.Default;
            MessageBox.Show(resultado, "Probar ping");
        }

        // ---------- Detectar mi red ----------
        // Hace un tracert hacia Google y arma la lista con el camino real, en orden. Lo que ya estaba en la lista
        // conserva su nombre y su mensaje. Lo que ya no está en el camino queda como "fuera del camino".
        // "Qué es" de los equipos nuevos se adivina por la IP, así que hay que revisarlo antes de guardar.

        const int SALTOS_DETECTAR = 15;

        async void Detectar()
        {
            grid.EndEdit();
            if (!Dialogos.Preguntar(this, "Detectar mi red",
                    "Yedsa va a ver por dónde pasa tu internet hasta llegar a Google (con tracert) y a armar la lista con ese camino, en orden. " +
                    "Tarda hasta un minuto.\n\nLo que ya está en tu lista conserva su nombre y su mensaje. Al terminar, revisa la columna " +
                    "\"Qué es\" antes de guardar: Yedsa adivina por la IP, pero tú sabes qué equipos son tuyos y cuáles del proveedor.",
                    "Detectar", "Cancelar"))
                return;
            botonDetectar.Enabled = false;
            botonDetectar.Text = "Detectando...";
            Cursor = Cursors.WaitCursor;
            var saltos = await Task.Run(() => Red.Tracert("8.8.8.8", SALTOS_DETECTAR));
            // Cada salto se prueba con ping: los que no contestan pings no sirven para vigilar.
            var responden = saltos == null ? null : await Task.Run(() => saltos.Select(ip => ip != "*" && Red.Responde(ip, 2)).ToList());
            Cursor = Cursors.Default;
            botonDetectar.Text = "Detectar mi red";
            botonDetectar.Enabled = true;
            if (saltos == null || saltos.Count == 0 || saltos[0] == "*")
            {
                Dialogos.Informar(this, "Detectar mi red", "No se pudo ver el camino. ¿Esta PC tiene internet ahora mismo?");
                return;
            }
            Dialogos.Informar(this, "Detectar mi red", ArmarConCamino(saltos, responden));
        }

        // Reemplaza la lista por: PCs, el camino detectado, lo que quedó fuera del camino y los destinos en internet.
        string ArmarConCamino(List<string> saltos, List<bool> responden)
        {
            var antes = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && Celda(r, C_IP).Length > 0)
                            .Select(r => new { Nombre = Celda(r, C_NOMBRE), Ip = Celda(r, C_IP), Rol = RolDe(r), Msg = Celda(r, C_MSG) }).ToList();
            var filas = new List<object[]>();
            var usadas = new HashSet<string>();
            Action<string, string, Rol, string> agregar = (nombre, ip, rol, msg) =>
            {
                filas.Add(new object[] { "", nombre, ip, Roles.Nombre(rol), msg });
                usadas.Add(ip);
            };

            // 1. Las PCs que se apagan van primero, como estaban.
            foreach (var a in antes.Where(x => x.Rol == Rol.Pc)) agregar(a.Nombre, a.Ip, a.Rol, a.Msg);

            // 2. El camino: hasta el primer salto con IP pública (el borde del proveedor); más allá ya es internet,
            //    donde las rutas cambian solas. Son "de mi red" hasta el último salto con IP de local (192.168 / 172.16).
            int ultimoSalto = saltos.FindIndex(ip => ip != "*" && !Red.EsPrivada(ip));
            if (ultimoSalto < 0 || saltos[ultimoSalto] == "8.8.8.8") ultimoSalto = saltos.Count - 1;
            if (saltos[ultimoSalto] == "8.8.8.8") ultimoSalto--;
            int ultimoLocal = saltos.FindLastIndex(ultimoSalto, ip => Red.EsDeLocal(ip));
            if (ultimoSalto < 0) return "No se pudo ver el camino: el tracert llegó directo a internet.";
            var mudos = new List<string>();
            int nuevos = 0, enCamino = 0;
            for (int i = 0; i <= ultimoSalto; i++)
            {
                string ip = saltos[i];
                if (ip == "*") continue;
                if (!responden[i]) { mudos.Add(ip); continue; }
                var ya = antes.FirstOrDefault(x => x.Ip == ip);
                enCamino++;
                Rol rol = i <= ultimoLocal ? Rol.Red : Rol.Proveedor;
                if (ya != null && (ya.Rol == Rol.Red || ya.Rol == Rol.Proveedor)) rol = ya.Rol;   // lo que ya decidiste, se respeta
                if (ya != null) agregar(ya.Nombre, ip, rol, ya.Rol == rol ? ya.Msg : "");
                else
                {
                    agregar((rol == Rol.Red ? "Router" : "Proveedor") + " (salto " + (i + 1) + ")", ip, rol, "");
                    nuevos++;
                }
            }

            // 3. Lo que estaba en la lista y ya no está en el camino: se sigue vigilando, pero fuera del camino.
            var quedaronFuera = new List<string>();
            foreach (var a in antes.Where(x => x.Rol != Rol.Pc && x.Rol != Rol.Internet && !usadas.Contains(x.Ip)))
            {
                agregar(a.Nombre, a.Ip, Rol.Fuera, a.Rol == Rol.Fuera ? a.Msg : "");   // su mensaje era para otro lugar de la cadena
                if (a.Rol != Rol.Fuera) quedaronFuera.Add(a.Nombre + " (" + a.Ip + ")");
            }

            // 4. Los destinos en internet, al final (si no había, Google y Cloudflare).
            var destinos = antes.Where(x => x.Rol == Rol.Internet).ToList();
            if (destinos.Count == 0)
            {
                agregar("Google", "8.8.8.8", Rol.Internet, "");
                agregar("Cloudflare", "1.1.1.1", Rol.Internet, "");
            }
            foreach (var a in destinos) agregar(a.Nombre, a.Ip, a.Rol, a.Msg);

            grid.Rows.Clear();
            foreach (var f in filas) grid.Rows.Add(f);
            grid.Invalidate();

            var sb = new StringBuilder();
            sb.Append("Camino encontrado: " + string.Join(" → ", saltos.Take(ultimoSalto + 1)) + " → internet.\n\n");
            sb.Append("•  Equipos del camino en la lista: " + enCamino +
                      (nuevos > 0 ? " (" + nuevos + " nuevos, con nombres como \"Router (salto 3)\": cámbiales el nombre si sabes qué son)." : ".") + "\n");
            if (mudos.Count > 0)
                sb.Append("•  No contestan pings, así que no se agregaron: " + string.Join(", ", mudos) + ".\n");
            if (quedaronFuera.Count > 0)
                sb.Append("•  Ya no están en el camino y quedaron como \"Fuera del camino\" (se siguen vigilando; si ya no te sirven, quítalos): " +
                          string.Join(", ", quedaronFuera) + ".\n");
            sb.Append("\nRevisa la columna \"Qué es\": los equipos con IP 192.168.x.x quedaron como \"Equipo de mi red\" y los demás como " +
                      "\"Equipo del proveedor\". El módem o la antena del proveedor suele tener IP 192.168.x.x: si es suyo, cámbialo a " +
                      "\"Equipo del proveedor\". Nada se guarda hasta que toques Guardar.");
            return sb.ToString();
        }

        void Guardar()
        {
            grid.EndEdit();
            var filas = new List<string[]>();
            string primerInternet = null, primerProveedor = null;
            foreach (DataGridViewRow r in grid.Rows)
            {
                if (r.IsNewRow) continue;
                string nombre = Celda(r, C_NOMBRE).Replace(";", ",");
                string ip = Celda(r, C_IP);
                string msg = Celda(r, C_MSG).Replace("\r", " ").Replace("\n", " ").Replace(";", ",");
                var rol = RolDe(r);
                if (nombre.Length == 0 && ip.Length == 0 && msg.Length == 0) continue;
                int num = r.Index + 1;
                if (nombre.Length == 0) { Aviso("La fila " + num + " no tiene nombre.", r, C_NOMBRE); return; }
                if (ip.Length == 0) { Aviso("La fila " + num + " (" + nombre + ") no tiene IP.", r, C_IP); return; }
                if (Uri.CheckHostName(ip) == UriHostNameType.Unknown)
                {
                    Aviso("\"" + ip + "\" (fila " + num + ") no es una IP válida. Ejemplo: 192.168.1.1", r, C_IP);
                    return;
                }
                // El orden tiene que ser el del camino: lo del local, luego el proveedor, y al final internet.
                if (rol != Rol.Internet && primerInternet != null)
                {
                    Aviso("Los destinos en internet van al final de la lista, pero \"" + nombre + "\" (fila " + num + ") está debajo de \"" +
                          primerInternet + "\".\n\nSube \"" + nombre + "\" con ▲ Subir, o cambia lo que es.", r, C_NOMBRE);
                    return;
                }
                if ((rol == Rol.Red || rol == Rol.Pc) && primerProveedor != null)
                {
                    Aviso("\"" + nombre + "\" (fila " + num + ") es de tu local, pero está debajo de \"" + primerProveedor + "\", que es del proveedor.\n\n" +
                          "El internet pasa primero por tus equipos y después por los del proveedor: sube \"" + nombre + "\" con ▲ Subir, " +
                          "o cambia lo que es.", r, C_ROL);
                    return;
                }
                if (rol == Rol.Internet && primerInternet == null) primerInternet = nombre;
                if (rol == Rol.Proveedor && primerProveedor == null) primerProveedor = nombre;
                filas.Add(new[] { nombre, ip, Roles.Clave(rol), msg });
            }
            if (filas.Count == 0) { MessageBox.Show("Agrega al menos un equipo.", Text); return; }
            if (filas.Count > VentanaPrincipal.MAX_EQUIPOS)
            {
                MessageBox.Show("Se pueden vigilar como máximo " + VentanaPrincipal.MAX_EQUIPOS + " equipos (tienes " + filas.Count + "). " +
                                "Cada equipo abre su propio ping, y con más la PC se pondría lenta.", Text);
                return;
            }
            // Con menos de 2 destinos en internet, un servidor que no conteste pings parecería un corte: se avisa, pero se deja guardar.
            int destinos = filas.Count(f => f[2] == Roles.Clave(Rol.Internet));
            if (destinos < 2 && !Dialogos.Preguntar(this, "Pocos destinos en internet",
                    (destinos == 0 ? "No hay ningún \"Destino en internet\". Sin ellos, el último de la lista hace de internet."
                                   : "Solo hay un \"Destino en internet\". Si un rato no contesta los pings, parecerá un corte aunque el internet funcione.") +
                    "\n\nTe recomiendo tener al menos dos, de empresas distintas, al final de la lista: por ejemplo Google 8.8.8.8 y Cloudflare 1.1.1.1.",
                    "Guardar así", "Volver a la lista"))
                return;
            // Se prueban todos a la vez: una IP mal escrita se ve ahora y no días después como un equipo "caído".
            Cursor = Cursors.WaitCursor;
            var pruebas = filas.Select(f => Task.Factory.StartNew(() =>
            {
                try { using (var p = new Ping()) return p.Send(f[1], 1500).Status == IPStatus.Success; }
                catch { return false; }
            })).ToArray();
            Task.WaitAll(pruebas);
            Cursor = Cursors.Default;
            var mudos = filas.Where((f, i) => !pruebas[i].Result).Select(f => "      •  " + f[0] + "  (" + f[1] + ")").ToList();
            if (mudos.Count > 0 && !Dialogos.Preguntar(this, "Algunos equipos no responden",
                    "Estos equipos no responden ahora mismo:\n\n" + string.Join("\n", mudos) + "\n\n" +
                    "Si la IP está mal escrita o el equipo no contesta pings, " + Programa.NOMBRE + " lo verá caído todo el tiempo. " +
                    "Si está apagado a propósito, está bien.",
                    "Guardar igual", "Volver a la lista"))
                return;
            try { Config.Guardar(filas); }
            catch (Exception ex) { MessageBox.Show("No se pudo guardar: " + ex.Message, Text); return; }
            DialogResult = DialogResult.OK;
        }

        void Aviso(string texto, DataGridViewRow r, int col)
        {
            MessageBox.Show(texto, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            grid.CurrentCell = r.Cells[col];
        }
    }
}
