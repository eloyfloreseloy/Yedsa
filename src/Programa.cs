// Punto de entrada: abre una sola copia de Yedsa y muestra la ventana principal.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

// Datos que Windows muestra en las Propiedades del .exe.
[assembly: System.Reflection.AssemblyTitle("Yedsa")]
[assembly: System.Reflection.AssemblyProduct("Yedsa")]
[assembly: System.Reflection.AssemblyDescription("Monitor de cortes de red")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) " + Yedsa.Programa.ANIO + " " + Yedsa.Programa.AUTOR)]
[assembly: System.Reflection.AssemblyVersion(Yedsa.Programa.VERSION)]
[assembly: System.Reflection.AssemblyFileVersion(Yedsa.Programa.VERSION)]

namespace Yedsa
{
    static class Programa
    {
        public const string NOMBRE = "Yedsa";
        public const string VERSION = "1.0.0";
        public const string AUTOR = "Eloy Flores";
        public const string ANIO = "2026";
        public const string WEB = "https://github.com/eloyfloreseloy/Yedsa";

        [STAThread]
        static void Main(string[] args)
        {
            bool oculto = args.Any(a => a.Equals("/minimizado", StringComparison.OrdinalIgnoreCase));
            // Una sola copia por carpeta: si ya hay una abierta, se le pide que se muestre y esta copia se cierra.
            string id = "Local\\Yedsa_" + ((uint)Application.ExecutablePath.ToLowerInvariant().GetHashCode()).ToString("X8");
            bool reiniciar = false, primero;
            using (var unico = new Mutex(true, id, out primero))
            {
                if (!primero)
                {
                    if (!oculto) try { EventWaitHandle.OpenExisting(id + "_mostrar").Set(); } catch { /* la otra copia se está cerrando */ }
                    return;
                }
                using (var mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, id + "_mostrar"))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    // Un error que nadie atrapó no cierra Yedsa: queda anotado y se sigue vigilando.
                    Application.ThreadException += (s, a) => AnotarError("Error inesperado", a.Exception);
                    AppDomain.CurrentDomain.UnhandledException += (s, a) => AnotarError("Error inesperado", a.ExceptionObject as Exception);
                    TaskScheduler.UnobservedTaskException += (s, a) => { AnotarError("Error en segundo plano", a.Exception); a.SetObserved(); };
                    Tema.Aplicar(Ajustes.Leer("tema") != "claro"); // oscuro salvo que se haya elegido claro
                    var form = new VentanaPrincipal(oculto, mostrar);
                    Application.Run(form);
                    reiniciar = form.Reiniciar;
                }
                unico.ReleaseMutex();
            }
            if (reiniciar) Process.Start(Application.ExecutablePath);
        }

        static readonly object candadoErrores = new object();

        // Anota un error en errores.txt, junto al programa (máximo 1 MB: si se pasa, se empieza de nuevo).
        // Para los errores que no se pueden mostrar en el momento, pero que hay que poder revisar después.
        public static void AnotarError(string que, Exception ex)
        {
            lock (candadoErrores)
                try
                {
                    string errores = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "errores.txt");
                    if (File.Exists(errores) && new FileInfo(errores).Length > 1024 * 1024) File.Delete(errores);
                    File.AppendAllText(errores, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + que +
                                                (ex == null ? "" : ": " + ex) + "\r\n\r\n");
                }
                catch { }   // si ni siquiera se puede escribir errores.txt, no hay a dónde avisar
        }
    }
}
