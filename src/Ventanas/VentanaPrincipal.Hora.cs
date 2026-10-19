// ¿La PC tiene bien la hora? Todo lo que Yedsa anota (terminales, pings, historial, reportes, Telegram) lleva hora:
// si la de la PC está mal, las pruebas para el proveedor no sirven. Cada minuto se compara con la hora de internet
// (viene en las respuestas de la consulta de IP pública) y, si está mal, Yedsa usa la de internet y avisa.
using System;
using System.Globalization;
using System.Media;
using System.Windows.Forms;

namespace Yedsa
{
    partial class VentanaPrincipal
    {
        const int SEGUNDOS_HORA_MAL = 120;   // menos que esto puede ser la demora de la red: no se toca
        bool horaMal;

        // Al abrir: si la última vez la PC tenía la hora mal, se usa esa misma corrección hasta poder compararla otra vez.
        // Así lo primero que se anota (y el rato sin vigilar) ya sale con la hora buena.
        static void CargarAjusteHora()
        {
            double seg;
            if (double.TryParse(Ajustes.Leer("ajuste_hora_seg"), NumberStyles.Any, CultureInfo.InvariantCulture, out seg))
                Reloj.Corregir(TimeSpan.FromSeconds(seg));
        }

        // diferencia = hora de internet menos hora de la PC, en segundos.
        void RevisarHora(double diferencia)
        {
            bool mal = Math.Abs(diferencia) > SEGUNDOS_HORA_MAL;
            var nuevo = mal ? TimeSpan.FromSeconds(Math.Round(diferencia)) : TimeSpan.Zero;
            if (Math.Abs((nuevo - Reloj.Ajuste).TotalSeconds) > 30)   // la hora del servidor viene en segundos enteros: no se corrige por tan poco
            {
                Reloj.Corregir(nuevo);
                Ajustes.Poner("ajuste_hora_seg", nuevo.TotalSeconds.ToString(CultureInfo.InvariantCulture));
            }
            if (mal == horaMal) return;
            horaMal = mal;

            string cuanto = Dur(Math.Abs(diferencia)) + (diferencia > 0 ? " atrasada" : " adelantada");
            string diag = mal
                ? "La hora de esta PC está " + cuanto + " (la PC dice " + DateTime.Now.ToString("dd/MM HH:mm") + "; internet, " +
                  Reloj.Ahora.ToString("dd/MM HH:mm") + "). Desde ahora " + Programa.NOMBRE + " anota todo con la hora de internet. " +
                  "Arréglala en Configuración → Hora e idioma → Establecer la hora automáticamente."
                : "La hora de esta PC ya está bien. " + Programa.NOMBRE + " vuelve a usar la de la PC.";
            var campos = new[] { Tipos.HoraMal, Reloj.Ahora.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture), "", "",
                                 mal ? "Hora de la PC " + cuanto : "Hora de la PC corregida", diag };
            GuardarIncidente(campos);
            AgregarFila(campos);
            if (!mal) return;
            if (chkSonido.Checked) SystemSounds.Exclamation.Play();
            tray.ShowBalloonTip(10000, "La hora de esta PC está mal (" + cuanto + ")", diag, ToolTipIcon.Warning);
            Telegram.Avisar("⏰ LA HORA DE LA PC ESTÁ MAL (" + cuanto + ")\n" + diag, true);
        }
    }
}
