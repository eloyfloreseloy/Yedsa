// Para que Yedsa no quede mal sin que nadie se dé cuenta:
//   - anota los ratos en que no vigiló (PC apagada, suspendida, sin luz, Yedsa cerrado);
//   - revisa cada cierto tiempo que el camino hacia internet sea el mismo (tracert);
//   - avisa si un equipo lleva mucho sin responder mientras el internet pasa (quizá cambió su IP).
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
        // ---------- Ratos sin vigilar ----------
        // Cada 30 segundos se anota la hora en registros\latido.txt. Al abrir, si la última hora es vieja,
        // Yedsa estuvo sin vigilar ese rato. Al cerrarse bien se anota por qué (alguien lo cerró, o Windows al apagar).

        const int MINUTOS_HUECO = 2;          // un hueco más corto (reiniciar Yedsa, cambiar el tema) no se anota
        const int MINUTOS_HUECO_TELEGRAM = 10;
        const int SEGUNDOS_GRACIA = 30;       // al volver de un hueco, la red tarda en conectarse: esos pings no cuentan

        // En Reloj.Segundos: no dependen del reloj de Windows (si alguien lo mueve o Yedsa corrige la hora, no se traban).
        double ultimoTick, proximoLatido;
        double ignorarHasta;   // hasta este momento, los pings fallidos no abren cortes

        string RutaLatido { get { return Path.Combine(carpeta, "latido.txt"); } }

        void EscribirLatido(string cierre)
        {
            try { File.WriteAllText(RutaLatido, Reloj.Ahora.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture) + (cierre == null ? "" : ";" + cierre)); }
            catch { }
        }

        // Al abrir: ¿cuánto tiempo estuvo Yedsa sin vigilar, y por qué?
        void RevisarHuecoAlAbrir()
        {
            if (arrancarOculto) ignorarHasta = Reloj.Segundos + SEGUNDOS_GRACIA;   // arrancó con Windows: la red puede no estar lista
            string texto;
            try { texto = File.Exists(RutaLatido) ? File.ReadAllText(RutaLatido).Trim() : null; }
            catch { texto = null; }
            EscribirLatido(null);
            if (string.IsNullOrEmpty(texto)) return;
            var p = texto.Split(';');
            DateTime ultimo;
            if (!DateTime.TryParseExact(p[0], FORMATO_FECHA, CultureInfo.InvariantCulture, DateTimeStyles.None, out ultimo)) return;
            string motivo = p.Length < 2 ? "Yedsa no se cerró normalmente: se fue la luz, la PC se colgó o se apagó de golpe."
                          : p.Length > 2 && p[2] == "windows" ? "Se apagó o se reinició la PC (Windows cerró Yedsa)."
                          : "Alguien cerró Yedsa.";
            RegistrarHueco(ultimo, Reloj.Ahora, motivo);
        }

        // Mientras corre: si entre un segundo y el siguiente pasó mucho más, la PC estuvo suspendida o congelada.
        void RevisarHuecoEnMarcha(DateTime ahora)
        {
            double seg = Reloj.Segundos;
            if (ultimoTick > 0 && seg - ultimoTick > 30)
            {
                var desde = ahora.AddSeconds(-(seg - ultimoTick));
                // Un corte abierto se cierra a la hora en que se dejó de vigilar: lo que pasó después no se sabe.
                if (actual != null) CerrarIncidente(desde, " (La PC dejó de vigilar durante el corte: se cierra a esa hora.)", false);
                foreach (var e in equipos.Where(x => !x.Apagado)) { e.Caido = false; e.FallosSeguidos = 0; }
                ignorarHasta = seg + SEGUNDOS_GRACIA;
                RegistrarHueco(desde, ahora, "La PC estuvo suspendida o congelada: Yedsa no pudo vigilar.");
            }
            ultimoTick = seg;
            if (seg >= proximoLatido)
            {
                proximoLatido = seg + 30;
                EscribirLatido(null);
            }
        }

        void RegistrarHueco(DateTime desde, DateTime hasta, string motivo)
        {
            double seg = (hasta - desde).TotalSeconds;
            if (seg < MINUTOS_HUECO * 60) return;
            string rango = desde.ToString("dd/MM HH:mm") + " al " + hasta.ToString("dd/MM HH:mm");
            var campos = new[] { Tipos.SinVigilar, desde.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture), hasta.ToString("HH:mm:ss"),
                                 Math.Round(seg).ToString(CultureInfo.InvariantCulture), "Sin vigilar del " + rango + " (" + Dur(seg) + ")",
                                 motivo + " En ese rato no hay datos: si el proveedor pregunta por esas horas, por eso no hay registro." };
            GuardarIncidente(campos);
            AgregarFila(campos);
            if (seg >= MINUTOS_HUECO_TELEGRAM * 60)
                Telegram.Avisar("⚪ " + Programa.NOMBRE + " VOLVIÓ A VIGILAR\nNo vigiló del " + rango + " (" + Dur(seg) + ").\n" + motivo, true);
        }

        // ---------- ¿Cambió el camino? ----------
        // Cada 30 minutos se hace un tracert y se comparan los primeros saltos (los del local y del proveedor) con los
        // de la vez anterior. Más allá del proveedor no se mira: en internet las rutas cambian solas todo el tiempo.

        const int MINUTOS_CAMINO = 30;
        double proximoCamino = Reloj.Segundos + 180;
        bool revisandoCamino;
        string caminoDistinto;   // el camino distinto de la revisión anterior: se avisa si se repite (uno suelto puede ser casualidad)

        // Cuántos saltos se comparan: los equipos del local y del proveedor que hay en la lista.
        int SaltosCamino { get { return Math.Max(1, equipos.Count(x => x.Rol == Rol.Red || x.Rol == Rol.Proveedor)); } }

        void RevisarCamino(DateTime ahora)
        {
            if (Reloj.Segundos < proximoCamino || revisandoCamino) return;
            proximoCamino = Reloj.Segundos + MINUTOS_CAMINO * 60;
            var destino = equipos.FirstOrDefault(x => EsInternet(x));
            // Durante un corte o un mantenimiento el camino no se puede comparar: se deja para la próxima.
            if (destino == null || actual != null || EnMantenimiento) return;
            revisandoCamino = true;
            int saltos = SaltosCamino;
            Task.Factory.StartNew(() => Red.Tracert(destino.Ip, saltos)).ContinueWith(t =>
            {
                revisandoCamino = false;
                if (!cerrando && t.Result != null) CompararCamino(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void CompararCamino(List<string> camino)
        {
            // El camino de referencia es el de la lista de equipos actual: si se edita la lista, se empieza de nuevo.
            string firmaLista = string.Join(",", equipos.Select(x => x.Ip));
            if (Ajustes.Leer("camino_lista") != firmaLista || Ajustes.Leer("camino") == "")
            {
                Ajustes.Poner("camino_lista", firmaLista);
                Ajustes.Poner("camino", string.Join(",", camino));
                caminoDistinto = null;
                return;
            }
            var conocido = Ajustes.Leer("camino").Split(',').ToList();
            // Un salto que no respondió ("*") no cuenta como cambio.
            bool igual = camino.Count == conocido.Count &&
                         camino.Zip(conocido, (a, b) => a == "*" || b == "*" || a == b).All(x => x);
            string firma = string.Join(",", camino);
            if (igual) { caminoDistinto = null; return; }
            if (caminoDistinto != firma) { caminoDistinto = firma; proximoCamino = Reloj.Segundos + 300; return; }   // se confirma en 5 min

            caminoDistinto = null;
            Ajustes.Poner("camino", firma);
            // Equipos de la lista que estaban en el camino y ya no aparecen.
            var faltan = equipos.Where(x => conocido.Contains(x.Ip) && !camino.Contains(x.Ip)).Select(x => x.Nombre + " (" + x.Ip + ")").ToList();
            string diag = "Antes el internet pasaba por " + string.Join(" → ", conocido) + "; ahora pasa por " + string.Join(" → ", camino) + ". " +
                          (faltan.Count > 0 ? "Ya no aparece: " + string.Join(", ", faltan) + ". " : "") +
                          "Si alguien cambió los routers o el proveedor cambió sus equipos, revisa tu lista en Equipos: " +
                          "un equipo que ya no está en el camino va a parecer caído.";
            var campos = new[] { Tipos.Camino, Reloj.Ahora.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture), "", "",
                                 string.Join(" → ", camino), diag };
            if (EnMantenimiento) campos = Tipos.ComoMantenimiento(campos);
            GuardarIncidente(campos);
            AgregarFila(campos);
            tray.ShowBalloonTip(10000, "Cambió el camino hacia internet", diag, ToolTipIcon.Warning);
            Telegram.Avisar("🟣 CAMBIÓ EL CAMINO HACIA INTERNET\n" + diag, true);
        }

        // ---------- Equipos que llevan mucho sin responder ----------
        // Un equipo de la red o del proveedor que no contesta pero deja pasar el internet queda "apagado" en silencio.
        // Si sigue así mucho rato, lo más probable es que cambió su IP o ya no contesta pings: se avisa una vez.

        const int MINUTOS_AVISO_LARGO = 30;

        void RevisarSilencios(DateTime ahora)
        {
            foreach (var e in equipos)
            {
                bool callado = (e.Apagado && !e.SeApaga) || (e.EsFuera && e.Caido);
                if (!callado || e.AvisadoLargo || (ahora - e.PrimerFallo).TotalMinutes < MINUTOS_AVISO_LARGO) continue;
                e.AvisadoLargo = true;
                string texto = e.Nombre + " (" + e.Ip + ") lleva " + Dur((ahora - e.PrimerFallo).TotalSeconds) + " sin responder" +
                               (e.EsFuera ? "." : ", pero el internet sigue pasando. Puede que haya cambiado su IP o que ya no conteste pings: " +
                                                  "revísalo con \"Probar ping\" en Equipos.");
                tray.ShowBalloonTip(10000, e.Nombre + " no responde hace rato", texto, ToolTipIcon.Warning);
                if (!EnMantenimiento) Telegram.Avisar("🟡 " + texto, true);
            }
        }
    }
}
