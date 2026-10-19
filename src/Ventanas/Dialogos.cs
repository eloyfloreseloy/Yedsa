// Ventanitas de pregunta y de texto con el tema del programa.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Yedsa
{
    static class Dialogos
    {
        static Form Base(string titulo, int ancho)
        {
            var f = new Form();
            f.Text = titulo;
            f.Font = Tema.Ventanita;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MinimizeBox = f.MaximizeBox = false;
            f.ShowIcon = false;
            f.ShowInTaskbar = false;
            f.TopMost = true;
            f.ClientSize = new Size(ancho, 100);
            return f;
        }

        // peligro: el botón principal cierra o borra algo. Se pinta en rojo y el foco queda en el otro,
        // para que un Enter sin querer no lo active.
        static FlowLayoutPanel FilaBotones(Form f, string si, string no, bool peligro = false)
        {
            var fila = new FlowLayoutPanel();
            fila.FlowDirection = FlowDirection.RightToLeft;
            fila.Dock = DockStyle.Bottom;
            fila.Height = 58;
            fila.Padding = new Padding(12, 10, 12, 12);
            var bNo = new BotonPlano();
            bNo.Text = no;
            bNo.DialogResult = DialogResult.Cancel;
            var bSi = new BotonPlano();
            bSi.Text = si;
            bSi.Font = Tema.Negrita;
            bSi.DialogResult = DialogResult.OK;
            bSi.Margin = new Padding(0, 0, 8, 0);
            fila.Controls.Add(bNo);
            fila.Controls.Add(bSi);
            f.AcceptButton = bSi;
            f.CancelButton = bNo;
            if (peligro)
            {
                bSi.Peligro = true;
                f.Shown += delegate { bNo.Focus(); };
            }
            return fila;
        }

        static DialogResult Mostrar(Form f, Form dueno)
        {
            f.StartPosition = dueno != null && dueno.Visible ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            Tema.AplicarA(f);
            return f.ShowDialog(dueno != null && dueno.Visible ? dueno : null);
        }

        // Pregunta con botones que dicen lo que hacen (en vez de Sí/No).
        public static bool Preguntar(Form dueno, string titulo, string texto, string si, string no, bool peligro = false)
        {
            using (var f = Base(titulo, 480))
            {
                var lbl = new Label();
                lbl.Text = texto;
                lbl.Location = new Point(20, 20);
                lbl.Size = lbl.GetPreferredSize(new Size(440, 0));
                lbl.MaximumSize = new Size(440, 0);
                lbl.AutoSize = true;
                var fila = FilaBotones(f, si, no, peligro);
                f.Controls.Add(lbl);
                f.Controls.Add(fila);
                f.ClientSize = new Size(480, lbl.GetPreferredSize(new Size(440, 0)).Height + 40 + fila.Height);
                return Mostrar(f, dueno) == DialogResult.OK;
            }
        }

        // Muestra opciones como botones, debajo de un texto. Devuelve el número de la elegida, o -1 si se canceló.
        public static int Elegir(Form dueno, string titulo, string texto, string[] opciones, int recomendada)
        {
            const int ANCHO = 460, M = 24;
            using (var f = Base(titulo, ANCHO))
            {
                int elegida = -1;
                var lbl = new Label();
                lbl.Text = texto;
                lbl.Font = f.Font;   // para medir bien su alto antes de agregarlo a la ventana
                lbl.MaximumSize = new Size(ANCHO - 2 * M, 0);
                lbl.AutoSize = true;
                lbl.Location = new Point(M, 20);
                f.Controls.Add(lbl);
                int y = 20 + lbl.GetPreferredSize(new Size(ANCHO - 2 * M, 0)).Height + 16;
                for (int i = 0; i < opciones.Length; i++)
                {
                    var b = new BotonPlano();
                    b.AutoSize = false;
                    b.Size = new Size(ANCHO - 2 * M, 36);
                    b.Location = new Point(M, y);
                    b.Text = opciones[i];
                    if (i == recomendada) { b.Font = Tema.Negrita; b.Seleccionado = true; }
                    int indice = i;
                    b.Click += delegate { elegida = indice; f.DialogResult = DialogResult.OK; };
                    f.Controls.Add(b);
                    y += 42;
                }
                var cancelar = new BotonPlano();
                cancelar.Text = "Cancelar";
                cancelar.DialogResult = DialogResult.Cancel;
                cancelar.Location = new Point(ANCHO - M - cancelar.PreferredSize.Width, y + 8);
                f.Controls.Add(cancelar);
                f.CancelButton = cancelar;
                f.ClientSize = new Size(ANCHO, y + 8 + cancelar.PreferredSize.Height + 18);
                return Mostrar(f, dueno) == DialogResult.OK ? elegida : -1;
            }
        }

        // Ventana de "Generar reporte": el periodo y qué tipos de fila incluir.
        // periodo: 0 hoy, 1 últimos 7 días, 2 últimos 30 días, 3 todo. tipos: se cambia con lo que se marque.
        public static bool ElegirReporte(Form dueno, ref int periodo, HashSet<string> tipos)
        {
            const int ANCHO = 520, M = 24, UTIL = ANCHO - 2 * M;
            using (var f = Base("Generar reporte", ANCHO))
            {
                Func<string, int, Label> subtitulo = (texto, y) =>
                {
                    var l = new Label();
                    l.Text = texto;
                    l.ForeColor = Color.DimGray;
                    l.AutoSize = true;
                    l.Location = new Point(M, y);
                    f.Controls.Add(l);
                    return l;
                };
                // Botón de una fila de opciones: el elegido queda resaltado y en negrita.
                Func<string, int, int, int, BotonPlano> opcion = (texto, x, y, ancho) =>
                {
                    var b = new BotonPlano();
                    b.AutoSize = false;
                    b.Size = new Size(ancho, 34);
                    b.Location = new Point(x, y);
                    b.Text = texto;
                    f.Controls.Add(b);
                    return b;
                };
                Action<BotonPlano, bool> resaltar = (b, si) =>
                {
                    b.Seleccionado = si;
                    b.Font = si ? Tema.Negrita : Tema.Normal;
                    b.Invalidate();
                };

                // 1. Periodo
                subtitulo("Periodo", 18);
                string[] periodos = { "Hoy", "7 días", "30 días", "Todo" };
                int elegido = periodo;
                int anchoP = (UTIL - 3 * 6) / 4;
                var botonesPeriodo = new List<BotonPlano>();
                for (int i = 0; i < periodos.Length; i++)
                {
                    var b = opcion(periodos[i], M + i * (anchoP + 6), 44, anchoP);
                    int indice = i;
                    b.Click += delegate
                    {
                        elegido = indice;
                        for (int k = 0; k < botonesPeriodo.Count; k++) resaltar(botonesPeriodo[k], k == elegido);
                    };
                    botonesPeriodo.Add(b);
                }
                for (int k = 0; k < botonesPeriodo.Count; k++) resaltar(botonesPeriodo[k], k == elegido);

                // 2. Para quién: cada botón marca las casillas de abajo; se resalta el que coincide con lo marcado.
                subtitulo("¿Para quién es?", 96);
                int anchoQ = (UTIL - 6) / 2;
                var proveedor = opcion("Para el proveedor", M, 122, anchoQ);
                var completo = opcion("Completo (para ti)", M + anchoQ + 6, 122, anchoQ);
                var ayuda = new Label();
                ayuda.ForeColor = Color.DimGray;
                ayuda.AutoSize = false;
                ayuda.Size = new Size(UTIL, 62);   // hasta 3 líneas
                ayuda.Location = new Point(M, 164);
                f.Controls.Add(ayuda);

                // 3. Qué incluir, en dos columnas.
                subtitulo("Qué incluir", 232);
                var casillas = new List<Casilla>();
                int filas = (Tipos.Todos.Length + 1) / 2;
                for (int i = 0; i < Tipos.Todos.Length; i++)
                {
                    var c = new Casilla(Tipos.Plural(Tipos.Todos[i]));
                    c.Tag = Tipos.Todos[i];
                    c.Size = new Size(UTIL / 2, 30);
                    c.Location = new Point(M + (i / filas) * (UTIL / 2), 258 + (i % filas) * 30);
                    c.Marcada = tipos.Contains(Tipos.Todos[i]);
                    f.Controls.Add(c);
                    casillas.Add(c);
                }

                var fila = FilaBotones(f, "Generar", "Cancelar");
                var generar = (BotonPlano)f.AcceptButton;
                Action actualizar = delegate
                {
                    var marcados = casillas.Where(c => c.Marcada).Select(c => (string)c.Tag).ToList();
                    bool esProveedor = marcados.Count == Tipos.ParaProveedor.Length && Tipos.ParaProveedor.All(marcados.Contains);
                    bool esCompleto = marcados.Count == Tipos.Todos.Length;
                    resaltar(proveedor, esProveedor);
                    resaltar(completo, esCompleto);
                    ayuda.Text = esProveedor ? "Solo lo que le sirve como prueba: los cortes que empezaron en el proveedor, los cambios de IP " +
                                               "y las caídas anotadas. Lo demás muestra cosas de tu propia red y le daría excusas."
                               : esCompleto ? "Todo lo registrado, para revisar cómo anda tu red."
                               : marcados.Count == 0 ? "Marca al menos una cosa para incluir."
                               : "Elegiste tú qué incluir.";
                    generar.Enabled = marcados.Count > 0;
                };
                foreach (var c in casillas) c.CambioMarca += delegate { actualizar(); };
                proveedor.Click += delegate { foreach (var c in casillas) c.Marcada = Tipos.ParaProveedor.Contains((string)c.Tag); };
                completo.Click += delegate { foreach (var c in casillas) c.Marcada = true; };
                actualizar();

                f.Controls.Add(fila);
                f.ClientSize = new Size(ANCHO, 258 + filas * 30 + 16 + fila.Height);
                if (Mostrar(f, dueno) != DialogResult.OK) return false;
                periodo = elegido;
                tipos.Clear();
                foreach (var c in casillas) if (c.Marcada) tipos.Add((string)c.Tag);
                return true;
            }
        }

        // Detalle completo de una fila del historial (corte, caída anotada o cambio de IP), con botón para copiarlo.
        // campos: Tipo; Inicio; Fin; Duracion_seg; Equipos o nota; Diagnostico (igual que incidentes.csv).
        // capturas: las capturas automáticas de esa fila (si hay, aparece "Ver capturas").
        public static void Detalle(Form dueno, string[] campos, List<string> capturas)
        {
            const int ANCHO = 600;
            string tipo = campos[0];
            string titulo = Tipos.Titulo(tipo);
            double seg;
            string duracion = double.TryParse(campos[3], System.Globalization.NumberStyles.Any,
                                              System.Globalization.CultureInfo.InvariantCulture, out seg)
                ? (seg < 60 ? Math.Round(seg) + " s" : (int)(seg / 60) + " min " + (int)(seg % 60) + " s") : "";
            var filas = new List<string[]>();
            filas.Add(new[] { "Inicio", campos[1] });
            if (campos[2].Length > 0) filas.Add(new[] { "Fin", campos[2] });
            if (duracion.Length > 0) filas.Add(new[] { "Duración", duracion });
            filas.Add(new[] { tipo == Tipos.Anotado ? "Nota" : tipo == Tipos.CambioIp ? "IP" : tipo == Tipos.Inestable ? "Perdió pings"
                              : tipo == Tipos.NoFueCorte ? "No contestó" : "Se cayó", campos[4] });
            filas.Add(new[] { tipo == Tipos.Anotado ? "En ese momento" : "Diagnóstico", campos[5] });

            using (var f = Base(titulo, ANCHO))
            {
                var encabezado = new Label();
                encabezado.Text = titulo;
                encabezado.Font = Tema.Encabezado;
                encabezado.ForeColor = Tipos.Color(tipo);
                encabezado.AutoSize = true;
                encabezado.Location = new Point(24, 18);
                f.Controls.Add(encabezado);

                int y = 62;
                foreach (var fila in filas)
                {
                    var etiqueta = new Label();
                    etiqueta.Text = fila[0];
                    etiqueta.ForeColor = Color.DimGray;
                    etiqueta.AutoSize = true;
                    etiqueta.Location = new Point(24, y);
                    var valor = new Label();
                    valor.Text = fila[1];
                    valor.Font = f.Font;
                    valor.MaximumSize = new Size(ANCHO - 170, 0);   // el texto largo se acomoda en varias líneas
                    valor.AutoSize = true;
                    valor.Location = new Point(146, y);
                    f.Controls.Add(etiqueta);
                    f.Controls.Add(valor);
                    y += Math.Max(24, valor.GetPreferredSize(new Size(ANCHO - 170, 0)).Height) + 10;
                }

                var cerrar = new BotonPlano();
                cerrar.Text = "Cerrar";
                cerrar.DialogResult = DialogResult.OK;
                cerrar.Location = new Point(ANCHO - 24 - cerrar.PreferredSize.Width, y + 10);
                var copiar = new BotonPlano();
                copiar.Glifo = "\uE8C8";
                copiar.Text = "Copiado ✔";                  // se mide con el texto más largo
                var tamCopiar = copiar.PreferredSize;      // y queda de ese ancho fijo: al cambiar el texto no tapa a "Cerrar"
                copiar.AutoSize = false;
                copiar.Size = tamCopiar;
                copiar.Text = "Copiar";
                copiar.Location = new Point(cerrar.Left - 8 - tamCopiar.Width, y + 10);
                copiar.Click += delegate
                {
                    var sb = new System.Text.StringBuilder(titulo + "\r\n");
                    foreach (var fila in filas) sb.Append(fila[0] + ": " + fila[1] + "\r\n");
                    try { Clipboard.SetText(sb.ToString()); copiar.Text = "Copiado ✔"; } catch { /* otro programa tiene el portapapeles: se puede volver a tocar */ }
                };
                f.Controls.Add(copiar);
                f.Controls.Add(cerrar);
                if (capturas.Count > 0)
                {
                    // Abre la carpeta con la primera captura marcada: las demás de esa fila quedan al lado.
                    var ver = new BotonPlano();
                    ver.Glifo = "";
                    ver.Text = "Ver capturas (" + capturas.Count + ")";
                    ver.Location = new Point(24, y + 10);
                    ver.Click += delegate
                    {
                        try { Process.Start("explorer.exe", "/select,\"" + capturas[0] + "\""); }
                        catch (Exception ex) { Programa.AnotarError("No se pudo abrir la carpeta de capturas", ex); }
                    };
                    f.Controls.Add(ver);
                }
                f.AcceptButton = f.CancelButton = cerrar;
                f.ClientSize = new Size(ANCHO, y + 10 + cerrar.PreferredSize.Height + 20);
                Mostrar(f, dueno);
            }
        }

        // Una lista de casillas para elegir varias cosas (por ejemplo, cuándo tomar capturas automáticas).
        // claves: lo que se guarda; nombres: lo que se ve. elegidas: se cambia con lo marcado.
        public static bool ElegirVarios(Form dueno, string titulo, string texto, string[] claves, string[] nombres, HashSet<string> elegidas)
        {
            const int ANCHO = 520, M = 24;
            using (var f = Base(titulo, ANCHO))
            {
                var lbl = new Label();
                lbl.Text = texto;
                lbl.Font = f.Font;   // para medir bien su alto antes de agregarlo a la ventana
                lbl.MaximumSize = new Size(ANCHO - 2 * M, 0);
                lbl.AutoSize = true;
                lbl.Location = new Point(M, 20);
                f.Controls.Add(lbl);
                int y = 20 + lbl.GetPreferredSize(new Size(ANCHO - 2 * M, 0)).Height + 12;
                var casillas = new List<Casilla>();
                for (int i = 0; i < claves.Length; i++)
                {
                    var c = new Casilla(nombres[i]);
                    c.Size = new Size(ANCHO - 2 * M, 30);
                    c.Location = new Point(M, y);
                    c.Marcada = elegidas.Contains(claves[i]);
                    f.Controls.Add(c);
                    casillas.Add(c);
                    y += 30;
                }
                var fila = FilaBotones(f, "Guardar", "Cancelar");
                f.Controls.Add(fila);
                f.ClientSize = new Size(ANCHO, y + 12 + fila.Height);
                if (Mostrar(f, dueno) != DialogResult.OK) return false;
                elegidas.Clear();
                for (int i = 0; i < claves.Length; i++) if (casillas[i].Marcada) elegidas.Add(claves[i]);
                return true;
            }
        }

        // "Acerca de": versión, autor, licencia, privacidad y enlace al proyecto.
        public static void AcercaDe(Form dueno)
        {
            const int ANCHO = 580;
            using (var f = Base("Acerca de " + Programa.NOMBRE, ANCHO))
            {
                var logo = new PictureBox();
                logo.Image = Logo.Imagen(Tema.Verde, 72);
                logo.SizeMode = PictureBoxSizeMode.AutoSize;
                logo.Location = new Point(24, 24);

                var nombre = new Label();
                nombre.Text = Programa.NOMBRE;
                nombre.Font = Tema.Marca;
                nombre.AutoSize = true;
                nombre.Location = new Point(110, 26);

                var version = new Label();
                version.Text = "Versión " + Programa.VERSION + "   ·   Monitor de cortes de red para Windows";
                version.ForeColor = Color.DimGray;
                version.AutoSize = true;
                version.Location = new Point(112, 68);

                var detalle = new Label();
                detalle.Text =
                    "© " + Programa.ANIO + " " + Programa.AUTOR + ". Software libre con licencia GPL-3.0: puedes usarlo (también en tu " +
                    "negocio), estudiarlo, modificarlo y compartirlo. Si compartes una versión modificada, tiene que llevar su código " +
                    "y la misma licencia.\n\n" +
                    "Privacidad: " + Programa.NOMBRE + " no envía tus datos a ningún servidor propio; todo se guarda en esta PC. " +
                    "Solo se conecta a internet para hacer ping a tus equipos, consultar tu IP pública (ipify, icanhazip y Amazon) " +
                    "y, si lo activas, mandarte avisos por Telegram.";
                detalle.Font = f.Font;   // para medir bien su alto antes de agregarlo a la ventana
                detalle.MaximumSize = new Size(ANCHO - 48, 0);
                detalle.AutoSize = true;
                detalle.Location = new Point(24, 116);

                var enlace = new LinkLabel();
                enlace.Text = Programa.WEB.Replace("https://", "");
                enlace.AutoSize = true;
                enlace.Location = new Point(24, detalle.Top + detalle.GetPreferredSize(new Size(ANCHO - 48, 0)).Height + 16);
                enlace.LinkColor = enlace.ActiveLinkColor = enlace.VisitedLinkColor = Tema.Azul;
                enlace.LinkClicked += delegate { try { Process.Start(Programa.WEB); } catch { /* sin navegador predeterminado */ } };

                int yBotones = enlace.Top + 40;
                var cerrar = new BotonPlano();
                cerrar.Text = "Cerrar";
                cerrar.DialogResult = DialogResult.OK;
                cerrar.Location = new Point(ANCHO - 24 - cerrar.PreferredSize.Width, yBotones);
                var licencia = new BotonPlano();
                licencia.Text = "Ver licencia";
                licencia.Location = new Point(cerrar.Left - 8 - licencia.PreferredSize.Width, yBotones);
                licencia.Click += delegate { try { Process.Start(Programa.WEB + "/blob/main/LICENSE"); } catch { /* sin navegador predeterminado */ } };

                f.Controls.AddRange(new Control[] { logo, nombre, version, detalle, enlace, licencia, cerrar });
                f.AcceptButton = f.CancelButton = cerrar;
                f.ClientSize = new Size(ANCHO, yBotones + cerrar.PreferredSize.Height + 20);
                Mostrar(f, dueno);
            }
        }

        // Texto de ayuda con un solo botón para cerrar.
        public static void Informar(Form dueno, string titulo, string texto)
        {
            const int ANCHO = 660;
            using (var f = Base(titulo, ANCHO))
            {
                var lbl = new Label();
                lbl.Text = texto;
                lbl.Font = f.Font;
                lbl.MaximumSize = new Size(ANCHO - 48, 0);
                lbl.AutoSize = true;
                lbl.Location = new Point(24, 20);
                int alto = lbl.GetPreferredSize(new Size(ANCHO - 48, 0)).Height;
                var cerrar = new BotonPlano();
                cerrar.Text = "Entendido";
                cerrar.DialogResult = DialogResult.OK;
                cerrar.Location = new Point(ANCHO - 24 - cerrar.PreferredSize.Width, 20 + alto + 20);
                f.Controls.Add(lbl);
                f.Controls.Add(cerrar);
                f.AcceptButton = f.CancelButton = cerrar;
                f.ClientSize = new Size(ANCHO, cerrar.Bottom + 20);
                Mostrar(f, dueno);
            }
        }

        // Pide un texto. Devuelve null si se canceló.
        public static string PedirTexto(Form dueno, string titulo, string texto, string valor)
        {
            using (var f = Base(titulo, 520))
            {
                var lbl = new Label();
                lbl.Text = texto;
                lbl.Location = new Point(20, 18);
                lbl.MaximumSize = new Size(480, 0);
                lbl.AutoSize = true;
                var caja = new TextBox();
                caja.Text = valor;
                caja.Width = 480;
                caja.Location = new Point(20, 20 + lbl.GetPreferredSize(new Size(480, 0)).Height + 10);
                var fila = FilaBotones(f, "Guardar", "Cancelar");
                f.Controls.Add(lbl);
                f.Controls.Add(caja);
                f.Controls.Add(fila);
                f.ClientSize = new Size(520, caja.Bottom + 16 + fila.Height);
                f.Shown += delegate { caja.SelectAll(); caja.Focus(); };
                return Mostrar(f, dueno) == DialogResult.OK ? caja.Text : null;
            }
        }
    }
}
