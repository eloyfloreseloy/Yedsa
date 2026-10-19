// Archivos por día, historial de cortes, notas y reporte para el proveedor.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace Yedsa
{
    partial class VentanaPrincipal
    {
        // registros\dias\AAAA-MM-DD: una carpeta por día con todos los pings y el bloc de notas de cada equipo.
        string CarpetaDias { get { return Path.Combine(carpeta, "dias"); } }

        string CarpetaDia(DateTime cuando)
        {
            string dir = Path.Combine(CarpetaDias, cuando.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        static bool EsFecha(string texto)
        {
            DateTime d;
            return DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        }

        // ---------- Límites: nada crece para siempre ----------

        const long MAX_MB_REGISTROS = 1024;   // carpetas de días: 7 días o 1 GB, lo que llegue primero
        const int MAX_HISTORIAL = 10000;      // cortes guardados en incidentes.csv (años de uso)
        const int MAX_CAPTURAS = 100;         // capturas de pantalla
        const int MAX_REPORTES = 30;          // reportes HTML
        const int FILAS_PANTALLA = 1000;      // filas del historial que se muestran en la pantalla (el archivo guarda más)

        // Deja solo las DIAS_GUARDAR carpetas de días más recientes y, si aun así pasan de MAX_MB_REGISTROS,
        // borra las más viejas (nunca la de hoy). Con muchos equipos un día puede pesar cientos de MB.
        void LimpiarDiasViejos()
        {
            try
            {
                if (!Directory.Exists(CarpetaDias)) return;
                var dias = Directory.GetDirectories(CarpetaDias).Select(Path.GetFileName)
                                    .Where(EsFecha).OrderByDescending(n => n).ToList();
                foreach (var n in dias.Skip(DIAS_GUARDAR))
                    try { Directory.Delete(Path.Combine(CarpetaDias, n), true); }
                    catch (Exception ex) { Programa.AnotarError("No se pudo borrar la carpeta vieja " + n, ex); }

                string hoy = Reloj.Ahora.Date.ToString("yyyy-MM-dd");
                long total = 0;
                foreach (var n in dias.Take(DIAS_GUARDAR))
                {
                    string dir = Path.Combine(CarpetaDias, n);
                    total += new DirectoryInfo(dir).GetFiles("*", SearchOption.AllDirectories).Sum(a => a.Length);
                    if (total > MAX_MB_REGISTROS * 1024 * 1024 && n != hoy)
                        try { Directory.Delete(dir, true); }
                        catch (Exception ex) { Programa.AnotarError("No se pudo borrar la carpeta vieja " + n, ex); }
                }
            }
            catch (Exception ex) { Programa.AnotarError("No se pudieron revisar las carpetas de días", ex); }
            LimpiarCapturasAutomaticas();
        }

        // registros\LEEME.txt: explica qué hay en cada carpeta y qué se borra solo. Se reescribe al abrir el programa
        // para que siempre coincida con los límites de esta versión.
        void EscribirLeeme()
        {
            string texto =
                "QUÉ HAY EN ESTA CARPETA (" + Programa.NOMBRE + " " + Programa.VERSION + ")\r\n" +
                "==========================================\r\n\r\n" +
                "dias\\AAAA-MM-DD\\\r\n" +
                "    Una carpeta por día:\r\n" +
                "    - todos_los_pings.csv: cada ping de cada equipo, con fecha y hora (se abre en Excel).\r\n" +
                "      Excel abre hasta un millón de filas: si en un día hay más (muchos equipos), sigue en\r\n" +
                "      \"todos_los_pings (parte 2).csv\", \"(parte 3)\"...\r\n" +
                "    - Un .txt por equipo: lo mismo que muestra su terminal \"ping -t\", con la hora.\r\n" +
                "    Se guardan los últimos " + DIAS_GUARDAR + " días, siempre que no pasen de " + MAX_MB_REGISTROS / 1024 + " GB en total.\r\n" +
                "    Si pasan de ese tamaño (por ejemplo, con muchos equipos), se borran los días más viejos.\r\n" +
                "    El día de hoy nunca se borra.\r\n\r\n" +
                "incidentes.csv\r\n" +
                "    El historial: cortes, avisos que no fueron corte, red inestable, equipos sin respuesta,\r\n" +
                "    caídas anotadas a mano, cambios de IP pública, mantenimiento, ratos sin vigilar y cambios de camino.\r\n" +
                "    Se guardan los últimos " + MAX_HISTORIAL + "; los más viejos se borran solos.\r\n\r\n" +
                "latido.txt\r\n" +
                "    La última hora en que " + Programa.NOMBRE + " estaba vigilando. Sirve para saber, al abrirlo, cuánto rato no vigiló.\r\n\r\n" +
                "capturas\\\r\n" +
                "    Las capturas de pantalla (\"Capturar pantalla\"). Se guardan las últimas " + MAX_CAPTURAS + ".\r\n" +
                "capturas\\automaticas\\\r\n" +
                "    Las que " + Programa.NOMBRE + " toma solo cuando pasa algo (un corte, la red inestable...): una del panel y otra\r\n" +
                "    de las terminales. Se guardan " + DIAS_GUARDAR + " días.\r\n\r\n" +
                "reportes\\\r\n" +
                "    Los reportes para el proveedor (se abren en el navegador). Se guardan los últimos " + MAX_REPORTES + ".\r\n\r\n" +
                "CUÁNDO SE BORRA\r\n" +
                "    Al abrir " + Programa.NOMBRE + " y a medianoche, si sigue abierto.\r\n\r\n" +
                "SI QUIERES GUARDAR ALGO PARA SIEMPRE\r\n" +
                "    Cópialo a otra carpeta fuera de \"registros\" (por ejemplo, Documentos).\r\n" +
                "    Lo que esté fuera de esta carpeta nunca se borra.\r\n";
            try { File.WriteAllText(Path.Combine(carpeta, "LEEME.txt"), texto, new UTF8Encoding(true)); }
            catch (Exception ex) { Programa.AnotarError("No se pudo escribir registros\\LEEME.txt", ex); }
        }

        // Deja solo los últimos MAX_HISTORIAL cortes en incidentes.csv.
        void RecortarHistorial()
        {
            try
            {
                if (!File.Exists(rutaIncidentes)) return;
                var lineas = File.ReadAllLines(rutaIncidentes, Encoding.UTF8);
                if (lineas.Length - 1 <= MAX_HISTORIAL) return;
                var quedan = new[] { CABECERA }.Concat(lineas.Skip(lineas.Length - MAX_HISTORIAL));
                File.WriteAllLines(rutaIncidentes, quedan, new UTF8Encoding(true));
            }
            catch (Exception ex) { Programa.AnotarError("No se pudo recortar incidentes.csv (se intenta de nuevo al abrir)", ex); }
        }

        // Deja solo los 'cuantos' archivos más nuevos que cumplan el patrón dentro de una carpeta.
        static void DejarUltimos(string dir, string patron, int cuantos)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in new DirectoryInfo(dir).GetFiles(patron).OrderByDescending(a => a.LastWriteTime).Skip(cuantos))
                    try { f.Delete(); }
                    catch (Exception ex) { Programa.AnotarError("No se pudo borrar " + f.Name, ex); }
            }
            catch (Exception ex) { Programa.AnotarError("No se pudo revisar " + dir, ex); }
        }

        static string NombreArchivo(Equipo e)
        {
            string n = e.Nombre + " (" + e.Ip + ").txt";
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n;
        }

        // Cada línea de la terminal va también al bloc de notas del día de ese equipo.
        void EscribirTerminal(Terminal t, string texto, DateTime cuando)
        {
            try
            {
                string fecha = cuando.ToString("yyyy-MM-dd");
                if (t.Archivo == null || t.ArchivoFecha != fecha)
                {
                    if (t.Archivo != null) t.Archivo.Dispose();
                    string ruta = Path.Combine(CarpetaDia(cuando), NombreArchivo(t.Equipo));
                    var fs = new FileStream(ruta, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    t.Archivo = new StreamWriter(fs, new UTF8Encoding(true));
                    t.ArchivoFecha = fecha;
                    if (fs.Position > 0) t.Archivo.WriteLine();
                    t.Archivo.WriteLine("========== " + cuando.ToString("dd/MM/yyyy HH:mm:ss") + "  —  " + t.Equipo.Nombre +
                                        "  —  C:\\>ping -t " + t.Equipo.Ip + " ==========");
                }
                t.Archivo.WriteLine(texto.Length > 0 ? "[" + cuando.ToString("HH:mm:ss") + "] " + texto : "");
            }
            catch (Exception ex)
            {
                t.Archivo = null;   // se reintenta abrir con la próxima línea
                FalloDisco(NombreArchivo(t.Equipo), ex);
            }
        }

        // Falla al escribir un registro que se escribe todo el tiempo (pings, terminales): se anota en errores.txt y se
        // avisa junto al reloj, como mucho una vez cada 10 minutos por archivo (si no, sería un aviso por segundo).
        readonly Dictionary<string, double> ultimoFalloDisco = new Dictionary<string, double>();

        void FalloDisco(string archivo, Exception ex)
        {
            double ahora = Reloj.Segundos, antes;
            if (ultimoFalloDisco.TryGetValue(archivo, out antes) && ahora - antes < 600) return;
            ultimoFalloDisco[archivo] = ahora;
            Programa.AnotarError("No se pudo escribir " + archivo, ex);
            if (!cerrando)
                tray.ShowBalloonTip(10000, "No se pudo guardar " + archivo, ex.Message +
                    " Si el disco está lleno o la carpeta no deja escribir, lo de ahora no queda registrado.", ToolTipIcon.Warning);
        }

        // Excel abre hasta 1.048.576 filas. Con muchos equipos un día tiene más (30 equipos = 2,6 millones),
        // así que al llegar a este número de filas se sigue en otro archivo del mismo día: "(parte 2)", "(parte 3)"...
        const int MAX_FILAS_CSV = 1000000;
        int logFilas;   // filas que ya tiene el archivo abierto (con el título)

        static string NombreParte(int parte)
        {
            return parte <= 1 ? "todos_los_pings.csv" : "todos_los_pings (parte " + parte + ").csv";
        }

        static int ContarLineas(string ruta)
        {
            int n = 0;
            using (var fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var buf = new byte[1 << 16];
                int leidos;
                while ((leidos = fs.Read(buf, 0, buf.Length)) > 0)
                    for (int i = 0; i < leidos; i++) if (buf[i] == (byte)'\n') n++;
            }
            return n;
        }

        // Abre la última parte del día (o la siguiente, si la última ya está llena).
        void AbrirLogPings(DateTime cuando)
        {
            string dir = CarpetaDia(cuando);
            int parte = 1;
            while (File.Exists(Path.Combine(dir, NombreParte(parte + 1)))) parte++;
            string ruta = Path.Combine(dir, NombreParte(parte));
            int filas = File.Exists(ruta) ? ContarLineas(ruta) : 0;
            if (filas > MAX_FILAS_CSV)
            {
                ruta = Path.Combine(dir, NombreParte(parte + 1));
                filas = 0;
            }
            var fs = new FileStream(ruta, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            logPings = new StreamWriter(fs, new UTF8Encoding(true));
            if (filas == 0) { logPings.WriteLine("FechaHora;Equipo;IP;Resultado;ms"); filas = 1; }
            logFilas = filas;
        }

        void EscribirPing(DateTime cuando, Equipo e, bool ok, long ms)
        {
            try
            {
                string fecha = cuando.ToString("yyyy-MM-dd");
                if (logPings == null || fecha != logFecha)
                {
                    if (logPings != null) { logPings.Dispose(); LimpiarDiasViejos(); RecortarHistorial(); }   // cambió el día
                    AbrirLogPings(cuando);
                    logFecha = fecha;
                }
                else if (logFilas > MAX_FILAS_CSV)
                {
                    logPings.Dispose();   // esta parte ya está llena: se sigue en la siguiente
                    AbrirLogPings(cuando);
                }
                logPings.WriteLine(cuando.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture) + ";" + e.Nombre + ";" + e.Ip + ";" +
                                   (ok ? "OK;" + ms : "SIN RESPUESTA;"));
                logFilas++;
            }
            catch (Exception ex)
            {
                logPings = null;   // se reintenta abrir con el próximo ping
                FalloDisco("todos_los_pings.csv", ex);
            }
        }

        // Una fila de incidentes.csv: sin ";" ni saltos de línea dentro de los campos.
        static string Linea(string[] campos)
        {
            return string.Join(";", campos.Select(x => x.Replace(";", ",").Replace("\n", " ").Replace("\r", "")));
        }

        void GuardarIncidente(string[] campos)
        {
            pendientes.Append(Linea(campos));
            pendientes.Append("\r\n");
            // Si incidentes.csv queda bloqueado muchísimo tiempo, lo pendiente no crece sin fin (máximo ~1 MB):
            // la mitad más vieja se guarda aparte, en incidentes_rescatados.csv, en vez de perderse.
            if (pendientes.Length > 1000000)
            {
                string texto = pendientes.ToString();
                int corte = texto.IndexOf("\r\n", texto.Length / 2) + 2;
                try
                {
                    File.AppendAllText(Path.Combine(carpeta, "incidentes_rescatados.csv"), texto.Substring(0, corte), new UTF8Encoding(true));
                    Programa.AnotarError("incidentes.csv lleva mucho bloqueado: parte del historial se guardó en incidentes_rescatados.csv", null);
                }
                catch (Exception ex) { Programa.AnotarError("Se perdió parte del historial (incidentes.csv bloqueado y no se pudo rescatar)", ex); }
                pendientes.Remove(0, corte);
            }
            IntentarGuardar();
        }

        // Si incidentes.csv está abierto en Excel, Windows no deja escribirlo: se reintenta cada segundo.
        // Si sigue así más de 30 segundos, se avisa (y cada 10 minutos, mientras dure), para que nadie crea que se está guardando.
        double sinGuardarDesde, proximoAvisoGuardar;   // en Reloj.Segundos; 0 = se está guardando bien
        bool avisoSinGuardar;                         // ya se avisó: al volver a guardar, se avisa que se arregló

        void IntentarGuardar()
        {
            if (pendientes.Length == 0) return;
            try
            {
                bool nuevo = !File.Exists(rutaIncidentes);
                using (var fs = new FileStream(rutaIncidentes, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(true)))
                {
                    if (nuevo) sw.Write(CABECERA + "\r\n");
                    sw.Write(pendientes.ToString());
                }
                pendientes.Clear();
                if (avisoSinGuardar && !cerrando)
                    tray.ShowBalloonTip(5000, "El historial se volvió a guardar", "Ya se guardó todo lo que estaba pendiente.", ToolTipIcon.Info);
                sinGuardarDesde = proximoAvisoGuardar = 0;
                avisoSinGuardar = false;
            }
            catch (Exception ex)
            {
                double ahora = Reloj.Segundos;
                if (sinGuardarDesde == 0) { sinGuardarDesde = ahora; proximoAvisoGuardar = ahora + 30; }
                if (ahora < proximoAvisoGuardar || cerrando) return;
                proximoAvisoGuardar = ahora + 600;
                avisoSinGuardar = true;
                Programa.AnotarError("No se puede guardar incidentes.csv", ex);
                tray.ShowBalloonTip(15000, "No se puede guardar el historial",
                    "incidentes.csv está abierto en otro programa (por ejemplo, Excel). Ciérralo: lo que pase mientras tanto " +
                    "se guarda apenas se pueda.", ToolTipIcon.Warning);
            }
        }

        List<string[]> LeerIncidentes()
        {
            var filas = new List<string[]>();
            if (!File.Exists(rutaIncidentes)) return filas;
            try
            {
                using (var fs = new FileStream(rutaIncidentes, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null)
                    {
                        if (l.StartsWith("Tipo;")) continue;
                        var p = l.Split(';');
                        if (p.Length >= 6) filas.Add(p);
                    }
                }
            }
            catch (Exception ex) { Programa.AnotarError("No se pudo leer incidentes.csv", ex); }
            return filas;
        }

        // Reemplaza una fila ya guardada (por ejemplo, para marcarla como mantenimiento) y recarga el historial.
        void CambiarFila(string[] antes, string[] despues)
        {
            IntentarGuardar();
            if (pendientes.Length > 0)
            {
                Dialogos.Informar(this, "No se pudo cambiar", "El historial (incidentes.csv) está abierto en otro programa, por ejemplo Excel. " +
                                                              "Ciérralo y vuelve a intentarlo.");
                return;
            }
            try
            {
                string buscada = Linea(antes);
                List<string> lineas;
                using (var fs = new FileStream(rutaIncidentes, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                    lineas = sr.ReadToEnd().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                int i = lineas.IndexOf(buscada);
                if (i < 0) return;
                lineas[i] = Linea(despues);
                using (var fs = new FileStream(rutaIncidentes, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                using (var sw = new StreamWriter(fs, new UTF8Encoding(true)))
                    sw.Write(string.Join("\r\n", lineas) + "\r\n");
            }
            catch (Exception ex)
            {
                Dialogos.Informar(this, "No se pudo cambiar", "No se pudo guardar el cambio: " + ex.Message);
                return;
            }
            CargarHistorial();
        }

        // Llena el historial de la pantalla desde incidentes.csv (sin los tipos que el usuario ocultó).
        void CargarHistorial()
        {
            historial.Rows.Clear();
            // La pantalla muestra las últimas FILAS_PANTALLA: solo se agregan esas (el archivo puede tener 10.000).
            var visibles = LeerIncidentes().Where(c => !ocultos.Contains(c[0])).ToList();
            foreach (var campos in visibles.Skip(Math.Max(0, visibles.Count - FILAS_PANTALLA))) AgregarFila(campos);
            historial.ClearSelection();
            MostrarArriba();
        }

        // Lo más nuevo, arriba: al agregar filas por arriba, la grilla a veces se queda mirando más abajo.
        void MostrarArriba()
        {
            try { if (historial.Rows.Count > 0) historial.FirstDisplayedScrollingRowIndex = 0; }
            catch (InvalidOperationException) { }   // la grilla todavía no tiene tamaño (arrancó escondido)
        }

        void AgregarFila(string[] c)
        {
            if (ocultos.Contains(c[0])) return;   // igual queda guardado en incidentes.csv y sale en el reporte
            double seg;
            string dur = double.TryParse(c[3], NumberStyles.Any, CultureInfo.InvariantCulture, out seg) ? Dur(seg) : "";
            bool estabaArriba = historial.Rows.Count == 0 || historial.FirstDisplayedScrollingRowIndex <= 0;
            historial.Rows.Insert(0, c[0], c[1], c[2], dur, c[4], c[5]);
            if (estabaArriba) MostrarArriba();   // si alguien estaba mirando filas viejas, no se le mueve
            historial.Rows[0].Tag = c;   // los datos completos, para la ventanita de detalle
            var tipo = historial.Rows[0].Cells[0];
            tipo.Style.ForeColor = tipo.Style.SelectionForeColor = Tipos.Color(c[0]);
            tipo.Style.Font = Tema.Negrita;
            while (historial.Rows.Count > FILAS_PANTALLA) historial.Rows.RemoveAt(historial.Rows.Count - 1);
            historial.ClearSelection();
        }

        void AnotarCaida()
        {
            DateTime ahora = Reloj.Ahora;
            string nota = Dialogos.PedirTexto(this, "Anotar caída reportada",
                "¿Qué pasó? Se guarda con la hora de ahora (" + ahora.ToString("HH:mm:ss") + ").",
                "Se cayó el juego / internet en las PCs");
            if (nota == null || nota.Trim().Length == 0) return;

            string estado;
            if (actual != null)
                estado = "En ese momento HABÍA un corte en curso: " + string.Join(", ", actual.Caidos.Select(x => x.Nombre)) + " sin respuesta.";
            else if (ultimoFin != DateTime.MinValue && (ahora - ultimoFin).TotalMinutes < 3)
                estado = "Hubo un corte detectado que terminó hace " + Dur((ahora - ultimoFin).TotalSeconds) + ".";
            else
                estado = "En ese momento TODOS los equipos respondían (incluido internet). Posible causa: falla del servidor del juego, o un corte muy corto del proveedor.";
            if (ultimoCambioIp != DateTime.MinValue && (ahora - ultimoCambioIp).TotalMinutes < 5)
                estado = "La IP pública cambió hace " + Dur((ahora - ultimoCambioIp).TotalSeconds) + " (" + ultimoCambioIpTexto +
                         "): eso explica la caída de los juegos. " + estado;

            var campos = new[] { Tipos.Anotado, ahora.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture), "", "", nota.Trim(), estado };
            GuardarIncidente(campos);
            AgregarFila(campos);
        }

        void GenerarReporte()
        {
            // El historial crece sin límite: el reporte solo incluye el periodo y los tipos que se elijan.
            // Se recuerda lo elegido la última vez (la primera vez: últimos 7 días, lo que sirve para el proveedor).
            int periodo;
            if (!int.TryParse(Ajustes.Leer("reporte_periodo"), out periodo) || periodo < 0 || periodo > 3) periodo = 1;
            var tipos = Tipos.Leer(Ajustes.Leer("reporte_tipos"), Tipos.ParaProveedor);
            if (!Dialogos.ElegirReporte(this, ref periodo, tipos)) return;
            Ajustes.Poner("reporte_periodo", periodo.ToString(CultureInfo.InvariantCulture));
            Ajustes.Poner("reporte_tipos", Tipos.Escribir(tipos));

            DateTime desde = periodo == 0 ? Reloj.Ahora.Date : periodo == 1 ? Reloj.Ahora.Date.AddDays(-6)
                           : periodo == 2 ? Reloj.Ahora.Date.AddDays(-29) : DateTime.MinValue;
            string textoPeriodo = periodo == 3 ? "todo el historial"
                                : "del " + desde.ToString("dd/MM/yyyy") + " al " + Reloj.Ahora.Date.ToString("dd/MM/yyyy");
            var filas = LeerIncidentes().Where(f =>
            {
                DateTime d;
                return tipos.Contains(f[0]) &&
                       DateTime.TryParseExact(f[1], FORMATO_FECHA, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) && d >= desde;
            }).ToList();
            var cortes = filas.Where(f => f[0] == Tipos.Corte).ToList();
            Func<string[], double> seg = f =>
            {
                double s;
                return double.TryParse(f[3], NumberStyles.Any, CultureInfo.InvariantCulture, out s) ? s : 0;
            };
            Func<string, string> H = WebUtility.HtmlEncode;

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>Reporte de cortes de red</title><style>" +
                      "body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#222}h1{margin-bottom:4px}" +
                      "table{border-collapse:collapse;width:100%;margin:12px 0 28px}th,td{border:1px solid #ccc;padding:6px 8px;text-align:left;font-size:14px;vertical-align:top}" +
                      "th{background:#f0f0f0}.corte td:first-child{color:#a00;font-weight:bold}.anot td:first-child{color:#1e46a0;font-weight:bold}.ip td:first-child{color:#7828a0;font-weight:bold}.aus td:first-child{color:#a06a00;font-weight:bold}.inest td:first-child{color:#c44e1c;font-weight:bold}" +
                      ".nofue td:first-child{color:#777;font-weight:bold}.local td:first-child{color:#b22c68;font-weight:bold}" +
                      ".mant td:first-child{color:#127882;font-weight:bold}.hueco td:first-child{color:#888;font-weight:bold}" +
                      ".dns td:first-child{color:#547a0a;font-weight:bold}.lento td:first-child{color:#967000;font-weight:bold}.hora td:first-child{color:#d63c3a;font-weight:bold}.nota{color:#666;font-size:13px}</style></head><body>");
            sb.Append("<h1>Reporte de cortes de red</h1>");
            sb.Append("<p><b>Periodo:</b> " + textoPeriodo + "</p>");
            sb.Append("<p><b>Horas:</b> " + H(TimeZoneInfo.Local.DisplayName) + ". Se comparan cada minuto con la hora de internet" +
                      (Reloj.Ajuste != TimeSpan.Zero ? "; esta PC tiene la hora mal y por eso se usó la de internet." : ".") + "</p>");
            sb.Append("<p><b>Incluye:</b> " + H(string.Join(", ", Tipos.Todos.Where(tipos.Contains).Select(Tipos.Plural))) + ".</p>");
            sb.Append("<p class='nota'>Generado el " + Reloj.Ahora.ToString("dd/MM/yyyy HH:mm") +
                      ". Método: desde una PC de la red local se envía 1 ping por segundo a cada equipo del camino hacia internet. " +
                      "Se considera <b>corte</b> cuando un equipo deja de responder " + UMBRAL + " o más pings seguidos y ninguno " +
                      "de los que están después en el camino responde; para internet se vigilan varios destinos y solo hay corte si " +
                      "ninguno responde. El diagnóstico indica el primer equipo del camino que dejó de responder: si es un equipo " +
                      "del proveedor o un destino en internet, el corte es del proveedor.</p>");

            sb.Append("<h2>Equipos vigilados (en orden, desde la red local hacia internet)</h2><table><tr><th>#</th><th>Equipo</th><th>IP</th><th>Qué es</th></tr>");
            foreach (var e in equipos)
                sb.Append("<tr><td>" + (e.Indice + 1) + "</td><td>" + H(e.Nombre) + "</td><td>" + H(e.Ip) + "</td><td>" + H(Roles.Nombre(e.Rol)) + "</td></tr>");
            sb.Append("</table>");

            // Resumen: solo de lo que se eligió incluir.
            sb.Append("<h2>Resumen</h2>");
            if (tipos.Contains(Tipos.Corte))
            {
                sb.Append("<h3>Cortes del proveedor</h3><table><tr><th>Diagnóstico</th><th>Cortes</th><th>Tiempo total sin servicio</th><th>Corte más largo</th></tr>");
                foreach (var grupo in cortes.GroupBy(f => f[5]).OrderByDescending(x => x.Count()))
                    sb.Append("<tr><td>" + H(grupo.Key) + "</td><td>" + grupo.Count() + "</td><td>" + Dur(grupo.Sum(seg)) + "</td><td>" + Dur(grupo.Max(seg)) + "</td></tr>");
                sb.Append("<tr><th>Total</th><th>" + cortes.Count + "</th><th>" + Dur(cortes.Sum(seg)) + "</th><th></th></tr></table>");
            }
            if (tipos.Contains(Tipos.CorteLocal))
            {
                var locales = filas.Where(f => f[0] == Tipos.CorteLocal).ToList();
                sb.Append("<p><b>Cortes del local:</b> " + locales.Count + (locales.Count > 0 ? " (" + Dur(locales.Sum(seg)) + " en total)" : "") +
                          ". Empezaron en un equipo del local (un router, un cable, esta PC): no son del proveedor.</p>");
            }
            if (tipos.Contains(Tipos.Dns))
            {
                var dns = filas.Where(f => f[0] == Tipos.Dns).ToList();
                sb.Append("<p><b>Fallas de DNS:</b> " + dns.Count + (dns.Count > 0 ? " (" + Dur(dns.Sum(seg)) + " en total)" : "") +
                          ". El ping funcionaba, pero las páginas no cargaban porque el DNS no contestaba.</p>");
            }
            if (tipos.Contains(Tipos.Lento))
            {
                var lentos = filas.Where(f => f[0] == Tipos.Lento).ToList();
                sb.Append("<p><b>Ratos de internet lento:</b> " + lentos.Count + (lentos.Count > 0 ? " (" + Dur(lentos.Sum(seg)) + " en total)" : "") +
                          ". Todo respondía, pero con tanta demora (más de " + LENTO + " ms) que los juegos en línea se traban.</p>");
            }
            if (tipos.Contains(Tipos.SinVigilar))
            {
                var huecos = filas.Where(f => f[0] == Tipos.SinVigilar).ToList();
                sb.Append("<p><b>Ratos sin vigilar:</b> " + huecos.Count + (huecos.Count > 0 ? " (" + Dur(huecos.Sum(seg)) + " en total)" : "") +
                          ". La PC estuvo apagada, suspendida o sin luz, o el programa estaba cerrado: en esos ratos no hay datos.</p>");
            }
            if (tipos.Contains(Tipos.Camino))
                sb.Append("<p><b>Cambios de camino:</b> " + filas.Count(f => f[0] == Tipos.Camino) +
                          ". Veces en que el camino hacia internet (los equipos del local o del proveedor) cambió.</p>");
            if (tipos.Contains(Tipos.Mantenimiento))
                sb.Append("<p><b>Mantenimiento:</b> " + filas.Count(f => f[0] == Tipos.Mantenimiento) +
                          ". Ratos en que estaban configurando la red; lo que pasó ahí no cuenta.</p>");
            if (tipos.Contains(Tipos.CambioIp))
            {
                int cambiosIp = filas.Count(f => f[0] == Tipos.CambioIp);
                sb.Append("<p><b>Cambios de IP pública detectados:</b> " + cambiosIp +
                          (cambiosIp > 0 ? " (cada cambio corta las partidas online y conexiones abiertas de todo el local)." : "") + "</p>");
            }
            if (tipos.Contains(Tipos.Anotado))
                sb.Append("<p><b>Caídas anotadas a mano:</b> " + filas.Count(f => f[0] == Tipos.Anotado) + "</p>");
            if (tipos.Contains(Tipos.NoFueCorte))
                sb.Append("<p><b>Avisos que no fueron corte:</b> " + filas.Count(f => f[0] == Tipos.NoFueCorte) +
                          ". Un equipo no contestó los pings, pero otro que está después en el camino sí respondía: el internet siguió funcionando.</p>");
            if (tipos.Contains(Tipos.Inestable))
            {
                var inestables = filas.Where(f => f[0] == Tipos.Inestable).ToList();
                sb.Append("<p><b>Ratos de red inestable:</b> " + inestables.Count +
                          (inestables.Count > 0 ? " (" + Dur(inestables.Sum(seg)) + " en total)" : "") +
                          ". Se perdieron pings sueltos sin llegar a cortarse; en los juegos se nota como lag o tirones.</p>");
            }
            if (tipos.Contains(Tipos.SinRespuesta))
                sb.Append("<p><b>Equipos que dejaron de responder un rato:</b> " + filas.Count(f => f[0] == Tipos.SinRespuesta) +
                          ". Equipos que pueden estar apagados y volvieron en pocos minutos (no se cortó el internet).</p>");

            sb.Append("<h2>Detalle</h2><table><tr><th>Tipo</th><th>Inicio</th><th>Fin</th><th>Duración</th><th>Qué se cayó / nota</th><th>Diagnóstico</th></tr>");
            foreach (var f in filas)
                sb.Append("<tr class='" + Tipos.Clase(f[0]) + "'><td>" + H(f[0]) + "</td><td>" + H(f[1]) + "</td><td>" + H(f[2]) +
                          "</td><td>" + (f[3].Length > 0 ? Dur(seg(f)) : "") + "</td><td>" + H(f[4]) + "</td><td>" + H(f[5]) + "</td></tr>");
            if (filas.Count == 0) sb.Append("<tr><td colspan='6'>No hay nada registrado en este periodo.</td></tr>");
            sb.Append("</table><p class='nota'>Registro completo de cada ping: una carpeta por día con todos_los_pings.csv y un archivo .txt por equipo con la salida de ping -t.</p></body></html>");

            string dirReportes = Path.Combine(carpeta, "reportes");
            Directory.CreateDirectory(dirReportes);
            string ruta = Path.Combine(dirReportes, "reporte_" + Reloj.Ahora.ToString("yyyy-MM-dd_HHmm") + ".html");
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
            DejarUltimos(dirReportes, "reporte_*.html", MAX_REPORTES);
            Process.Start(ruta);
        }
    }
}
