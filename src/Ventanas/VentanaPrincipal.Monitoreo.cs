// Cada segundo: ping a cada equipo, detección de cortes, diagnóstico y franja de estado.
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
        // ---------- Pings ----------

        void Tick()
        {
            RevisarHuecoEnMarcha(Reloj.Ahora);   // antes de los pings: si la PC estuvo suspendida, los primeros no cuentan
            foreach (var e in equipos)
                if (!e.EnCurso) HacerPing(e);
            if (logPings != null) try { logPings.Flush(); } catch { logPings = null; }
            foreach (var t in terminales)
                if (t.Archivo != null) try { t.Archivo.Flush(); } catch { t.Archivo = null; }
            IntentarGuardar();
            ActualizarEstado();
            ActualizarTitulos();
            panel.Invalidate();

            DateTime ahora = Reloj.Ahora;
            double seg = Reloj.Segundos;
            if (seg >= proximaConsultaIp)
            {
                proximaConsultaIp = seg + 60;
                ConsultarIpPublica();
            }
            if (seg >= proximoReintentoTelegram)
            {
                proximoReintentoTelegram = seg + 30;
                Telegram.Procesar();
            }
            RevisarMantenimiento(ahora);
            RevisarInestabilidad(ahora);
            RevisarSilencios(ahora);
            RevisarCamino(ahora);
            RevisarDns(ahora);
            RevisarLentitud(ahora);
            // Aviso al celular solo si el corte ya dura 1 minuto (los cortes cortos llegan resumidos al terminar).
            if (actual != null && !actual.AvisadoTelegram && !actual.EnMantenimiento &&
                (ahora - actual.Inicio).TotalSeconds >= 60 && !EsContradiccion(actual))
            {
                actual.AvisadoTelegram = true;
                Telegram.Avisar("🔴 CORTE EN CURSO desde las " + actual.Inicio.ToString("HH:mm:ss") +
                                "\nSin respuesta: " + string.Join(", ", equipos.Where(x => x.Caido && !x.Apagado && !x.EsFuera).Select(Etiqueta)) +
                                "\nProbable: " + Diagnosticar(actual), false);
            }
            // Un equipo que lleva mucho sin responder mientras los de después sí (por ejemplo, una PC apagada)
            // no puede dejar el corte abierto para siempre: se cierra y ese equipo queda como "apagado".
            if (actual != null && EsContradiccion(actual) && (ahora - actual.Inicio).TotalMinutes >= MINUTOS_APAGADO)
            {
                var apagados = equipos.Where(x => x.Caido && !x.Apagado).ToList();
                CerrarIncidente(ahora, " Lleva más de " + MINUTOS_APAGADO + " minutos sin responder: se considera apagado " +
                                       "y no se cuenta en los cortes hasta que vuelva a responder.", false);
                foreach (var e in apagados) e.Apagado = true;
                tray.ShowBalloonTip(8000, string.Join(", ", apagados.Select(x => x.Nombre)) + " parece apagado",
                    "Los demás equipos responden. Se sigue vigilando y se avisará de cortes nuevos por separado.", ToolTipIcon.Info);
            }
        }

        async void HacerPing(Equipo e)
        {
            e.EnCurso = true;
            DateTime cuando = Reloj.Ahora;
            bool ok = false;
            long ms = -1;
            try
            {
                using (var p = new Ping())
                {
                    PingReply r = await p.SendPingAsync(e.Ip, TIMEOUT);
                    if (r.Status == IPStatus.Success) { ok = true; ms = r.RoundtripTime; }
                }
            }
            catch { }
            e.EnCurso = false;
            if (!cerrando) Resultado(e, cuando, ok, ms);
        }

        void Resultado(Equipo e, DateTime cuando, bool ok, long ms)
        {
            e.Enviados++;
            if (ok) e.UltimoMs = ms;
            e.Historial.Add(!ok ? Muestra.Perdido : ms > LENTO ? Muestra.Lento : Muestra.Ok);
            if (e.Historial.Count > HIST) e.Historial.RemoveAt(0);
            EscribirPing(cuando, e, ok, ms);
            // Recién vuelto de un rato sin vigilar (PC suspendida, arranque de Windows) la red tarda en conectarse:
            // esos pings quedan registrados, pero no abren cortes.
            if (Reloj.Segundos < ignorarHasta) return;
            if (ok && !e.Caido) AnotarLatencia(e, cuando, ms);

            // Pings sueltos (no los de un corte ni los de un equipo apagado): sirven para notar si la red está inestable.
            if (!e.Caido && !e.Apagado)
            {
                e.Recientes.Enqueue(new PingReciente { Hora = cuando, Perdido = !ok });
                if (!ok) e.PerdidosRecientes++;
                if (inestable != null) inestable.Sumar(e, cuando, !ok);
            }

            if (ok)
            {
                e.AvisadoLargo = false;
                if (e.Caido)
                {
                    e.Caido = false;
                    if (e.EsFuera)
                    {
                        // Volvió un equipo fuera del camino: queda anotado cuánto no respondió, salvo que haya
                        // caído junto con un corte de la red (entonces es parte de ese corte, no algo suyo).
                        if (actual == null && ultimoFin < e.PrimerFallo) RegistrarAusencia(e, cuando, (cuando - e.PrimerFallo).TotalSeconds);
                    }
                    else if (e.Apagado)
                    {
                        e.Apagado = false;   // volvió: se vigila normal otra vez
                        // Un equipo que "puede estar apagado" y vuelve rápido no se apagó: falló. Queda anotado.
                        double ausencia = (cuando - e.PrimerFallo).TotalSeconds;
                        if (e.SeApaga && ausencia < MINUTOS_AUSENCIA * 60) RegistrarAusencia(e, cuando, ausencia);
                    }
                    else if (actual != null)
                    {
                        Sumar(actual, e, (cuando - e.PrimerFallo).TotalSeconds);
                        if (!equipos.Any(x => x.Caido && !x.Apagado)) CerrarIncidente(cuando, "");
                    }
                }
                e.FallosSeguidos = 0;
                return;
            }

            if (e.FallosSeguidos == 0) e.PrimerFallo = cuando;
            e.FallosSeguidos++;
            if (e.Caido || e.FallosSeguidos < UMBRAL) return;

            // Equipo fuera del camino (otra red, una impresora): el internet no pasa por él, así que nunca es
            // parte de un corte. Se muestra SIN RESPUESTA y, cuando vuelva, se anota cuánto tardó.
            if (e.EsFuera)
            {
                e.Caido = true;
                if (actual == null && !EnMantenimiento)
                    tray.ShowBalloonTip(8000, e.Nombre + " dejó de responder",
                        "Está fuera del camino del internet: no es un corte. Cuando vuelva, se anotará cuánto tardó.", ToolTipIcon.Info);
                return;
            }

            // Equipo que puede estar apagado (ej. una PC): si los que están después siguen respondiendo,
            // el internet no se cortó; solo se apagó. Se marca como apagado y no se registra como corte.
            if (e.SeApaga && Despues(e).Any(x => !x.Apagado && x.Enviados > 0 && x.FallosSeguidos == 0))
            {
                e.Caido = true;
                e.Apagado = true;
                return;
            }

            e.Caido = true;
            e.Cortes++;
            if (actual == null)
            {
                actual = new Incidente();
                actual.Inicio = e.PrimerFallo;
                actual.Caidos.Add(e);
                actual.EnMantenimiento = EnMantenimiento;
                tray.Icon = Icon = iconoMal;
                if (!actual.EnMantenimiento)
                {
                    if (chkSonido.Checked) SystemSounds.Exclamation.Play();
                    tray.ShowBalloonTip(8000, "¡Corte detectado!", e.Nombre + " (" + e.Ip + ") dejó de responder.", ToolTipIcon.Warning);
                }
            }
            else
            {
                actual.Caidos.Add(e);
                if (e.PrimerFallo < actual.Inicio) actual.Inicio = e.PrimerFallo;
            }
        }

        // Fila "SIN RESPUESTA": un equipo que puede estar apagado dejó de responder un rato corto y volvió,
        // o un equipo fuera del camino volvió. No es un corte de internet (los demás respondían), así que no va a Telegram.
        void RegistrarAusencia(Equipo e, DateTime fin, double segundos)
        {
            string diag = e.Nombre + " dejó de responder " + Dur(segundos) + " y volvió solo. Los demás equipos respondían, " +
                          "así que no se cortó el internet. Si se repite seguido, revisa su cable o el equipo.";
            var campos = new[] { Tipos.SinRespuesta, e.PrimerFallo.ToString("yyyy-MM-dd HH:mm:ss"), fin.ToString("HH:mm:ss"),
                                 Math.Round(segundos).ToString(CultureInfo.InvariantCulture), Etiqueta(e) + " (" + Dur(segundos) + ")", diag };
            if (EnMantenimiento) campos = Tipos.ComoMantenimiento(campos);
            GuardarIncidente(campos);
            AgregarFila(campos);
            if (!cerrando && !EnMantenimiento)
                tray.ShowBalloonTip(8000, e.Nombre + " volvió tras " + Dur(segundos) + " sin responder", diag, ToolTipIcon.Info);
        }

        // ---------- Red inestable ----------
        // Pings perdidos sueltos (1 o 2 seguidos) no son un corte, pero si se juntan muchos en poco tiempo
        // los jugadores lo notan como lag. Se anota como una sola fila "INESTABLE" con cuánto duró y dónde empezó.

        void RevisarInestabilidad(DateTime ahora)
        {
            DateTime haceUnMinuto = ahora.AddSeconds(-60);
            foreach (var e in equipos)
                while (e.Recientes.Count > 0 && e.Recientes.Peek().Hora < haceUnMinuto)
                    if (e.Recientes.Dequeue().Perdido) e.PerdidosRecientes--;

            if (equipos.Any(x => !x.Apagado && x.PerdidosRecientes >= PERDIDAS_INESTABLE))
            {
                if (inestable == null)
                {
                    // Empieza en el primer ping perdido del último minuto; lo de ese minuto también cuenta.
                    inestable = new Inestabilidad();
                    var minuto = equipos.SelectMany(x => x.Recientes.Select(p => new { E = x, P = p })).OrderBy(x => x.P.Hora).ToList();
                    inestable.Inicio = minuto.First(x => x.P.Perdido).P.Hora;
                    foreach (var m in minuto.Where(x => x.P.Hora >= inestable.Inicio)) inestable.Sumar(m.E, m.P.Hora, m.P.Perdido);
                }
                inestable.UltimaVez = ahora;
            }
            else if (inestable != null && (ahora - inestable.UltimaVez).TotalSeconds >= SEGUNDOS_ESTABLE)
                CerrarInestabilidad();
        }

        void CerrarInestabilidad()
        {
            var ep = inestable;
            inestable = null;
            double dur = (ep.Fin - ep.Inicio).TotalSeconds;

            // Se nombran los equipos que perdieron al menos el 2 % (uno que perdió 1 ping no cuenta).
            var conPerdidas = equipos.Where(x => ep.Perdidos.ContainsKey(x)).OrderBy(x => x.Indice).ToList();
            var afectados = conPerdidas.Where(x => ep.Porcentaje(x) >= 2).ToList();
            if (afectados.Count == 0) afectados = conPerdidas;
            string que = string.Join(", ", afectados.Select(x => Etiqueta(x) + " (" + ep.Porcentaje(x).ToString("0.#") + " % perdido)"));
            string nombres = string.Join(", ", afectados.Select(x => x.Nombre));

            var activos = equipos.Where(x => !x.Apagado).OrderBy(x => x.Indice).ToList();
            // Dónde empezó: el primero afectado que está en el camino (los de fuera del camino no cuentan para eso).
            var primero = afectados.FirstOrDefault(x => !x.EsFuera) ?? afectados[0];
            var previo = activos.LastOrDefault(x => x.Indice < primero.Indice && !x.EsFuera && !(EsInternet(primero) && EsInternet(x)));
            // Si algún destino en internet respondía bien, el internet no se vio afectado.
            var internetBien = activos.FirstOrDefault(x => EsInternet(x) && !afectados.Contains(x));
            bool uno = afectados.Count == 1;
            string diag;
            if (internetBien != null)
                diag = (uno ? "Solo " + nombres + " perdió" : nombres + " perdieron") + " pings sueltos, pero " + internetBien.Nombre +
                       " respondía bien: el internet no se vio afectado. Lo más probable es que " +
                       (uno ? "estuviera ocupado y no contestara" : "estuvieran ocupados y no contestaran") + " algunos pings.";
            else if (previo == null)
                diag = "Se perdieron pings sueltos desde el primer equipo, " + primero.Nombre + ": puede estar fallando esta PC " +
                       "(su cable o tarjeta de red), el switch o " + primero.Nombre + ". No llegó a cortarse, pero se nota como lag " +
                       "o tirones en los juegos. Si se repite seguido, revísalo.";
            else
                diag = previo.Nombre + " respondía bien, pero desde " + primero.Nombre + " en adelante se perdían pings sueltos: " +
                       "la conexión entre " + previo.Nombre + " y " + primero.Nombre + " estuvo inestable (el cable, un puerto o " +
                       primero.Nombre + " muy ocupado). No llegó a cortarse, pero se nota como lag o tirones en los juegos. " +
                       "Si se repite seguido, revísalo.";

            var campos = new[] { Tipos.Inestable, ep.Inicio.ToString("yyyy-MM-dd HH:mm:ss"), ep.Fin.ToString("HH:mm:ss"),
                                 Math.Round(dur).ToString(CultureInfo.InvariantCulture), que, diag };
            if (ep.EnMantenimiento) campos = Tipos.ComoMantenimiento(campos);
            GuardarIncidente(campos);
            AgregarFila(campos);
            if (!cerrando && !ep.EnMantenimiento) tray.ShowBalloonTip(8000, "Red inestable durante " + Dur(dur), diag, ToolTipIcon.Info);
        }

        static void Sumar(Incidente inc, Equipo e, double seg)
        {
            double previo;
            inc.Duraciones.TryGetValue(e, out previo);
            inc.Duraciones[e] = previo + seg;
        }

        // ---------- La cadena y los destinos en internet ----------
        // Los equipos van en orden: para llegar a uno, el tráfico pasa por todos los anteriores. Los destinos
        // en internet (Google, Cloudflare...) son distintos: están al final y son caminos paralelos, no uno detrás de otro.

        bool EsInternet(Equipo e)
        {
            // Si no se marcó ninguno (listas viejas), el último de la lista hace de internet.
            return hayDestinosInternet ? e.EsInternet : e.Indice == equipos.Count - 1;
        }

        // Equipos que, si responden, prueban que el internet pasaba aunque "e" no conteste:
        // los que están después en la cadena; para un destino en internet, los otros destinos.
        // Los de fuera del camino no prueban nada: el internet no pasa por ellos.
        IEnumerable<Equipo> Despues(Equipo e)
        {
            return EsInternet(e) ? equipos.Where(x => x != e && EsInternet(x)) : equipos.Where(x => x.Indice > e.Indice && !x.EsFuera);
        }

        // Si un corte empieza en este equipo, ¿es del local? (sus equipos de red o PCs; no los del proveedor ni internet)
        bool EsDelLocal(Equipo e)
        {
            return (e.Rol == Rol.Red || e.Rol == Rol.Pc) && !EsInternet(e);
        }

        // Un equipo del local, antes del primero caído, que venía perdiendo pings sueltos en el último minuto.
        // Si la red del local estaba inestable, un "corte del proveedor" corto suele ser eso mismo: cuando el
        // MikroTik pierde uno de cada cuatro pings, a veces justo se pierden 3 seguidos de Google mientras él alcanza a contestar.
        Equipo LocalInestable(Equipo primero)
        {
            return equipos.Where(x => x.Indice < primero.Indice && EsDelLocal(x) && x.PerdidosRecientes >= PERDIDAS_LOCAL_SOSPECHOSAS)
                          .OrderBy(x => x.Indice).FirstOrDefault();
        }

        bool EsContradiccion(Incidente inc)
        {
            var primero = inc.Caidos.OrderBy(x => x.Indice).First();
            return Despues(primero).Any(x => !inc.Caidos.Contains(x) && !x.Apagado);
        }

        string Diagnosticar(Incidente inc)
        {
            var caidos = inc.Caidos.OrderBy(x => x.Indice).ToList();
            var primero = caidos[0];
            var siguieron = Despues(primero).Where(x => !inc.Caidos.Contains(x) && !x.Apagado).ToList();
            // Contradicción: un equipo no respondió pero otro que está DESPUÉS en la cadena (u otro destino
            // en internet) sí. El tráfico pasó, así que no hubo corte; solo dejó de contestar pings un momento.
            if (siguieron.Count > 0)
            {
                string fallaron = string.Join(", ", caidos.Select(x => x.Nombre));
                return "No fue un corte de internet: " + fallaron + " no contestó los pings, pero " +
                       string.Join(", ", siguieron.Select(x => x.Nombre)) +
                       (EsInternet(primero) ? " (otro destino en internet)" : " (que está después en la cadena)") + " sí respondía. " +
                       "Lo más probable es que " + fallaron + " estuviera ocupado y no contestara, aunque siguió pasando el internet.";
            }
            return primero.Diagnostico;
        }

        void CerrarIncidente(DateTime fin, string extra, bool avisar = true)
        {
            var inc = actual;
            actual = null;
            ultimoFin = fin;
            foreach (var e in equipos.Where(x => x.Caido && !x.Apagado))
                Sumar(inc, e, (fin - e.PrimerFallo).TotalSeconds);

            double dur = (fin - inc.Inicio).TotalSeconds;
            string caidos = string.Join(", ", inc.Caidos.OrderBy(x => x.Indice).Select(x =>
            {
                double d;
                inc.Duraciones.TryGetValue(x, out d);
                return Etiqueta(x) + " (" + Dur(d) + ")";
            }));
            string diag = Diagnosticar(inc) + extra;
            // Si un equipo de más adelante siguió respondiendo, el internet no se cortó: se guarda aparte.
            // Si se cortó, de quién es depende de dónde empezó: en un equipo del local o en el proveedor/internet.
            bool contradiccion = EsContradiccion(inc);
            var primeroCaido = inc.Caidos.OrderBy(x => x.Indice).First();
            bool local = EsDelLocal(primeroCaido);
            var inestableLocal = contradiccion || local ? null : LocalInestable(primeroCaido);
            if (inestableLocal != null)
            {
                local = true;
                diag += " Pasó mientras la red del local estaba inestable (" + inestableLocal.Nombre + " venía perdiendo pings): " +
                        "lo más probable es que la causa sea esa y no el proveedor.";
            }
            string tipo = contradiccion ? Tipos.NoFueCorte : local ? Tipos.CorteLocal : Tipos.Corte;
            var campos = new[] { tipo, inc.Inicio.ToString("yyyy-MM-dd HH:mm:ss"),
                                 fin.ToString("HH:mm:ss"), Math.Round(dur).ToString(CultureInfo.InvariantCulture), caidos, diag };
            if (inc.EnMantenimiento) campos = Tipos.ComoMantenimiento(campos);
            GuardarIncidente(campos);
            AgregarFila(campos);

            if (cerrando) return;
            tray.Icon = Icon = iconoOk;
            if (!avisar) return;
            if (contradiccion || inc.EnMantenimiento)
            {
                tray.ShowBalloonTip(10000, (inc.EnMantenimiento ? "Durante el mantenimiento: " + Tipos.Titulo(tipo).ToLower() : "No fue un corte de internet") +
                                           " (" + Dur(dur) + ")", diag, ToolTipIcon.Info);
                return;
            }
            if (chkSonido.Checked) SystemSounds.Asterisk.Play();
            tray.ShowBalloonTip(10000, "Volvió la conexión (el corte duró " + Dur(dur) + ")", diag, ToolTipIcon.Info);
            Telegram.Avisar("🟢 VOLVIÓ LA CONEXIÓN\nCorte de " + Dur(dur) + " (" + inc.Inicio.ToString("HH:mm:ss") + " → " +
                            fin.ToString("HH:mm:ss") + ")" + (local ? " · falla en el LOCAL" : "") + "\nSe cayó: " + caidos + "\n" + diag, true);
            proximaConsultaIp = Reloj.Segundos + 5; // los cambios de IP suelen venir justo después de un corte
        }

        void ActualizarEstado()
        {
            ActualizarItemMantenimiento();
            if (actual != null)
            {
                string sinResp = string.Join(", ", equipos.Where(x => x.Caido && !x.Apagado && !x.EsFuera).Select(x => x.Nombre));
                if (actual.EnMantenimiento)
                    banner.Poner(Banner.Tipo.Mantenimiento,
                        "Mantenimiento hasta las " + mantHasta.ToString("HH:mm") + "  ·  sin respuesta: " + sinResp,
                        "Lo que pase ahora no cuenta para el proveedor ni se avisa por Telegram.");
                else
                    banner.Poner(Banner.Tipo.Corte,
                        "Corte en curso desde las " + actual.Inicio.ToString("HH:mm:ss") +
                        " (" + Dur((Reloj.Ahora - actual.Inicio).TotalSeconds) + ")  ·  sin respuesta: " + sinResp,
                        "Probable: " + Diagnosticar(actual));
                Text = "⚠ CORTE - " + Programa.NOMBRE;
            }
            else if (dnsCaidoDesde != DateTime.MinValue && !EnMantenimiento)
            {
                banner.Poner(Banner.Tipo.Corte, "Las páginas no cargan: el DNS (" + servidoresDns + ") no responde desde las " +
                                                dnsCaidoDesde.ToString("HH:mm:ss"),
                    "El ping funciona, pero sin DNS no se abren las páginas ni los juegos que buscan su servidor por nombre.");
                Text = "⚠ DNS - " + Programa.NOMBRE;
            }
            else if (horaMal && !EnMantenimiento)
            {
                banner.Poner(Banner.Tipo.Corte, "La hora de esta PC está mal: " + Programa.NOMBRE + " anota todo con la hora de internet",
                    "Arréglala en Configuración → Hora e idioma → Establecer la hora automáticamente. Todo lo demás se sigue vigilando.");
                Text = "⚠ HORA - " + Programa.NOMBRE;
            }
            else if (EnMantenimiento)
            {
                banner.Poner(Banner.Tipo.Mantenimiento, "Modo mantenimiento hasta las " + mantHasta.ToString("HH:mm") + " (se apaga solo)",
                    "Lo que pase ahora se anota como MANTENIMIENTO: no cuenta para el proveedor ni se avisa por Telegram.");
                Text = Programa.NOMBRE + " (mantenimiento)";
            }
            else if (equipos.All(x => x.Enviados > 0))
            {
                Func<Equipo, bool> reciente = x => x.SeApaga && (Reloj.Ahora - x.PrimerFallo).TotalMinutes < MINUTOS_AUSENCIA;
                var sinRespuesta = equipos.Where(x => (x.Apagado && reciente(x)) || (x.EsFuera && x.Caido)).Select(x => x.Nombre).ToList();
                var apagados = equipos.Where(x => x.Apagado && !reciente(x)).Select(x => x.Nombre).ToList();
                banner.Poner(Banner.Tipo.Ok,
                    (lento != null ? "Funcionando, pero internet está lento desde las " + lento.Inicio.ToString("HH:mm:ss")
                     : inestable != null ? "Funcionando, pero la red está inestable desde las " + inestable.Inicio.ToString("HH:mm:ss")
                     : "Todo funcionando") +
                    (sinRespuesta.Count == 0 ? "" : "   ·   sin respuesta: " + string.Join(", ", sinRespuesta)) +
                    (apagados.Count == 0 ? "" : "   ·   apagado: " + string.Join(", ", apagados)) +
                    (ultimoFin == DateTime.MinValue ? "" :
                        "   ·   último corte terminó a las " + ultimoFin.ToString("HH:mm:ss") +
                        (ultimoFin.Date != Reloj.Ahora.Date ? " del " + ultimoFin.ToString("dd/MM") : "")),
                    ipsPublicas == null ? "" : TextoIpPublica() +
                        (ultimoCambioIp == DateTime.MinValue ? "" : "   (cambió a las " + ultimoCambioIp.ToString("HH:mm:ss") + ")"));
                Text = Programa.NOMBRE;
            }
        }

        // Nombre e IP juntos ("Proveedor (entrada) · 10.0.0.1"): si después se cambia la IP de un equipo,
        // el historial sigue diciendo cuál era en ese momento.
        static string Etiqueta(Equipo e) { return e.Nombre + " · " + e.Ip; }

        static string Dur(double segundos)
        {
            int t = (int)Math.Round(segundos);
            if (t < 60) return t + " s";
            if (t < 3600) return (t / 60) + " min " + (t % 60) + " s";
            return (t / 3600) + " h " + (t % 3600 / 60) + " min";
        }
    }
}
