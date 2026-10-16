// Colores (tema oscuro y claro), fuentes y ayudas para dibujar con esquinas redondeadas.
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
    static class Tema
    {
        // Paleta del tema elegido. Se llena con Aplicar() al abrir el programa, antes de crear cualquier ventana.
        public static bool Oscuro { get; private set; }
        public static Color Fondo, Superficie, Superficie2, Hover, Borde;
        public static Color Texto, TextoSec, TextoTenue;
        public static Color Verde, VerdeFondo, VerdeTexto;
        public static Color Rojo, RojoFondo, RojoTexto;
        public static Color Amarillo, Naranja, Rosa, Morado, Azul, Cian, Lima, Dorado;

        // Iguales en los dos temas: las terminales siempre se ven como el CMD de Windows.
        public static readonly Color Consola = Color.FromArgb(12, 12, 12);
        public static readonly Color ConsolaTexto = Color.FromArgb(204, 204, 204);
        public static readonly Color ConsolaTitulo = Color.FromArgb(240, 240, 236);
        public static readonly Color ConsolaRojo = Color.FromArgb(250, 170, 168);
        public static readonly Color ConsolaBorde = Color.FromArgb(48, 48, 46);
        public static readonly Color CerrarHover = Color.FromArgb(196, 43, 28);

        public static void Aplicar(bool oscuro)
        {
            Oscuro = oscuro;
            if (oscuro)
            {
                Fondo = Color.FromArgb(14, 14, 13);
                Superficie = Color.FromArgb(23, 23, 22);
                Superficie2 = Color.FromArgb(33, 33, 31);
                Hover = Color.FromArgb(46, 46, 43);
                Borde = Color.FromArgb(52, 52, 49);
                Texto = Color.FromArgb(236, 235, 230);
                TextoSec = Color.FromArgb(166, 164, 157);
                TextoTenue = Color.FromArgb(118, 117, 111);
                Verde = Color.FromArgb(99, 186, 72);
                VerdeFondo = Color.FromArgb(25, 45, 19);
                VerdeTexto = Color.FromArgb(172, 220, 130);
                Rojo = Color.FromArgb(226, 75, 74);
                RojoFondo = Color.FromArgb(74, 22, 22);
                RojoTexto = Color.FromArgb(250, 170, 168);
                Amarillo = Color.FromArgb(239, 159, 39);
                Naranja = Color.FromArgb(242, 130, 84);
                Rosa = Color.FromArgb(240, 128, 170);
                Morado = Color.FromArgb(180, 172, 242);
                Azul = Color.FromArgb(135, 185, 236);
                Cian = Color.FromArgb(94, 196, 204);
                Lima = Color.FromArgb(166, 206, 74);
                Dorado = Color.FromArgb(222, 192, 92);
            }
            else
            {
                Fondo = Color.FromArgb(243, 242, 238);
                Superficie = Color.FromArgb(255, 255, 255);
                Superficie2 = Color.FromArgb(240, 239, 234);
                Hover = Color.FromArgb(228, 227, 221);
                Borde = Color.FromArgb(214, 212, 205);
                Texto = Color.FromArgb(30, 30, 28);
                TextoSec = Color.FromArgb(92, 91, 87);
                TextoTenue = Color.FromArgb(136, 135, 128);
                Verde = Color.FromArgb(76, 160, 46);
                VerdeFondo = Color.FromArgb(230, 242, 218);
                VerdeTexto = Color.FromArgb(39, 92, 14);
                Rojo = Color.FromArgb(214, 60, 58);
                RojoFondo = Color.FromArgb(252, 232, 231);
                RojoTexto = Color.FromArgb(150, 36, 36);
                Amarillo = Color.FromArgb(186, 117, 23);
                Naranja = Color.FromArgb(196, 78, 28);
                Rosa = Color.FromArgb(178, 44, 104);
                Morado = Color.FromArgb(83, 74, 183);
                Azul = Color.FromArgb(24, 95, 165);
                Cian = Color.FromArgb(18, 120, 130);
                Lima = Color.FromArgb(84, 122, 10);
                Dorado = Color.FromArgb(150, 112, 0);
            }
        }

        public static readonly Font Normal = new Font("Segoe UI", 9.75f);
        public static readonly Font Negrita = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        public static readonly Font Grande = new Font("Segoe UI", 11f, FontStyle.Bold);
        public static readonly Font Titulo = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        public static readonly Font Pequena = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        public static readonly Font Iconos = new Font("Segoe MDL2 Assets", 11f);
        public static readonly Font IconosChicos = new Font("Segoe MDL2 Assets", 9f);

        public static GraphicsPath Redondeado(RectangleF r, float radio)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Rellenar(Graphics g, Color c, RectangleF r, float radio)
        {
            var antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Redondeado(r, radio))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
            g.SmoothingMode = antes;
        }

        public static void Contorno(Graphics g, Color c, RectangleF r, float radio)
        {
            var antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Redondeado(r, radio))
            using (var pen = new Pen(c))
                g.DrawPath(pen, p);
            g.SmoothingMode = antes;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int atributo, ref int valor, int largo);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr h, string app, string id);

        static void AlTenerHandle(Control c, Action<IntPtr> accion)
        {
            if (c.IsHandleCreated) accion(c.Handle);
            c.HandleCreated += delegate { accion(c.Handle); };
        }

        // Barra de título de Windows en oscuro (para las ventanitas de diálogo). En el tema claro no hace nada.
        public static void BarraOscura(Form f)
        {
            if (!Oscuro) return;
            AlTenerHandle(f, h =>
            {
                try
                {
                    int si = 1;
                    if (DwmSetWindowAttribute(h, 20, ref si, 4) != 0) DwmSetWindowAttribute(h, 19, ref si, 4);
                }
                catch { }
            });
        }

        // Barras de desplazamiento oscuras (Windows 10 1809 o más nuevo; si no, quedan como siempre).
        // siempre = true para las terminales, que son negras en los dos temas.
        public static void ScrollOscuro(Control c, bool siempre = false)
        {
            if (!Oscuro && !siempre) return;
            AlTenerHandle(c, h => { try { SetWindowTheme(h, "DarkMode_Explorer", null); } catch { } });
        }

        public static void Grilla(DataGridView g, Color fondo, bool conLineas)
        {
            g.BackgroundColor = fondo;
            g.BorderStyle = BorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersDefaultCellStyle.BackColor = fondo;
            g.ColumnHeadersDefaultCellStyle.ForeColor = TextoSec;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = fondo;
            g.ColumnHeadersDefaultCellStyle.Font = Pequena;
            g.DefaultCellStyle.BackColor = fondo;
            g.DefaultCellStyle.ForeColor = Texto;
            g.DefaultCellStyle.SelectionBackColor = Hover;
            g.DefaultCellStyle.SelectionForeColor = Texto;
            g.RowHeadersDefaultCellStyle.BackColor = fondo;
            g.RowHeadersDefaultCellStyle.ForeColor = TextoSec;
            g.GridColor = Borde;
            g.CellBorderStyle = conLineas ? DataGridViewCellBorderStyle.Single : DataGridViewCellBorderStyle.SingleHorizontal;
            foreach (Control c in g.Controls) ScrollOscuro(c);
        }

        // Pasa una ventana común (labels, cajas de texto, listas, grillas, botones) al tema oscuro.
        public static void AplicarA(Control raiz)
        {
            if (raiz is Form)
            {
                raiz.BackColor = Fondo;
                raiz.ForeColor = Texto;
                BarraOscura((Form)raiz);
            }
            foreach (Control c in raiz.Controls)
            {
                if (c is Label)
                {
                    if (c.ForeColor == Color.DimGray || c.ForeColor == Color.Gray) c.ForeColor = TextoSec;
                }
                else if (c is TextBox || c is ListBox)
                {
                    c.BackColor = Superficie2;
                    c.ForeColor = Texto;
                    if (c is TextBox) ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                    if (c is ListBox) ((ListBox)c).BorderStyle = BorderStyle.FixedSingle;
                    ScrollOscuro(c);
                }
                else if (c is DataGridView)
                {
                    Grilla((DataGridView)c, Superficie, true);
                }
                else if (c is Button && !(c is BotonPlano))
                {
                    var b = (Button)c;
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = Superficie2;
                    b.ForeColor = Texto;
                    b.FlatAppearance.BorderColor = Borde;
                    b.FlatAppearance.MouseOverBackColor = Hover;
                }
                AplicarA(c);
            }
        }
    }
}
