// Terminales con ping -t reales, cambio de vista y captura de pantalla.
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
    partial class VentanaPrincipal
    {
        // ---------- Terminales (ping -t reales) ----------

        // Hasta 6 terminales en una sola pantalla; con más, páginas de 6 en 6 con botones arriba.
        void CrearTerminales()
        {
            vistaTerminales.Dock = DockStyle.Fill;
            vistaTerminales.Visible = false;
            vistaTerminales.BackColor = Tema.Fondo;

            var grupos = new List<List<Equipo>>();
            for (int i = 0; i < equipos.Count; i += POR_PAGINA)
                grupos.Add(equipos.Skip(i).Take(POR_PAGINA).ToList());

            if (grupos.Count == 1)
            {
                vistaTerminales.Controls.Add(CrearCuadricula(grupos[0]));
                return;
            }
            paginas = new List<Control>();
            botonesPagina = new List<BotonPlano>();
            var barraPaginas = new FlowLayoutPanel();
            barraPaginas.Dock = DockStyle.Top;
            barraPaginas.Height = 46;
            barraPaginas.Padding = new Padding(0, 2, 0, 8);
            for (int i = 0; i < grupos.Count; i++)
            {
                var cuadricula = CrearCuadricula(grupos[i]);
                cuadricula.Tag = grupos[i];
                paginas.Add(cuadricula);
                vistaTerminales.Controls.Add(cuadricula);
                var b = new BotonPlano();
                b.Margin = new Padding(0, 0, 8, 0);
                int indice = i;
                b.Click += delegate { MostrarPagina(indice); };
                botonesPagina.Add(b);
                barraPaginas.Controls.Add(b);
            }
            vistaTerminales.Controls.Add(barraPaginas);
            MostrarPagina(0);
        }

        void MostrarPagina(int indice)
        {
            for (int i = 0; i < paginas.Count; i++)
            {
                paginas[i].Visible = i == indice;
                botonesPagina[i].Seleccionado = i == indice;
                botonesPagina[i].Invalidate();
            }
        }

        // 1-2 equipos lado a lado, 3-4 en 2x2, 5-6 en 3x2.
        TableLayoutPanel CrearCuadricula(List<Equipo> grupo)
        {
            int n = grupo.Count;
            int cols = n <= 2 ? n : n <= 4 ? 2 : 3;
            int filas = (n + cols - 1) / cols;
            var tabla = new TableLayoutPanel();
            tabla.Dock = DockStyle.Fill;
            tabla.BackColor = Tema.Fondo;
            tabla.ColumnCount = cols;
            tabla.RowCount = filas;
            for (int c = 0; c < cols; c++) tabla.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
            for (int r = 0; r < filas; r++) tabla.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / filas));

            foreach (var e in grupo)
            {
                var t = new Terminal();
                t.Equipo = e;

                t.Caja = new TextBox();
                t.Caja.Multiline = true;
                t.Caja.ReadOnly = true;
                t.Caja.WordWrap = cols >= 3; // en 3 columnas la línea de ping no entra a lo ancho
                t.Caja.ScrollBars = ScrollBars.Vertical;
                t.Caja.BorderStyle = BorderStyle.None;
                t.Caja.BackColor = Tema.Consola;
                t.Caja.ForeColor = Tema.ConsolaTexto;
                t.Caja.Font = new Font("Consolas", cols >= 3 ? 9.5f : 10.5f);
                t.Caja.Dock = DockStyle.Fill;
                Tema.ScrollOscuro(t.Caja, true);

                t.Titulo = new Label();
                t.Titulo.Dock = DockStyle.Top;
                t.Titulo.Height = 30;
                t.Titulo.Padding = new Padding(2, 0, 0, 4);
                t.Titulo.TextAlign = ContentAlignment.MiddleLeft;
                t.Titulo.Font = Tema.Negrita;
                t.Titulo.ForeColor = Tema.ConsolaTitulo;
                t.Titulo.BackColor = Tema.Consola;

                t.Marco = new Tarjeta(Tema.Consola);
                t.Marco.Dock = DockStyle.Fill;
                t.Marco.Margin = new Padding(0, 0, 8, 8);
                t.Marco.Padding = new Padding(12, 6, 6, 10);
                t.Marco.ColorBorde = Tema.ConsolaBorde;
                t.Marco.Controls.Add(t.Caja);
                t.Marco.Controls.Add(t.Titulo);
                tabla.Controls.Add(t.Marco);
                terminales.Add(t);
            }
            return tabla;
        }

        bool terminalesIniciadas;

        void IniciarTerminales()
        {
            if (terminalesIniciadas) return;
            terminalesIniciadas = true;
            foreach (var t in terminales) IniciarTerminal(t);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            IniciarTerminales();
            AjustarAltoEquipos();
        }

        // Al arrancar con Windows la ventana empieza escondida (solo el ícono junto al reloj).
        protected override void SetVisibleCore(bool value)
        {
            if (arrancarOculto)
            {
                arrancarOculto = false;
                if (!IsHandleCreated) CreateHandle();
                IniciarTerminales();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        void IniciarTerminal(Terminal t)
        {
            AgregarLinea(t, "C:\\>ping -t " + t.Equipo.Ip, Reloj.Ahora, false);
            try
            {
                var psi = new ProcessStartInfo("ping.exe", "-t " + t.Equipo.Ip);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                var p = new Process();
                p.StartInfo = psi;
                p.OutputDataReceived += (s, a) =>
                {
                    if (a.Data == null || cerrando) return;
                    DateTime cuando = Reloj.Ahora;
                    string texto = a.Data;
                    try { BeginInvoke((Action)(() => AgregarLinea(t, texto, cuando, true))); } catch { }
                };
                p.Start();
                Job.Asignar(p);
                p.BeginOutputReadLine();
                t.Proc = p;
            }
            catch (Exception ex)
            {
                AgregarLinea(t, "No se pudo iniciar ping: " + ex.Message, Reloj.Ahora, false);
            }
        }

        void AgregarLinea(Terminal t, string texto, DateTime cuando, bool esSalidaPing)
        {
            if (t.Caja.IsDisposed) return;
            if (esSalidaPing) EscribirTerminal(t, texto, cuando);
            string prefijo = esSalidaPing && chkHora.Checked && texto.Length > 0 ? "[" + cuando.ToString("HH:mm:ss") + "] " : "";
            t.Caja.AppendText(prefijo + texto + "\r\n");
            if (++t.Lineas > 3000)
            {
                var lineas = t.Caja.Lines;
                t.Caja.Lines = lineas.Skip(lineas.Length - 1500).ToArray();
                t.Lineas = 1500;
                IrAlFinal(t);
            }
        }

        static void IrAlFinal(Terminal t)
        {
            t.Caja.SelectionStart = t.Caja.TextLength;
            t.Caja.ScrollToCaret();
        }

        void ActualizarTitulos()
        {
            foreach (var t in terminales)
            {
                var e = t.Equipo;
                string texto = (e.Indice + 1) + ".  " + e.Nombre + "   ·   ping -t " + e.Ip + (e.Apagado && !(e.SeApaga && (Reloj.Ahora - e.PrimerFallo).TotalMinutes < MINUTOS_AUSENCIA) ? "     (apagado)"
                              : e.Caido ? "     ⚠ SIN RESPUESTA" : "");
                Color color = e.Caido ? Tema.ConsolaRojo : e.FallosSeguidos > 0 ? Tema.Amarillo : Tema.ConsolaTitulo;
                if (t.Titulo.Text != texto) t.Titulo.Text = texto;
                if (t.Titulo.ForeColor != color) t.Titulo.ForeColor = color;
                t.Marco.ColorBorde = e.Caido ? Tema.Rojo : e.FallosSeguidos > 0 ? Tema.Amarillo : Tema.ConsolaBorde;
            }
            if (paginas == null) return;
            for (int i = 0; i < paginas.Count; i++)
            {
                var g = (List<Equipo>)paginas[i].Tag;
                int desde = g[0].Indice + 1, hasta = g[g.Count - 1].Indice + 1;
                string texto = (desde == hasta ? "Equipo " + desde : "Equipos " + desde + " a " + hasta) +
                               (g.Any(x => x.Caido) ? "   ⚠" : "");
                if (botonesPagina[i].Text != texto) botonesPagina[i].Text = texto;
                botonesPagina[i].ForeColor = g.Any(x => x.Caido) ? Tema.RojoTexto : Tema.Texto;
            }
        }

        void CambiarVista()
        {
            bool aTerminales = !vistaTerminales.Visible;
            vistaTerminales.Visible = aTerminales;
            vistaPrincipal.Visible = !aTerminales;
            banner.Boton.Text = aTerminales ? "Volver al panel" : "Ver terminales";
            banner.Boton.Glifo = aTerminales ? "\uE80F" : "\uE756";
            banner.Boton.Invalidate();
            if (aTerminales) foreach (var t in terminales) IrAlFinal(t);
        }

        // Guarda una imagen de toda la pantalla (incluye otros programas abiertos, como el del proveedor).
        void CapturarPantalla()
        {
            try
            {
                string dir = Path.Combine(carpeta, "capturas");
                Directory.CreateDirectory(dir);
                string ruta = Path.Combine(dir, "captura_" + Reloj.Ahora.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                Rectangle area = SystemInformation.VirtualScreen;
                using (var bmp = new Bitmap(area.Width, area.Height))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                    bmp.Save(ruta, System.Drawing.Imaging.ImageFormat.Png);
                }
                DejarUltimos(dir, "captura_*.png", MAX_CAPTURAS);
                tray.ShowBalloonTip(5000, "Captura guardada", Path.GetFileName(ruta) + "  (en registros\\capturas)", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo guardar la captura: " + ex.Message, Programa.NOMBRE);
            }
        }
    }
}
