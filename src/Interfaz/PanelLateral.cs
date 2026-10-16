// Panel lateral plegable con secciones, opciones e interruptores.
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
    abstract class ControlLateral : Control
    {
        bool plegado;

        protected ControlLateral()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Tema.Normal;
            ForeColor = Tema.Texto;
        }

        public bool Plegado
        {
            get { return plegado; }
            set { plegado = value; Invalidate(); }
        }
    }

    class SeccionLateral : ControlLateral
    {
        public SeccionLateral(string titulo) { Text = titulo; Height = 30; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            if (Plegado)
                using (var p = new Pen(Tema.Borde))
                    e.Graphics.DrawLine(p, 10, Height / 2 + 4, Width - 10, Height / 2 + 4);
            else
                TextRenderer.DrawText(e.Graphics, Text, Tema.Pequena, new Point(12, 12), Tema.TextoTenue);
        }
    }

    // Opción del panel: ícono + texto. Si es interruptor, muestra un switch que se prende y apaga al tocarlo.
    class ItemLateral : ControlLateral
    {
        public string Glifo;
        public bool EsInterruptor;
        public bool Peligro;   // opción que cierra el programa: se pone roja al pasar el mouse
        bool encendido, encima;

        public ItemLateral(string glifo, string texto)
        {
            Glifo = glifo;
            Text = texto;
            Height = 38;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return encendido; }
            set { encendido = value; Invalidate(); }
        }

        // Ícono en verde mientras la opción está trabajando (por ejemplo, con su ventanita abierta).
        bool activo;
        public bool Activo
        {
            get { return activo; }
            set { activo = value; Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            if (EsInterruptor) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent.BackColor);
            bool rojo = Peligro && encima;
            if (encima) Tema.Rellenar(g, rojo ? Tema.RojoFondo : Tema.Hover, new RectangleF(2, 1, Width - 4, Height - 2), 8);

            Color colorGlifo = rojo ? Tema.Rojo : EsInterruptor ? (encendido ? Tema.Verde : Tema.TextoTenue) : activo ? Tema.Verde : Tema.TextoSec;
            TextRenderer.DrawText(g, Glifo, Tema.Iconos, new Rectangle(4, 0, 36, Height), colorGlifo,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Plegado) return;

            int finTexto = EsInterruptor ? Width - 52 : Width - 8;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(44, 0, finTexto - 44, Height), rojo ? Tema.RojoTexto : ForeColor,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (!EsInterruptor) return;

            // Switch tipo celular.
            var pista = new RectangleF(Width - 46, Height / 2f - 8, 32, 16);
            Tema.Rellenar(g, encendido ? Tema.Verde : Tema.Borde, pista, 8);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(encendido ? Tema.Fondo : Tema.TextoSec))
                g.FillEllipse(b, encendido ? pista.Right - 14 : pista.X + 2, pista.Y + 2, 12, 12);
        }
    }

    class PanelLateral : Panel
    {
        public const int ANCHO = 240, ANCHO_PLEGADO = 52;
        readonly List<ControlLateral> arriba = new List<ControlLateral>(), abajo = new List<ControlLateral>();
        readonly ToolTip ayuda = new ToolTip();
        bool plegado;

        public PanelLateral()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Tema.Superficie;
            Width = ANCHO;
        }

        public T Agregar<T>(T c, bool alFinal) where T : ControlLateral
        {
            (alFinal ? abajo : arriba).Add(c);
            Controls.Add(c);
            c.TextChanged += delegate { ActualizarAyuda(c); };
            ActualizarAyuda(c);
            return c;
        }

        void ActualizarAyuda(ControlLateral c)
        {
            if (c is ItemLateral) ayuda.SetToolTip(c, plegado ? c.Text : null);
        }

        public bool Plegado
        {
            get { return plegado; }
            set
            {
                plegado = value;
                Width = value ? ANCHO_PLEGADO : ANCHO;
                foreach (var c in arriba.Concat(abajo)) { c.Plegado = value; ActualizarAyuda(c); }
                PerformLayout();
                Invalidate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int y = 8;
            foreach (var c in arriba) { c.SetBounds(6, y, Width - 13, c.Height); y += c.Height + 1; }
            int yb = Height - 8;
            for (int i = abajo.Count - 1; i >= 0; i--) { yb -= abajo[i].Height; abajo[i].SetBounds(6, yb, Width - 13, abajo[i].Height); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Tema.Borde))
                e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height);
        }
    }
}
