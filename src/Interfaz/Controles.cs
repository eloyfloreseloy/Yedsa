// Controles propios: botón redondeado, tarjeta, franja de estado y panel de dibujo.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Yedsa
{
    // Botón redondeado y plano. Hereda de Button, así que sirve como AcceptButton/CancelButton y con DialogResult.
    class BotonPlano : Button
    {
        public string Glifo;           // ícono opcional (fuente Segoe MDL2 Assets)
        public bool Seleccionado;      // para las pestañas de terminales
        public Color? FondoExterior;   // color de las esquinas si el padre pinta otro fondo
        public bool Peligro;           // botón que cierra o borra algo: rojo suave, y rojo fuerte al pasar el mouse
        bool encima, presionado;

        public BotonPlano()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Tema.Superficie2;
            ForeColor = Tema.Texto;
            Font = Tema.Normal;
            Padding = new Padding(12, 0, 12, 0);
            Cursor = Cursors.Hand;
            AutoSize = true;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var t = TextRenderer.MeasureText(Text, Font);
            int ancho = t.Width + Padding.Horizontal + (string.IsNullOrEmpty(Glifo) ? 0 : 24);
            return new Size(Math.Max(ancho, 40), Math.Max(34, t.Height + 14));
        }

        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = presionado = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { presionado = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { presionado = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(FondoExterior ?? (Parent != null ? Parent.BackColor : Tema.Fondo));
            Color relleno = !Enabled ? Tema.Superficie : presionado ? Tema.Borde : (encima || Seleccionado) ? Tema.Hover : BackColor;
            Color borde = Seleccionado ? Tema.TextoSec : Tema.Borde;
            Color texto = Enabled ? ForeColor : Tema.TextoTenue;
            if (Peligro && Enabled)
            {
                bool fuerte = encima || presionado;
                relleno = presionado ? ControlPaint.Dark(Tema.CerrarHover, 0.1f) : fuerte ? Tema.CerrarHover : Tema.RojoFondo;
                borde = fuerte ? relleno : Tema.Rojo;
                texto = fuerte ? Color.White : Tema.RojoTexto;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Tema.Rellenar(g, relleno, r, 8);
            Tema.Contorno(g, borde, r, 8);

            var medida = TextRenderer.MeasureText(Text, Font);
            int anchoGlifo = string.IsNullOrEmpty(Glifo) ? 0 : 24;
            int x = (Width - medida.Width - anchoGlifo) / 2;
            if (anchoGlifo > 0)
                TextRenderer.DrawText(g, Glifo, Tema.Iconos, new Rectangle(x, 0, 20, Height), texto,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + anchoGlifo, 0, medida.Width + 2, Height), texto,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Casilla para marcar con el tema del programa (la de Windows se ve blanca en el tema oscuro).
    class Casilla : Control
    {
        public event EventHandler CambioMarca;
        bool marcada, encima;

        public Casilla(string texto)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = texto;
            Font = Tema.Normal;
            Height = 30;
            Cursor = Cursors.Hand;
        }

        public bool Marcada
        {
            get { return marcada; }
            set
            {
                if (marcada == value) return;
                marcada = value;
                Invalidate();
                if (CambioMarca != null) CambioMarca(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Marcada = !Marcada; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Tema.Fondo);
            var caja = new RectangleF(1.5f, Height / 2f - 9, 18, 18);
            if (marcada)
            {
                Tema.Rellenar(g, Tema.Verde, caja, 5);
                TextRenderer.DrawText(g, "\uE73E", Tema.IconosChicos, Rectangle.Round(caja), Tema.Fondo,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                Tema.Rellenar(g, Tema.Superficie2, caja, 5);
                Tema.Contorno(g, encima ? Tema.TextoSec : Tema.Borde, caja, 5);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(30, 0, Width - 30, Height), Tema.Texto,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Divisor horizontal que se arrastra: cambia en vivo el alto del control de arriba y, al soltar,
    // deja que la ventana ajuste el alto final (por ejemplo, a filas enteras).
    class DivisorArrastrable : Control
    {
        public Control Arriba;              // control cuyo alto cambia
        public Func<int, int> Limitar;      // alto pedido -> alto permitido mientras se arrastra
        public Func<int, int> AlSoltar;     // alto al soltar -> alto final
        bool arrastrando, encima;
        int inicioMouseY, inicioAlto;

        public DivisorArrastrable()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 14;
            Cursor = Cursors.SizeNS;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Arriba == null) return;
            arrastrando = true;
            Capture = true;                     // sigue recibiendo el mouse aunque salga de la rayita
            inicioMouseY = Cursor.Position.Y;   // en coordenadas de pantalla: el divisor se mueve mientras se arrastra
            inicioAlto = Arriba.Height;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!arrastrando) return;
            int alto = Limitar(inicioAlto + Cursor.Position.Y - inicioMouseY);
            if (alto == Arriba.Height) return;
            Arriba.Height = alto;
            Parent.Update();   // se redibuja ya, no al soltar
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!arrastrando) return;
            arrastrando = false;
            Capture = false;
            Arriba.Height = AlSoltar(Arriba.Height);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Tema.Fondo);
            Tema.Rellenar(e.Graphics, arrastrando || encima ? Tema.TextoTenue : Tema.Borde,
                          new RectangleF(Width / 2f - 24, Height / 2f - 2, 48, 4), 2);
        }
    }

    // Barra de desplazamiento delgada y redondeada (la de Windows no se puede pintar con el tema).
    class BarraDesplazamiento : Control
    {
        int valor, contenido, visible, inicioMouse, inicioValor;
        bool arrastrando, encima;
        public event EventHandler Cambio;

        public BarraDesplazamiento()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = 12;
        }

        // contenido: alto total de lo que se desplaza; visible: alto de lo que se ve.
        public void Configurar(int contenido, int visible)
        {
            this.contenido = contenido;
            this.visible = visible;
            Visible = contenido > visible;
            Valor = valor;
            Invalidate();
        }

        public int Valor
        {
            get { return valor; }
            set
            {
                int v = Math.Max(0, Math.Min(value, Math.Max(0, contenido - visible)));
                if (v == valor) return;
                valor = v;
                Invalidate();
                if (Cambio != null) Cambio(this, EventArgs.Empty);
            }
        }

        Rectangle Pulgar()
        {
            if (contenido <= visible || Height <= 0) return Rectangle.Empty;
            int alto = Math.Max(30, Height * visible / contenido);
            int y = (Height - alto) * valor / Math.Max(1, contenido - visible);
            return new Rectangle(3, y, Width - 6, alto);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Tema.Superficie);
            var r = Pulgar();
            if (!r.IsEmpty) Tema.Rellenar(e.Graphics, arrastrando || encima ? Tema.TextoTenue : Tema.Borde, r, 3);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var r = Pulgar();
            if (r.Contains(e.Location))
            {
                arrastrando = true;
                inicioMouse = e.Y;
                inicioValor = valor;
                Invalidate();
            }
            else Valor += e.Y < r.Y ? -visible : visible;   // clic fuera del pulgar: avanza una página
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!arrastrando) return;
            int recorrido = Height - Pulgar().Height;
            if (recorrido > 0) Valor = inicioValor + (e.Y - inicioMouse) * (contenido - visible) / recorrido;
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); arrastrando = false; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // Tarjeta: panel con fondo redondeado. Opcionalmente con borde de color (ej. rojo si un equipo se cae).
    class Tarjeta : Panel
    {
        Color? colorBorde;

        public Tarjeta(Color relleno)
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = relleno;
            Padding = new Padding(10);
        }

        public Color? ColorBorde
        {
            get { return colorBorde; }
            set { if (colorBorde != value) { colorBorde = value; Invalidate(); } }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Tema.Fondo);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Tema.Rellenar(e.Graphics, BackColor, r, 12);
            if (colorBorde.HasValue) Tema.Contorno(e.Graphics, colorBorde.Value, r, 12);
        }
    }

    class Banner : Control
    {
        public enum Tipo { Iniciando, Ok, Corte, Mantenimiento }
        Tipo tipo = Tipo.Iniciando;
        string linea1 = "Iniciando...", linea2 = "";
        public readonly BotonPlano Boton = new BotonPlano();

        public Banner()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Boton.Font = Tema.Negrita;
            Boton.BackColor = Tema.Superficie2;
            Controls.Add(Boton);
            Boton.SizeChanged += delegate { PerformLayout(); };
            Boton.FondoExterior = ColorFondo();
        }

        public void Poner(Tipo t, string l1, string l2)
        {
            if (t == tipo && l1 == linea1 && l2 == linea2) return;
            tipo = t;
            linea1 = l1;
            linea2 = l2;
            Boton.FondoExterior = ColorFondo();
            Boton.Invalidate();
            Invalidate();
        }

        Color ColorFondo() { return tipo == Tipo.Ok ? Tema.VerdeFondo : tipo == Tipo.Corte ? Tema.RojoFondo : Tema.Superficie2; }
        Color ColorTexto()
        {
            return tipo == Tipo.Ok ? Tema.VerdeTexto : tipo == Tipo.Corte ? Tema.RojoTexto : tipo == Tipo.Mantenimiento ? Tema.Cian : Tema.TextoSec;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Boton.Location = new Point(Width - Boton.Width - 16, (Height - Boton.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Tema.Fondo);
            Dibujar(e.Graphics, new Rectangle(0, 0, Width, Height), Boton.Left - 64);
        }

        // La franja (sin el bot\u00F3n) en cualquier lugar: en la pantalla o en una captura autom\u00E1tica.
        public void Dibujar(Graphics g, Rectangle r, int anchoTexto)
        {
            Tema.Rellenar(g, ColorFondo(), new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1), 12);
            Color c = ColorTexto();
            string glifo = tipo == Tipo.Ok ? "\uE73E" : tipo == Tipo.Corte ? "\uE7BA" : tipo == Tipo.Mantenimiento ? "\uE90F" : "\uE895";
            TextRenderer.DrawText(g, glifo, Tema.IconosFranja, new Rectangle(r.X + 14, r.Y, 32, r.Height), c,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            bool dos = linea2.Length > 0;
            TextRenderer.DrawText(g, linea1, Tema.Grande, new Rectangle(r.X + 54, r.Y + (dos ? r.Height / 2 - 23 : 0), anchoTexto, dos ? 24 : r.Height), c,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (dos)
                TextRenderer.DrawText(g, linea2, Tema.Normal, new Rectangle(r.X + 54, r.Y + r.Height / 2 + 1, anchoTexto, 22), c,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    class PanelDibujo : Panel
    {
        public PanelDibujo() { DoubleBuffered = true; ResizeRedraw = true; }
    }
}
