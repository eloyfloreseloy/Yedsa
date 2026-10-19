// Ventana sin la barra de Windows: barra de título propia, bordes para redimensionar y menú oscuro.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Yedsa
{
    // Barra de título propia: ícono, nombre y botones minimizar / maximizar / cerrar. Se arrastra para mover.
    class BarraTitulo : Control
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        const int ANCHO_BOTON = 46;
        readonly Form form;
        int encima = -1;
        DateTime ultimoClic = DateTime.MinValue;
        public string Subtitulo;   // texto gris al lado del nombre (la versión)
        bool arrastreMaximizada;
        Point puntoPresion;

        public BarraTitulo(Form f)
        {
            form = f;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = 38;
            Dock = DockStyle.Top;
            BackColor = Tema.Fondo;
            f.TextChanged += delegate { Invalidate(); };
            f.Resize += delegate { Invalidate(); };
        }

        Rectangle Boton(int i) { return new Rectangle(Width - (3 - i) * ANCHO_BOTON, 0, ANCHO_BOTON, Height); }

        int BotonEn(Point p)
        {
            for (int i = 0; i < 3; i++) if (Boton(i).Contains(p)) return i;
            return -1;
        }

        void AlternarMaximizar()
        {
            form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // Maximizada: al jalar la barra, la ventana vuelve a su tamaño normal pegada al mouse (como en Windows).
            if (arrastreMaximizada && e.Button == MouseButtons.Left &&
                (Math.Abs(e.X - puntoPresion.X) > 4 || Math.Abs(e.Y - puntoPresion.Y) > 4))
            {
                arrastreMaximizada = false;
                float proporcion = (float)e.X / Width;      // dónde estaba el mouse a lo ancho de la barra
                Point mouse = Cursor.Position;
                form.WindowState = FormWindowState.Normal;
                form.Location = new Point(mouse.X - (int)(form.Width * proporcion), mouse.Y - e.Y - form.Padding.Top);
                ReleaseCapture();
                SendMessage(form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                return;
            }
            int b = BotonEn(e.Location);
            if (b != encima) { encima = b; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { encima = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || BotonEn(e.Location) >= 0) return;
            if ((DateTime.Now - ultimoClic).TotalMilliseconds < SystemInformation.DoubleClickTime)
            {
                ultimoClic = DateTime.MinValue;
                AlternarMaximizar();
                return;
            }
            ultimoClic = DateTime.Now;
            if (form.WindowState == FormWindowState.Maximized)
            {
                arrastreMaximizada = true;   // si se mueve el mouse con el botón apretado, se restaura (ver OnMouseMove)
                puntoPresion = e.Location;
                return;
            }
            ReleaseCapture();
            SendMessage(form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN en HTCAPTION: Windows mueve la ventana
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            arrastreMaximizada = false;
            if (e.Button != MouseButtons.Left) return;
            int b = BotonEn(e.Location);
            if (b == 0) form.WindowState = FormWindowState.Minimized;
            else if (b == 1) AlternarMaximizar();
            else if (b == 2) form.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            bool alerta = form.Text.StartsWith("⚠");
            // Logo: círculo verde (rojo si hay corte) con el símbolo blanco adentro.
            var circulo = new RectangleF(13, (Height - 22) / 2f, 22, 22);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(alerta ? Tema.Rojo : Tema.Verde)) g.FillEllipse(b, circulo);
            g.SmoothingMode = SmoothingMode.None;
            circulo.Inflate(-4.5f, -4.5f);
            Logo.Dibujar(g, circulo, Color.White);
            TextRenderer.DrawText(g, form.Text, Tema.Titulo, new Rectangle(44, 0, Width - 3 * ANCHO_BOTON - 54, Height),
                                  alerta ? Tema.RojoTexto : Tema.Texto, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (!string.IsNullOrEmpty(Subtitulo))   // ej. la versión, chiquita y en gris junto al nombre
            {
                int ancho = TextRenderer.MeasureText(g, form.Text, Tema.Titulo, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, Subtitulo, Tema.Normal, new Rectangle(44 + ancho + 10, 1, 200, Height), Tema.TextoTenue,
                                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            string[] glifos = { "\uE921", form.WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922", "\uE8BB" };
            for (int i = 0; i < 3; i++)
            {
                var r = Boton(i);
                if (encima == i)
                    using (var b = new SolidBrush(i == 2 ? Tema.CerrarHover : Tema.Hover))
                        g.FillRectangle(b, r);
                TextRenderer.DrawText(g, glifos[i], Tema.IconosChicos, r, encima == 2 && i == 2 ? Color.White : Tema.TextoSec,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // Ventana sin borde de Windows, pero que se puede redimensionar desde los bordes y minimizar desde la barra de tareas.
    class FormSinBorde : Form
    {
        const int BORDE = 8;   // zona de los bordes para agrandar o achicar la ventana

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int indice);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int indice, int valor);

        public FormSinBorde()
        {
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Tema.Fondo;
            ForeColor = Tema.Texto;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Padding = new Padding(BORDE);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // WS_THICKFRAME: Windows la trata como redimensionable (bordes, acomodo a los lados, Win + flechas).
                // WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU: minimizar/maximizar desde la barra de tareas.
                // El marco que traería WS_THICKFRAME se quita en WM_NCCALCSIZE (ver WndProc).
                cp.Style |= 0x40000 | 0x20000 | 0x10000 | 0x80000;
                cp.ClassStyle |= 0x20000;        // CS_DROPSHADOW: sombra alrededor
                return cp;
            }
        }

        void AjustarMaximo()
        {
            if (IsHandleCreated) MaximizedBounds = Screen.FromHandle(Handle).WorkingArea; // no tapar la barra de tareas
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); AjustarMaximo(); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (WindowState == FormWindowState.Normal) AjustarMaximo(); }

        protected override void OnResize(EventArgs e)
        {
            Padding = WindowState == FormWindowState.Maximized ? Padding.Empty : new Padding(BORDE);
            base.OnResize(e);
        }

        protected override void WndProc(ref Message m)
        {
            // Sin marco visible: toda la ventana es área propia (el marco de WS_THICKFRAME no se dibuja).
            const int WM_NCCALCSIZE = 0x83, WM_NCPAINT = 0x85, WM_NCACTIVATE = 0x86, WM_SETTEXT = 0x0C, WM_SETICON = 0x80;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            // Windows intenta dibujar su barra de título clásica al activarse la ventana: se lo impide.
            if (m.Msg == WM_NCPAINT) { m.Result = IntPtr.Zero; return; }
            if (m.Msg == WM_NCACTIVATE) { m.Result = (IntPtr)1; return; }
            // También la dibuja al cambiar el título o el ícono: se oculta el estilo "visible" solo durante ese instante.
            if ((m.Msg == WM_SETTEXT || m.Msg == WM_SETICON) && IsHandleCreated)
            {
                int estilo = GetWindowLong(Handle, -16);
                SetWindowLong(Handle, -16, estilo & ~0x10000000);
                base.WndProc(ref m);
                SetWindowLong(Handle, -16, estilo);
                return;
            }
            base.WndProc(ref m);
            const int WM_NCHITTEST = 0x84, HTCLIENT = 1;
            if (m.Msg != WM_NCHITTEST || (int)m.Result != HTCLIENT || WindowState != FormWindowState.Normal) return;
            long lp = m.LParam.ToInt64();
            var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            bool izq = p.X < BORDE, der = p.X >= ClientSize.Width - BORDE, arr = p.Y < BORDE, aba = p.Y >= ClientSize.Height - BORDE;
            int r = arr && izq ? 13 : arr && der ? 14 : aba && izq ? 16 : aba && der ? 17 : izq ? 10 : der ? 11 : arr ? 12 : aba ? 15 : 0;
            if (r != 0) m.Result = (IntPtr)r;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (WindowState == FormWindowState.Normal)
                using (var p = new Pen(Tema.Borde))
                    e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    class ColoresMenu : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Tema.Superficie; } }
        public override Color ImageMarginGradientBegin { get { return Tema.Superficie; } }
        public override Color ImageMarginGradientMiddle { get { return Tema.Superficie; } }
        public override Color ImageMarginGradientEnd { get { return Tema.Superficie; } }
        public override Color MenuBorder { get { return Tema.Borde; } }
        public override Color MenuItemBorder { get { return Tema.Hover; } }
        public override Color MenuItemSelected { get { return Tema.Hover; } }
        public override Color SeparatorDark { get { return Tema.Borde; } }
        public override Color SeparatorLight { get { return Tema.Superficie; } }
        public override Color CheckBackground { get { return Tema.Superficie; } }
        public override Color CheckSelectedBackground { get { return Tema.Hover; } }
        public override Color CheckPressedBackground { get { return Tema.Hover; } }
    }

    class MenuConTema : ToolStripProfessionalRenderer
    {
        public const string PELIGRO = "peligro";   // Tag de una opción que cierra o borra algo: se pinta en rojo

        public MenuConTema() : base(new ColoresMenu()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = PELIGRO.Equals(e.Item.Tag) ? Tema.RojoTexto : Tema.Texto;
            base.OnRenderItemText(e);
        }

        // La marca de Windows es negra y no se ve en el tema oscuro: se dibuja un ✓ verde.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, "\uE73E", Tema.IconosChicos, Rectangle.Inflate(e.ImageRectangle, 4, 4), Tema.Verde,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
