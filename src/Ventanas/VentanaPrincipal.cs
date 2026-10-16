// Ventana principal: armado de la pantalla, menú del ícono junto al reloj, inicio con Windows y cierre.
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
    partial class VentanaPrincipal : FormSinBorde
    {
        const int UMBRAL = 3;        // pings seguidos sin respuesta para considerar "corte"
        const int TIMEOUT = 1000;    // ms de espera por cada ping
        const int LENTO = 150;       // ms a partir de los cuales se pinta amarillo
        const int HIST = 600;        // muestras visibles en la línea de tiempo (10 min)
        const int ALTO = 58;         // alto de cada fila del panel
        const int DIAS_GUARDAR = 7;  // carpetas de días que se conservan en registros\
        const int POR_PAGINA = 6;    // terminales por pestaña
        const int MINUTOS_APAGADO = 5; // sin responder (con los de después bien) para considerarlo apagado
        const int MINUTOS_AUSENCIA = 10;   // "puede estar apagado": si vuelve antes de esto, no se apagó, falló (queda anotado)
        const int PERDIDAS_INESTABLE = 5;  // pings sueltos perdidos en 1 minuto (sin ser corte) para anotar "INESTABLE"
        const int SEGUNDOS_ESTABLE = 60;   // sin llegar a eso durante este tiempo, la red volvió a estar estable
        const int PERDIDAS_LOCAL_SOSPECHOSAS = 3;   // pings sueltos perdidos por un equipo del local en el último minuto:
                                                    // si hay un corte "del proveedor" en ese rato, lo más probable es que sea del local
        public const int MAX_EQUIPOS = 30; // cada equipo abre su propio ping.exe: 30 es mucho y sigue siendo liviano
        const int EQUIPOS_VISIBLES = 6;     // filas que se ven de entrada; el resto, con la barra de la tarjeta
        const int ALTO_CABECERA = 28;       // fila de títulos de las columnas

        static readonly Color VERDE = Tema.Verde;
        static readonly Color AMARILLO = Tema.Amarillo;
        static readonly Color ROJO = Tema.Rojo;
        const string CABECERA = "Tipo;Inicio;Fin;Duracion_seg;Equipos;Diagnostico";

        readonly string carpeta, rutaIncidentes;
        readonly List<Equipo> equipos;
        readonly bool hayDestinosInternet;   // si se marcó algún "Destino en internet" en Equipos
        readonly StringBuilder pendientes = new StringBuilder();
        Incidente actual;
        Inestabilidad inestable;
        DateTime ultimoFin = DateTime.MinValue;
        StreamWriter logPings;
        string logFecha;
        bool cerrando;

        readonly Timer timer = new Timer();
        readonly PanelDibujo panel = new PanelDibujo();          // filas de equipos
        readonly Panel listaEquipos = new Panel();               // contiene a panel y lo desplaza
        readonly BarraDesplazamiento barraEquipos = new BarraDesplazamiento();
        readonly DivisorArrastrable divisor = new DivisorArrastrable();  // entre equipos e historial
        Tarjeta tarjetaEquipos;
        readonly Banner banner = new Banner();
        readonly DataGridView historial = new DataGridView();
        readonly BotonPlano botonMostrar = new BotonPlano();   // qué tipos de fila se ven en el historial
        readonly HashSet<string> ocultos = Tipos.Leer(Ajustes.Leer("historial_ocultos"), new string[0]);
        readonly PanelLateral lateral = new PanelLateral();
        readonly ItemLateral chkSonido, chkHora, chkInicio, itemTelegram;
        readonly Panel vistaPrincipal = new Panel();
        readonly Panel vistaTerminales = new Panel();
        List<Control> paginas;               // solo si hay más de POR_PAGINA equipos
        List<BotonPlano> botonesPagina;
        bool arrancarOculto;                 // /minimizado: arrancó con Windows
        bool salirDeVerdad;                  // false = la X solo esconde la ventana
        public bool Reiniciar;               // lo lee Main para volver a abrir el programa
        readonly List<Terminal> terminales = new List<Terminal>();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly Icon iconoOk = Logo.Icono(VERDE, 32), iconoMal = Logo.Icono(ROJO, 32);
        readonly Font fNombre = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        readonly Font fNormal = Tema.Normal;
        readonly Font fCabecera = Tema.Pequena;

        readonly EventWaitHandle senalMostrar;

        public VentanaPrincipal(bool oculto, EventWaitHandle mostrar)
        {
            arrancarOculto = oculto;
            CargarAjusteHora();   // antes de anotar nada: si la PC tiene la hora mal, que todo salga con la buena
            senalMostrar = mostrar;
            carpeta = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "registros");
            Directory.CreateDirectory(carpeta);
            rutaIncidentes = Path.Combine(carpeta, "incidentes.csv");
            LimpiarDiasViejos();
            RecortarHistorial();
            EscribirLeeme();
            equipos = CargarEquipos();
            hayDestinosInternet = equipos.Any(x => x.EsInternet);

            Text = Programa.NOMBRE;
            Icon = iconoOk;
            Font = Tema.Normal;
            Size = new Size(1380, 860);
            MinimumSize = new Size(1000, 620);
            StartPosition = FormStartPosition.CenterScreen;

            var barra = new BarraTitulo(this);
            barra.Subtitulo = Programa.VERSION;

            // Franja de estado, con el botón grande de terminales a la derecha.
            banner.Dock = DockStyle.Fill;
            banner.Boton.Text = "Ver terminales";
            banner.Boton.Glifo = "\uE756";
            banner.Boton.Click += delegate { CambiarVista(); };
            var zonaBanner = new Panel();
            zonaBanner.Dock = DockStyle.Top;
            zonaBanner.Height = 82;
            zonaBanner.Padding = new Padding(12, 2, 12, 10);
            zonaBanner.Controls.Add(banner);

            // Panel lateral: Evidencia arriba, Configuración debajo, Salir al fondo.
            // Los íconos son caracteres de la fuente "Segoe MDL2 Assets" que trae Windows 10.
            lateral.Dock = DockStyle.Left;
            var plegar = lateral.Agregar(new ItemLateral("\uE700", "Ocultar panel"), false);
            plegar.Click += delegate
            {
                lateral.Plegado = !lateral.Plegado;
                plegar.Text = lateral.Plegado ? "Mostrar panel" : "Ocultar panel";
                Ajustes.Poner("panel_plegado", lateral.Plegado ? "1" : "0");
            };
            lateral.Agregar(new SeccionLateral("EVIDENCIA"), false);
            Opcion("\uE722", "Capturar pantalla", CapturarPantalla);
            Opcion("\uE70F", "Anotar caída", AnotarCaida);
            // Sin Activar(): su \u00EDcono queda encendido todo el rato que dura el mantenimiento.
            itemMantenimiento = lateral.Agregar(new ItemLateral("\uE90F", "Mantenimiento"), false);
            itemMantenimiento.Click += delegate { Mantenimiento(); };
            Opcion("\uE8A5", "Generar reporte", GenerarReporte);
            Opcion("\uE838", "Registros", delegate { Process.Start(carpeta); });
            lateral.Agregar(new SeccionLateral("CONFIGURACIÓN"), false);
            Opcion("\uE968", "Equipos", EditarEquipos);
            itemTelegram = Opcion("\uE724", TextoTelegram(), delegate
            {
                using (var v = new VentanaTelegram()) v.ShowDialog(this);
                itemTelegram.Text = TextoTelegram();
            });
            chkSonido = lateral.Agregar(Interruptor("\uE767", "Sonido de alerta", Ajustes.Leer("sonido") != "0"), false);
            chkSonido.Click += delegate { Ajustes.Poner("sonido", chkSonido.Checked ? "1" : "0"); };
            chkHora = lateral.Agregar(Interruptor("\uE823", "Hora en terminales", Ajustes.Leer("hora") != "0"), false);
            chkHora.Click += delegate { Ajustes.Poner("hora", chkHora.Checked ? "1" : "0"); };
            var chkTema = lateral.Agregar(Interruptor("\uE708", "Tema oscuro", Tema.Oscuro), false);
            chkTema.Click += delegate { CambiarTema(chkTema); };
            chkInicio = lateral.Agregar(Interruptor("\uE770", "Iniciar con Windows", InicioActivado()), false);
            chkInicio.Click += delegate { ConfigurarInicio(chkInicio.Checked); };
            if (chkInicio.Checked) ConfigurarInicio(true); // por si el .exe se movió de carpeta
            var acerca = lateral.Agregar(new ItemLateral("\uE946", "Acerca de"), true);
            acerca.Click += delegate { Activar(acerca, delegate { Dialogos.AcercaDe(this); }); };
            var salir = lateral.Agregar(new ItemLateral("\uE7E8", "Salir"), true);
            salir.Peligro = true;
            salir.Click += delegate { Salir(); };
            if (Ajustes.Leer("panel_plegado") == "1")
            {
                lateral.Plegado = true;
                plegar.Text = "Mostrar panel";
            }

            // Vista principal: tarjeta de equipos arriba, historial abajo y un divisor entre los dos que se arrastra.
            // La tarjeta muestra hasta EQUIPOS_VISIBLES; si hay más, su lista se desplaza con una barra.
            var tarjetaEquipos = new Tarjeta(Tema.Superficie);
            tarjetaEquipos.Dock = DockStyle.Fill;
            var cabecera = new PanelDibujo();
            cabecera.Dock = DockStyle.Top;
            cabecera.Height = ALTO_CABECERA;
            cabecera.BackColor = Tema.Superficie;
            cabecera.Paint += PintarCabecera;
            listaEquipos.Dock = DockStyle.Fill;
            listaEquipos.BackColor = Tema.Superficie;
            panel.BackColor = Tema.Superficie;
            panel.Height = ALTO * equipos.Count;
            panel.Paint += PintarPanel;
            barraEquipos.Dock = DockStyle.Right;
            barraEquipos.Cambio += delegate { panel.Top = -barraEquipos.Valor; };
            panel.MouseWheel += (s, a) => { barraEquipos.Valor -= a.Delta / 120 * ALTO; };   // una fila por cada paso de la rueda
            listaEquipos.Controls.Add(panel);
            listaEquipos.Controls.Add(barraEquipos);
            barraEquipos.BringToFront();
            listaEquipos.Resize += delegate
            {
                barraEquipos.Configurar(panel.Height, listaEquipos.ClientSize.Height);
                bool hayBarra = panel.Height > listaEquipos.ClientSize.Height;   // no todos los equipos entran
                panel.Width = listaEquipos.ClientSize.Width - (hayBarra ? barraEquipos.Width : 0);
                panel.Top = -barraEquipos.Valor;
                panel.Invalidate();
            };
            tarjetaEquipos.Controls.Add(listaEquipos);
            tarjetaEquipos.Controls.Add(cabecera);

            // Título del historial, con el botón para elegir qué tipos de fila se muestran.
            var titulo = new Panel();
            titulo.Dock = DockStyle.Top;
            titulo.Height = 40;
            titulo.Padding = new Padding(0, 6, 0, 2);
            var textoTitulo = new Label();
            textoTitulo.Dock = DockStyle.Fill;
            textoTitulo.Padding = new Padding(6, 10, 0, 0);
            textoTitulo.Font = Tema.Pequena;
            textoTitulo.ForeColor = Tema.TextoTenue;
            textoTitulo.Text = "HISTORIAL DE CORTES   ·   un corte = " + UMBRAL + " o más pings seguidos sin respuesta";
            botonMostrar.Dock = DockStyle.Right;
            botonMostrar.Glifo = "";
            botonMostrar.Click += delegate { MenuMostrar().Show(botonMostrar, new Point(0, botonMostrar.Height + 2)); };
            ActualizarBotonMostrar();
            titulo.Controls.Add(textoTitulo);
            titulo.Controls.Add(botonMostrar);

            var tarjetaHistorial = new Tarjeta(Tema.Superficie);
            tarjetaHistorial.Dock = DockStyle.Fill;
            ArmarHistorial();
            tarjetaHistorial.Controls.Add(historial);

            tarjetaEquipos.Dock = DockStyle.Top;
            this.tarjetaEquipos = tarjetaEquipos;
            divisor.Dock = DockStyle.Top;
            divisor.Arriba = tarjetaEquipos;
            divisor.Limitar = LimitarAltoEquipos;
            divisor.AlSoltar = SoltarDivisor;

            // Con Dock, el último control agregado se acomoda primero: equipos, divisor, título e historial.
            vistaPrincipal.Dock = DockStyle.Fill;
            vistaPrincipal.Controls.Add(tarjetaHistorial);
            vistaPrincipal.Controls.Add(titulo);
            vistaPrincipal.Controls.Add(divisor);
            vistaPrincipal.Controls.Add(tarjetaEquipos);
            vistaPrincipal.Resize += delegate { AjustarAltoEquipos(); };   // si la ventana se achica, que el historial no desaparezca
            CrearTerminales();

            var zona = new Panel();
            zona.Dock = DockStyle.Fill;
            zona.Padding = new Padding(12, 0, 12, 12);
            zona.Controls.Add(vistaTerminales);
            zona.Controls.Add(vistaPrincipal);

            // Con Dock, el último control agregado se acomoda primero.
            Controls.Add(zona);
            Controls.Add(lateral);
            Controls.Add(zonaBanner);
            Controls.Add(barra);
            PerformLayout();
            AjustarAltoEquipos();

            var menu = new ContextMenuStrip();
            menu.Renderer = new MenuConTema();
            menu.BackColor = Tema.Superficie;
            var abrir = menu.Items.Add("Abrir " + Programa.NOMBRE, null, delegate { Mostrar(); });
            abrir.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add("Capturar pantalla", null, delegate { CapturarPantalla(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir (dejar de vigilar)", null, delegate { Salir(); }).Tag = MenuConTema.PELIGRO;

            tray.Icon = iconoOk;
            tray.Text = Programa.NOMBRE;
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += (s, a) => { if (a.Button == MouseButtons.Left) Mostrar(); };
            tray.BalloonTipClicked += delegate { Mostrar(); };

            // Si alguien abre el .exe otra vez, la otra copia avisa por aquí y esta ventana se muestra.
            ThreadPool.RegisterWaitForSingleObject(senalMostrar, (s, vencido) =>
            {
                try { BeginInvoke((Action)Mostrar); } catch { }
            }, null, -1, false);

            CargarMantenimiento();
            RevisarHuecoAlAbrir();
            CargarHistorial();

            timer.Interval = 1000;
            timer.Tick += delegate { Tick(); };
            timer.Start();
        }

        // Opción del panel que hace algo al tocarla (abrir una ventana, generar un archivo...).
        ItemLateral Opcion(string glifo, string texto, Action accion)
        {
            var item = lateral.Agregar(new ItemLateral(glifo, texto), false);
            item.Click += delegate { Activar(item, accion); };
            return item;
        }

        // El ícono se pone verde mientras la acción trabaja. Si es instantánea, queda verde medio segundo
        // para que se note que respondió.
        static void Activar(ItemLateral item, Action accion)
        {
            item.Activo = true;
            item.Refresh();
            DateTime inicio = Reloj.Ahora;
            try { accion(); }
            finally
            {
                int falta = 500 - (int)(Reloj.Ahora - inicio).TotalMilliseconds;
                if (falta <= 0) item.Activo = false;
                else
                {
                    var espera = new Timer();
                    espera.Interval = falta;
                    espera.Tick += delegate { espera.Stop(); espera.Dispose(); item.Activo = false; };
                    espera.Start();
                }
            }
        }

        void CambiarTema(ItemLateral interruptor)
        {
            Ajustes.Poner("tema", interruptor.Checked ? "oscuro" : "claro");
            if (!Preguntar(this, "Cambiar tema",
                           "Para cambiar al tema " + (interruptor.Checked ? "oscuro" : "claro") + ", " + Programa.NOMBRE +
                           " se reinicia solo (tarda un par de segundos y no se pierde nada).\n\n" +
                           "Si eliges \"Más tarde\", el tema cambia la próxima vez que lo abras.",
                           "Reiniciar ahora", "Más tarde")) return;
            Reiniciar = true;
            salirDeVerdad = true;
            Close();
        }

        // ---------- Divisor entre equipos e historial ----------

        // Alto de la tarjeta de equipos para mostrar cierta cantidad de filas (con su margen y la cabecera).
        static int AltoTarjeta(int filas) { return 20 + ALTO_CABECERA + ALTO * filas; }

        const int HISTORIAL_MINIMO = 160;   // alto mínimo que se le deja al historial

        // Mientras se arrastra: entre 1 fila y todas, sin tapar el historial.
        int LimitarAltoEquipos(int alto)
        {
            int libre = vistaPrincipal.ClientSize.Height - divisor.Height - 40 - HISTORIAL_MINIMO;   // 40 = título del historial
            return Math.Max(AltoTarjeta(1), Math.Min(alto, Math.Min(AltoTarjeta(equipos.Count), libre)));
        }

        // Cuántas filas enteras entran con la ventana del tamaño actual.
        int FilasQueEntran()
        {
            int filas = (LimitarAltoEquipos(int.MaxValue) - AltoTarjeta(0)) / ALTO;
            return Math.Max(1, Math.Min(filas, equipos.Count));
        }

        // Al soltar: se ajusta a la fila entera más cercana, para que no quede un equipo cortado.
        int SoltarDivisor(int alto)
        {
            int filas = (int)Math.Round((alto - AltoTarjeta(0)) / (double)ALTO);
            filas = Math.Max(1, Math.Min(filas, FilasQueEntran()));
            Ajustes.Poner("filas_equipos", filas.ToString(CultureInfo.InvariantCulture));
            return AltoTarjeta(filas);
        }

        // Filas que eligió el usuario (o EQUIPOS_VISIBLES la primera vez), hasta donde entren.
        void AjustarAltoEquipos()
        {
            if (tarjetaEquipos == null || vistaPrincipal.ClientSize.Height <= 0) return;
            int elegidas;
            if (!int.TryParse(Ajustes.Leer("filas_equipos"), out elegidas)) elegidas = EQUIPOS_VISIBLES;
            tarjetaEquipos.Height = AltoTarjeta(Math.Max(1, Math.Min(elegidas, FilasQueEntran())));
        }

        static ItemLateral Interruptor(string glifo, string texto, bool encendido)
        {
            var i = new ItemLateral(glifo, texto);
            i.EsInterruptor = true;
            i.Checked = encendido;
            return i;
        }

        void ArmarHistorial()
        {
            historial.Dock = DockStyle.Fill;
            historial.ReadOnly = true;
            historial.AllowUserToAddRows = false;
            historial.AllowUserToDeleteRows = false;
            historial.AllowUserToResizeRows = false;
            historial.RowHeadersVisible = false;
            historial.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            historial.MultiSelect = false;
            historial.RowTemplate.Height = 30;
            historial.ColumnHeadersHeight = 32;
            historial.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            historial.Columns.Add("tipo", "TIPO");
            historial.Columns.Add("inicio", "INICIO");
            historial.Columns.Add("fin", "FIN");
            historial.Columns.Add("dur", "DURACIÓN");
            historial.Columns.Add("que", "QUÉ SE CAYÓ / NOTA");
            historial.Columns.Add("diag", "DIAGNÓSTICO");
            int[] anchos = { 130, 150, 80, 100, 270 };
            for (int i = 0; i < anchos.Length; i++) historial.Columns[i].Width = anchos[i];
            historial.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            foreach (DataGridViewColumn c in historial.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            Tema.Grilla(historial, Tema.Superficie, false);
            // Clic en una fila: se abre su detalle completo (el diagnóstico suele no entrar en la columna).
            historial.ShowCellToolTips = false;
            historial.Cursor = Cursors.Hand;
            // Clic derecho: menú para marcar la fila como mantenimiento (o quitarle la marca).
            historial.CellMouseClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var datos = historial.Rows[e.RowIndex].Tag as string[];
                historial.ClearSelection();
                if (datos == null) return;
                if (e.Button == MouseButtons.Right) MenuFila(datos, Cursor.Position);
                else if (e.Button == MouseButtons.Left) Dialogos.Detalle(this, datos);
            };
        }

        static string TextoTelegram() { return Telegram.Activo ? "Telegram  ✔" : "Telegram"; }

        void Mostrar()
        {
            Show();
            AjustarAltoEquipos();   // arrancó escondido con Windows: recién ahora tiene tamaño
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        // ---------- Qué se muestra en el historial ----------
        // Se puede ocultar un tipo de fila (por ejemplo, la red inestable) solo de la pantalla:
        // igual se guarda en incidentes.csv y puede salir en el reporte.

        ContextMenuStrip MenuMostrar()
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new MenuConTema();
            menu.BackColor = Tema.Superficie;
            foreach (var tipo in Tipos.Todos)
            {
                var item = new ToolStripMenuItem(Tipos.Plural(tipo));
                item.Checked = !ocultos.Contains(tipo);
                string t = tipo;
                item.Click += delegate
                {
                    if (!ocultos.Remove(t)) ocultos.Add(t);
                    item.Checked = !ocultos.Contains(t);
                    Ajustes.Poner("historial_ocultos", Tipos.Escribir(ocultos));
                    ActualizarBotonMostrar();
                    CargarHistorial();
                };
                menu.Items.Add(item);
            }
            // El menú queda abierto al marcar, para poder cambiar varias cosas seguidas.
            menu.Closing += (s, e) => { if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
            menu.Closed += delegate { BeginInvoke((Action)menu.Dispose); };
            return menu;
        }

        void ActualizarBotonMostrar()
        {
            botonMostrar.Text = ocultos.Count == 0 ? "Mostrar: todo" : "Mostrar: " + ocultos.Count + (ocultos.Count == 1 ? " oculto" : " ocultos");
            botonMostrar.Invalidate();
        }

        void Salir()
        {
            if (!Dialogos.Preguntar(this, "Cerrar " + Programa.NOMBRE,
                                    "Si cierras " + Programa.NOMBRE + " deja de vigilar la red: no se detectarán cortes " +
                                    "hasta que lo vuelvas a abrir.\n\n¿Cerrarlo de todas formas?",
                                    "Cerrar " + Programa.NOMBRE, "Cancelar", true)) return;
            salirDeVerdad = true;
            Close();
        }

        public static bool Preguntar(Form dueno, string titulo, string texto, string si, string no)
        {
            return Dialogos.Preguntar(dueno, titulo, texto, si, no);
        }

        // ---------- Iniciar con Windows ----------

        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RUN_NOMBRE = Programa.NOMBRE;

        static bool InicioActivado()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                    return k != null && k.GetValue(RUN_NOMBRE) != null;
            }
            catch { return false; }
        }

        static void ConfigurarInicio(bool activar)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RUN_KEY))
                {
                    if (activar) k.SetValue(RUN_NOMBRE, "\"" + Application.ExecutablePath + "\" /minimizado");
                    else k.DeleteValue(RUN_NOMBRE, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo cambiar el inicio con Windows: " + ex.Message, Programa.NOMBRE);
            }
        }

        // ---------- Configuración ----------

        List<Equipo> CargarEquipos()
        {
            var lista = Config.Leer();
            if (lista.Count == 0)
            {
                using (var ed = new EditorEquipos()) ed.ShowDialog();
                lista = Config.Leer();
                if (lista.Count == 0) Environment.Exit(1);
            }
            return lista;
        }

        void EditarEquipos()
        {
            using (var ed = new EditorEquipos())
            {
                if (ed.ShowDialog(this) != DialogResult.OK) return;
            }
            if (!Preguntar(this, "Equipos guardados",
                           "Para usar la nueva lista, " + Programa.NOMBRE + " se reinicia solo (tarda un par de segundos y no se pierde nada).\n\n" +
                           "Si eliges \"Más tarde\", los cambios se aplican la próxima vez que lo abras.",
                           "Reiniciar ahora", "Más tarde")) return;
            Reiniciar = true;
            salirDeVerdad = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // La X no cierra: esconde la ventana y el programa sigue vigilando junto al reloj.
            if (e.CloseReason == CloseReason.UserClosing && !salirDeVerdad)
            {
                e.Cancel = true;
                Hide();
                // Se explica una sola vez en esta PC (queda anotado en ajustes.txt).
                if (Ajustes.Leer("aviso_bandeja") != "1")
                {
                    Ajustes.Poner("aviso_bandeja", "1");
                    tray.ShowBalloonTip(8000, Programa.NOMBRE + " sigue vigilando",
                        "Está aquí, junto al reloj. Clic para abrirlo. Para cerrarlo del todo: clic derecho → Salir.", ToolTipIcon.Info);
                }
                return;
            }
            cerrando = true;
            timer.Stop();
            // Al abrir de nuevo se sabrá por qué no se vigiló este rato.
            EscribirLatido("cerrado;" + (e.CloseReason == CloseReason.WindowsShutDown ? "windows" : "usuario"));
            foreach (var t in terminales)
            {
                if (t.Proc != null) try { if (!t.Proc.HasExited) t.Proc.Kill(); } catch { }
                if (t.Archivo != null)
                    try
                    {
                        t.Archivo.WriteLine("========== " + Programa.NOMBRE + " cerrado: " + Reloj.Ahora.ToString("dd/MM/yyyy HH:mm:ss") + " ==========");
                        t.Archivo.Dispose();
                    }
                    catch { }
            }
            if (actual != null) CerrarIncidente(Reloj.Ahora, " (" + Programa.NOMBRE + " se cerró durante el corte.)");
            if (inestable != null) CerrarInestabilidad();
            if (lento != null) CerrarLentitud();
            if (dnsCaidoDesde != DateTime.MinValue) CerrarFallaDns(Reloj.Ahora);
            IntentarGuardar();
            if (logPings != null) try { logPings.Dispose(); } catch { }
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }
    }
}
