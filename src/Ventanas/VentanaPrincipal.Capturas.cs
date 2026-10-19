// Capturas automáticas: cuando pasa algo (un corte, la red inestable...), Yedsa guarda solo una imagen del panel y
// otra de las terminales. Las arma con su propia ventana, así que funciona aunque esté escondido junto al reloj y
// sin cruzarse con lo que esté haciendo la persona en la PC.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Yedsa
{
    partial class VentanaPrincipal
    {
        // Cuándo se toman (se eligen en "Capturas automáticas"; se guarda en ajustes.txt como "capturas_auto").
        static readonly string[] MOMENTOS_CAPTURA = { "corte_inicio", "corte_fin", "inestable", "lento", "dns", "nofue" };
        static readonly string[] NOMBRES_MOMENTO =
        {
            "Corte: " + SEGUNDOS_CAPTURA_CORTE + " segundos después de empezar",
            "Corte: al terminar",
            "Red inestable: al terminar",
            "Internet lento: al terminar",
            "Falla de DNS: al terminar",
            "También los avisos que no fueron corte"
        };
        static readonly string[] MOMENTOS_POR_DEFECTO = { "corte_inicio", "corte_fin", "inestable", "lento", "dns" };

        const int SEGUNDOS_CAPTURA_CORTE = 10;   // a los 10 s las terminales ya muestran varios "Tiempo de espera agotado"
        const int ANCHO_CAPTURA = 1340;

        HashSet<string> momentosCaptura;
        double capturaCortePendiente;   // en Reloj.Segundos; 0 = no hay ninguna esperando

        string CarpetaCapturasAuto { get { return Path.Combine(carpeta, "capturas", "automaticas"); } }

        void CargarMomentosCaptura()
        {
            string guardado = Ajustes.Leer("capturas_auto");
            momentosCaptura = guardado == "" ? new HashSet<string>(MOMENTOS_POR_DEFECTO)
                            : guardado == "ninguna" ? new HashSet<string>()
                            : new HashSet<string>(guardado.Split(',').Where(MOMENTOS_CAPTURA.Contains));
        }

        // Opción del panel "Capturas automáticas".
        void ElegirCapturasAutomaticas()
        {
            var elegidos = new HashSet<string>(momentosCaptura);
            if (!Dialogos.ElegirVarios(this, "Capturas automáticas",
                    "Cuando pasa algo, " + Programa.NOMBRE + " guarda solo una imagen del panel y otra de las terminales, aunque esté " +
                    "escondido junto al reloj. Quedan en registros\\capturas\\automaticas (se guardan " + DIAS_GUARDAR + " días) y se " +
                    "abren desde el detalle de cada fila del historial.\n\n¿Cuándo?",
                    MOMENTOS_CAPTURA, NOMBRES_MOMENTO, elegidos))
                return;
            momentosCaptura = elegidos;
            Ajustes.Poner("capturas_auto", elegidos.Count == 0 ? "ninguna" : string.Join(",", MOMENTOS_CAPTURA.Where(elegidos.Contains)));
        }

        // Un corte acaba de empezar: la captura del inicio se toma unos segundos después.
        void ProgramarCapturaCorte()
        {
            capturaCortePendiente = Reloj.Segundos + SEGUNDOS_CAPTURA_CORTE;
        }

        // Cada segundo: ¿llegó la hora de la captura del inicio del corte?
        void RevisarCapturas()
        {
            if (capturaCortePendiente == 0 || Reloj.Segundos < capturaCortePendiente) return;
            capturaCortePendiente = 0;
            if (actual == null || actual.EnMantenimiento || !momentosCaptura.Contains("corte_inicio")) return;
            actual.Capturas.AddRange(Capturar(actual.Inicio, "corte", "inicio", "Corte en curso (" + SEGUNDOS_CAPTURA_CORTE + " s después de empezar)"));
        }

        // Al cerrar un corte ya se sabe qué fue: las capturas del inicio se renombran con eso (o se borran, si eso
        // no se quería capturar) y se toma la del final.
        void CapturasAlCerrarCorte(Incidente inc, string tipo, DateTime inicio)
        {
            capturaCortePendiente = 0;
            bool nofue = tipo == Tipos.NoFueCorte;
            bool seQuiere = !inc.EnMantenimiento && (!nofue || momentosCaptura.Contains("nofue"));
            foreach (var c in inc.Capturas)
                try
                {
                    if (!seQuiere) { File.Delete(c.Ruta); continue; }
                    string nueva = RutaCaptura(inicio, tipo, c.Momento, c.Parte);
                    if (nueva != c.Ruta && !File.Exists(nueva)) File.Move(c.Ruta, nueva);
                }
                catch (Exception ex) { Programa.AnotarError("No se pudo renombrar la captura " + Path.GetFileName(c.Ruta), ex); }
            if (seQuiere && !cerrando && momentosCaptura.Contains(nofue ? "nofue" : "corte_fin"))
                Capturar(inicio, tipo, "fin", Tipos.Titulo(tipo) + " (al terminar)");
        }

        // Al terminar un rato de red inestable, internet lento o falla de DNS.
        void CapturaAlTerminar(string momento, string tipo, DateTime inicio, bool enMantenimiento)
        {
            if (enMantenimiento || cerrando || !momentosCaptura.Contains(momento)) return;
            Capturar(inicio, tipo, "fin", Tipos.Titulo(tipo) + " (al terminar)");
        }

        // Nombre de archivo: empieza con la hora de inicio y el tipo de la fila del historial, para encontrarlas desde el detalle.
        // Ejemplo: 2026-10-18_12-49-54_corte-local_inicio_terminales.png
        string RutaCaptura(DateTime inicio, string tipo, string momento, string parte)
        {
            return Path.Combine(CarpetaCapturasAuto, PrefijoCaptura(inicio, tipo) + "_" + momento + "_" + parte + ".png");
        }

        static string PrefijoCaptura(DateTime inicio, string tipo)
        {
            return inicio.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_" + tipo.ToLowerInvariant().Replace(' ', '-');
        }

        // Las capturas automáticas de una fila del historial (para el botón "Ver capturas" del detalle).
        List<string> CapturasDe(string[] campos)
        {
            DateTime inicio;
            if (!DateTime.TryParseExact(campos[1], FORMATO_FECHA, CultureInfo.InvariantCulture, DateTimeStyles.None, out inicio) ||
                !Directory.Exists(CarpetaCapturasAuto))
                return new List<string>();
            return Directory.GetFiles(CarpetaCapturasAuto, PrefijoCaptura(inicio, campos[0]) + "_*.png").OrderBy(f => f).ToList();
        }

        // Guarda las dos imágenes (panel y terminales) y devuelve lo guardado.
        List<CapturaHecha> Capturar(DateTime inicio, string tipo, string momento, string queEs)
        {
            var hechas = new List<CapturaHecha>();
            ActualizarEstado();   // al terminar algo, la franja todavía no se enteró (se actualiza en el próximo segundo)
            try
            {
                Directory.CreateDirectory(CarpetaCapturasAuto);
                string leyenda = Programa.NOMBRE + "   ·   captura automática   ·   " + queEs + "   ·   " +
                                 Reloj.Ahora.ToString("dd/MM/yyyy HH:mm:ss") + "   ·   " + Environment.MachineName;
                foreach (var parte in new[] { "panel", "terminales" })
                {
                    string ruta = RutaCaptura(inicio, tipo, momento, parte);
                    using (var img = parte == "panel" ? ImagenPanel(leyenda) : ImagenTerminales(leyenda))
                        img.Save(ruta, ImageFormat.Png);
                    hechas.Add(new CapturaHecha { Ruta = ruta, Momento = momento, Parte = parte });
                }
            }
            catch (Exception ex) { Programa.AnotarError("No se pudo guardar una captura automática", ex); }
            return hechas;
        }

        // ---------- Las imágenes ----------
        // Se dibujan con las mismas funciones que la pantalla, en una imagen aparte: no hace falta que la ventana se vea.

        const int MARGEN_CAPTURA = 14, ALTO_LEYENDA = 34, ALTO_FRANJA = 70;

        Bitmap NuevaImagen(int alto, string leyenda, out Graphics g)
        {
            var bmp = new Bitmap(ANCHO_CAPTURA, alto);
            g = Graphics.FromImage(bmp);
            g.Clear(Tema.Fondo);
            TextRenderer.DrawText(g, leyenda, Tema.Normal, new Rectangle(MARGEN_CAPTURA, 0, ANCHO_CAPTURA - 2 * MARGEN_CAPTURA, ALTO_LEYENDA),
                                  Tema.TextoSec, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            banner.Dibujar(g, new Rectangle(MARGEN_CAPTURA, ALTO_LEYENDA, ANCHO_CAPTURA - 2 * MARGEN_CAPTURA, ALTO_FRANJA),
                           ANCHO_CAPTURA - 2 * MARGEN_CAPTURA - 70);
            return bmp;
        }

        // La franja de estado y TODOS los equipos con su línea de los últimos 10 minutos.
        Bitmap ImagenPanel(string leyenda)
        {
            int ancho = ANCHO_CAPTURA - 2 * MARGEN_CAPTURA - 20;
            int altoEquipos = ALTO * equipos.Count;
            int arriba = ALTO_LEYENDA + ALTO_FRANJA + MARGEN_CAPTURA;
            Graphics g;
            var bmp = NuevaImagen(arriba + 10 + ALTO_CABECERA + altoEquipos + 10 + MARGEN_CAPTURA, leyenda, out g);
            using (g)
            {
                Tema.Rellenar(g, Tema.Superficie, new RectangleF(MARGEN_CAPTURA, arriba, ANCHO_CAPTURA - 2 * MARGEN_CAPTURA,
                                                                 ALTO_CABECERA + altoEquipos + 20), 12);
                using (var cab = new Bitmap(ancho, ALTO_CABECERA))
                using (var filas = new Bitmap(ancho, Math.Max(1, altoEquipos)))
                {
                    using (var gc = Graphics.FromImage(cab)) DibujarCabecera(gc);
                    using (var gf = Graphics.FromImage(filas)) DibujarEquipos(gf, ancho);
                    g.DrawImageUnscaled(cab, MARGEN_CAPTURA + 10, arriba + 10);
                    g.DrawImageUnscaled(filas, MARGEN_CAPTURA + 10, arriba + 10 + ALTO_CABECERA);
                }
            }
            return bmp;
        }

        // Todas las terminales (de 2 en 2), con sus últimas líneas, como se ven en la pantalla.
        Bitmap ImagenTerminales(string leyenda)
        {
            const int COLUMNAS = 2, ALTO_CAJA = 250, SEP = 8, LINEAS = 12, ALTO_LINEA = 16;
            int filas = (terminales.Count + COLUMNAS - 1) / COLUMNAS;
            int anchoCaja = (ANCHO_CAPTURA - 2 * MARGEN_CAPTURA - (COLUMNAS - 1) * SEP) / COLUMNAS;
            int arriba = ALTO_LEYENDA + ALTO_FRANJA + MARGEN_CAPTURA;
            Graphics g;
            var bmp = NuevaImagen(arriba + filas * (ALTO_CAJA + SEP) + MARGEN_CAPTURA, leyenda, out g);
            using (g)
                for (int i = 0; i < terminales.Count; i++)
                {
                    var t = terminales[i];
                    var r = new Rectangle(MARGEN_CAPTURA + (i % COLUMNAS) * (anchoCaja + SEP), arriba + (i / COLUMNAS) * (ALTO_CAJA + SEP),
                                          anchoCaja, ALTO_CAJA);
                    Tema.Rellenar(g, Tema.Consola, r, 10);
                    Tema.Contorno(g, t.Marco.ColorBorde ?? Tema.ConsolaBorde, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 10);
                    TextRenderer.DrawText(g, t.Titulo.Text, Tema.Negrita, new Rectangle(r.X + 12, r.Y + 6, r.Width - 24, 26), t.Titulo.ForeColor,
                                          TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    var lineas = t.Caja.Lines.Where(l => l.Length > 0).ToList();
                    int y = r.Y + 38;
                    foreach (var l in lineas.Skip(Math.Max(0, lineas.Count - LINEAS)))
                    {
                        TextRenderer.DrawText(g, l, Tema.LetraConsolaChica, new Rectangle(r.X + 12, y, r.Width - 24, ALTO_LINEA), Tema.ConsolaTexto,
                                              TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        y += ALTO_LINEA;
                    }
                }
            return bmp;
        }

        // Lo de más de DIAS_GUARDAR días se borra solo (como las carpetas de pings).
        void LimpiarCapturasAutomaticas()
        {
            try
            {
                if (!Directory.Exists(CarpetaCapturasAuto)) return;
                DateTime limite = Reloj.Ahora.AddDays(-DIAS_GUARDAR);
                foreach (var f in new DirectoryInfo(CarpetaCapturasAuto).GetFiles("*.png").Where(a => a.LastWriteTime < limite))
                    try { f.Delete(); }
                    catch (Exception ex) { Programa.AnotarError("No se pudo borrar la captura vieja " + f.Name, ex); }
            }
            catch (Exception ex) { Programa.AnotarError("No se pudieron revisar las capturas automáticas", ex); }
        }
    }
}
