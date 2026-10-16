// Punto de entrada: abre una sola copia de Yedsa y muestra la ventana principal.
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

// Datos que Windows muestra en las Propiedades del .exe.
[assembly: System.Reflection.AssemblyTitle("Yedsa")]
[assembly: System.Reflection.AssemblyProduct("Yedsa")]
[assembly: System.Reflection.AssemblyDescription("Monitor de cortes de red")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 " + Yedsa.Programa.AUTOR)]
[assembly: System.Reflection.AssemblyVersion(Yedsa.Programa.VERSION)]
[assembly: System.Reflection.AssemblyFileVersion(Yedsa.Programa.VERSION)]

namespace Yedsa
{
    static class Programa
    {
        public const string NOMBRE = "Yedsa";
        public const string VERSION = "1.0.0";
        public const string AUTOR = "Eloy Flores";
        public const string WEB = "https://github.com/eloyfloreseloy/Yedsa";

        [STAThread]
        static void Main(string[] args)
        {
            bool oculto = args.Any(a => a.Equals("/minimizado", StringComparison.OrdinalIgnoreCase));
            // Una sola copia por carpeta: si ya hay una abierta, se le pide que se muestre y esta copia se cierra.
            string id = "Local\\Yedsa_" + Math.Abs(Application.ExecutablePath.ToLowerInvariant().GetHashCode());
            bool reiniciar = false, primero;
            using (var unico = new Mutex(true, id, out primero))
            {
                if (!primero)
                {
                    if (!oculto) try { EventWaitHandle.OpenExisting(id + "_mostrar").Set(); } catch { }
                    return;
                }
                using (var mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, id + "_mostrar"))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.ThreadException += (s, a) =>
                    {
                        try
                        {
                            string errores = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "errores.txt");
                            if (File.Exists(errores) && new FileInfo(errores).Length > 1024 * 1024) File.Delete(errores);   // máximo 1 MB
                            File.AppendAllText(errores, DateTime.Now + "  " + a.Exception + "\r\n\r\n");
                        }
                        catch { }
                    };
                    Tema.Aplicar(Ajustes.Leer("tema") != "claro"); // oscuro salvo que se haya elegido claro
                    var form = new VentanaPrincipal(oculto, mostrar);
                    Application.Run(form);
                    reiniciar = form.Reiniciar;
                }
                unico.ReleaseMutex();
            }
            if (reiniciar) Process.Start(Application.ExecutablePath);
        }
    }
}
