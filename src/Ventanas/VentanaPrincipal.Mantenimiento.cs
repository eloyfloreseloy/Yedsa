// Modo mantenimiento: mientras alguien configura la red (routers, MikroTik...), lo que pase se anota
// como MANTENIMIENTO y no como corte, no suena ni se avisa por Telegram, y no sale en el reporte para el proveedor.
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
        // Siempre con hora de fin: si alguien se olvida de apagarlo, se apaga solo y no esconde cortes de verdad.
        static readonly int[] MINUTOS_MANTENIMIENTO = { 15, 30, 60, 120 };
        const string FORMATO_FECHA = "yyyy-MM-dd HH:mm:ss";

        DateTime mantDesde = DateTime.MinValue, mantHasta = DateTime.MinValue;
        ItemLateral itemMantenimiento;

        bool EnMantenimiento { get { return mantHasta != DateTime.MinValue; } }

        // Al abrir: si se cerró Yedsa durante un mantenimiento, sigue (o se anota, si ya terminó).
        void CargarMantenimiento()
        {
            DateTime desde, hasta;
            if (!DateTime.TryParseExact(Ajustes.Leer("mantenimiento_desde"), FORMATO_FECHA, CultureInfo.InvariantCulture, DateTimeStyles.None, out desde) ||
                !DateTime.TryParseExact(Ajustes.Leer("mantenimiento_hasta"), FORMATO_FECHA, CultureInfo.InvariantCulture, DateTimeStyles.None, out hasta))
                return;
            mantDesde = desde;
            mantHasta = hasta;
            if (hasta <= Reloj.Ahora) TerminarMantenimiento(hasta, false);
        }

        void GuardarMantenimiento()
        {
            Ajustes.Poner("mantenimiento_desde", EnMantenimiento ? mantDesde.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture) : "");
            Ajustes.Poner("mantenimiento_hasta", EnMantenimiento ? mantHasta.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture) : "");
        }

        // Opción "Mantenimiento" del panel.
        void Mantenimiento()
        {
            if (EnMantenimiento)
            {
                int r = Dialogos.Elegir(this, "Mantenimiento", "El modo mantenimiento está activo hasta las " + mantHasta.ToString("HH:mm") + ".",
                                        new[] { "Terminar ahora (ya acabé)", "Darme 30 minutos más" }, 0);
                if (r == 0) TerminarMantenimiento(Reloj.Ahora, true);
                if (r == 1) { mantHasta = mantHasta.AddMinutes(30); GuardarMantenimiento(); }
                return;
            }
            int i = Dialogos.Elegir(this, "Voy a configurar la red",
                "Úsalo antes de tocar los routers, los MikroTik o los cables. Mientras dure, lo que pase se anota como " +
                "MANTENIMIENTO: no cuenta para el proveedor, no suena y no se avisa por Telegram (solo se avisa que empezó y que terminó).\n\n" +
                "Se apaga solo, para que un olvido no esconda cortes de verdad. ¿Cuánto vas a tardar?",
                MINUTOS_MANTENIMIENTO.Select(m => m < 60 ? m + " minutos" : m / 60 + (m == 60 ? " hora" : " horas")).ToArray(), 1);
            if (i < 0) return;
            mantDesde = Reloj.Ahora;
            mantHasta = mantDesde.AddMinutes(MINUTOS_MANTENIMIENTO[i]);
            GuardarMantenimiento();
            // Lo que ya está pasando en este momento también es parte del mantenimiento.
            if (actual != null) actual.EnMantenimiento = true;
            if (inestable != null) inestable.EnMantenimiento = true;
            Telegram.Avisar("🔧 MANTENIMIENTO de " + mantDesde.ToString("HH:mm") + " a " + mantHasta.ToString("HH:mm") +
                            "\nEstán configurando la red: hasta esa hora no se avisarán cortes.", true);
            ActualizarEstado();
        }

        // Cada segundo: lo que pasa durante el mantenimiento queda marcado, y al llegar la hora se termina solo.
        void RevisarMantenimiento(DateTime ahora)
        {
            if (!EnMantenimiento) return;
            if (ahora >= mantHasta)
            {
                TerminarMantenimiento(mantHasta, true);
                return;
            }
            if (actual != null) actual.EnMantenimiento = true;
            if (inestable != null) inestable.EnMantenimiento = true;
        }

        // Queda una fila con el rato de mantenimiento, para saber después por qué esas horas no cuentan.
        void TerminarMantenimiento(DateTime fin, bool avisar)
        {
            double seg = Math.Max(0, (fin - mantDesde).TotalSeconds);
            var campos = new[] { Tipos.Mantenimiento, mantDesde.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture), fin.ToString("HH:mm:ss"),
                                 Math.Round(seg).ToString(CultureInfo.InvariantCulture), "Modo mantenimiento (" + Dur(seg) + ")",
                                 "Estaban configurando la red. Lo que pasó en este rato está marcado como MANTENIMIENTO y no cuenta para el proveedor." };
            mantDesde = mantHasta = DateTime.MinValue;
            GuardarMantenimiento();
            GuardarIncidente(campos);
            AgregarFila(campos);
            if (!avisar || cerrando) return;
            tray.ShowBalloonTip(8000, "Terminó el mantenimiento", "Se vigila normal otra vez.", ToolTipIcon.Info);
            Telegram.Avisar("✅ TERMINÓ EL MANTENIMIENTO (" + fin.ToString("HH:mm") + ")\nSe vigila normal otra vez.", true);
            ActualizarEstado();
        }

        // El ícono del panel queda encendido mientras dura, con la hora de fin.
        void ActualizarItemMantenimiento()
        {
            if (itemMantenimiento == null) return;
            itemMantenimiento.Activo = EnMantenimiento;
            itemMantenimiento.Text = EnMantenimiento ? "Mantenimiento (hasta " + mantHasta.ToString("HH:mm") + ")" : "Mantenimiento";
        }

        // ---------- Marcar una fila después ----------
        // Clic derecho en el historial: "Fue mantenimiento", por si se olvidó activar el modo antes de configurar.

        void MenuFila(string[] campos, Point dondeEnPantalla)
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new MenuConTema();
            menu.BackColor = Tema.Superficie;
            menu.Items.Add("Ver detalle", null, delegate { Dialogos.Detalle(this, campos); });
            var original = Tipos.SinMantenimiento(campos);
            if (original != null)
                menu.Items.Add("Quitar la marca de mantenimiento (vuelve a ser " + original[0] + ")", null,
                               delegate { CambiarFila(campos, original); });
            else if (campos[0] != Tipos.Mantenimiento && campos[0] != Tipos.Anotado)
                menu.Items.Add("Fue mantenimiento (no cuenta para el proveedor)", null,
                               delegate { CambiarFila(campos, Tipos.ComoMantenimiento(campos)); });
            menu.Closed += delegate { BeginInvoke((Action)menu.Dispose); };
            menu.Show(dondeEnPantalla);
        }
    }
}
