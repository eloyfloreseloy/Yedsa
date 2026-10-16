// Detección de cambios en la IP pública del local.
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
        // Hay locales que salen a internet por una sola IP pública y otros por varias (un bloque de IPs del
        // proveedor o varias líneas balanceadas). En esos, cada servidor de destino sale por una IP distinta
        // y los servicios de "¿cuál es mi IP?" tienen varios servidores, así que la IP vista salta sola entre
        // las del bloque. Por eso: con una sola IP se avisa si cambia; con varias, solo si aparece una de
        // otro rango (el proveedor cambió el bloque).
        static readonly string[] SERVICIOS_IP = { "https://api.ipify.org", "https://icanhazip.com", "https://checkip.amazonaws.com" };
        List<string> ipsPublicas;     // vistas en la última consulta
        bool ipConsultadaEnSesion, consultandoIp;
        double proximaConsultaIp, proximoReintentoTelegram;   // en Reloj.Segundos: no dependen del reloj de Windows
        DateTime ultimoCambioIp = DateTime.MinValue;
        string ultimoCambioIpTexto;

        async void ConsultarIpPublica()
        {
            if (consultandoIp) return;
            consultandoIp = true;
            try
            {
                var tareas = SERVICIOS_IP.Select(PedirIp).ToArray();
                await Task.WhenAll(tareas);
                var vistas = tareas.Select(t => t.Result.Ip).Where(ip => ip != null).Distinct().ToList();
                if (vistas.Count > 0 && !cerrando) RegistrarIps(vistas);
                // De paso, cada respuesta trae la hora de ese servidor: sirve para saber si la PC tiene la hora mal.
                var diferencias = tareas.Where(t => t.Result.Diferencia.HasValue).Select(t => t.Result.Diferencia.Value).ToList();
                if (diferencias.Count > 0 && !cerrando) RevisarHora(diferencias.OrderBy(x => x).ElementAt(diferencias.Count / 2));
            }
            finally { consultandoIp = false; }
        }

        class RespuestaIp
        {
            public string Ip;
            public double? Diferencia;   // segundos: hora del servidor menos hora de la PC (positivo = la PC está atrasada)
        }

        static async Task<RespuestaIp> PedirIp(string url)
        {
            var res = new RespuestaIp();
            try
            {
                DateTime antes = DateTime.UtcNow;
                using (var r = await Red.Http.GetAsync(url))
                {
                    DateTime despues = DateTime.UtcNow;
                    string texto = (await r.Content.ReadAsStringAsync()).Trim();
                    IPAddress a;
                    if (r.IsSuccessStatusCode && IPAddress.TryParse(texto, out a)) res.Ip = texto;
                    // La hora del servidor se compara con la de la PC a mitad del viaje de ida y vuelta.
                    if (r.Headers.Date.HasValue)
                        res.Diferencia = (r.Headers.Date.Value.UtcDateTime - antes.AddTicks((despues - antes).Ticks / 2)).TotalSeconds;
                }
            }
            catch { } // sin internet o servicio caído: se reintenta el próximo minuto
            return res;
        }

        string TextoIpPublica()
        {
            var conocidas = Ajustes.Leer("ips_conocidas").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (conocidas.Length <= 1 && ipsPublicas.Count == 1) return "IP pública: " + ipsPublicas[0];
            return "IPs públicas: " + string.Join(", ", conocidas.Select(Rango).Distinct()) +
                   "  (el local sale por varias: " + conocidas.Length + " vistas hasta ahora)";
        }

        static string Rango(string ip)
        {
            int i = ip.LastIndexOf('.');
            return i > 0 ? ip.Substring(0, i) + ".x" : ip;
        }

        void RegistrarIps(List<string> vistas)
        {
            bool primeraVez = !ipConsultadaEnSesion;
            ipConsultadaEnSesion = true;
            ipsPublicas = vistas.OrderBy(x => x.Length).ThenBy(x => x).ToList();

            var conocidas = Ajustes.Leer("ips_conocidas").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            bool varias = conocidas.Count > 1 || vistas.Count > 1;
            string antes = null, ahoraTxt = null;
            if (conocidas.Count > 0)
            {
                if (!varias && vistas[0] != conocidas[0])
                {
                    antes = conocidas[0];
                    ahoraTxt = vistas[0];
                }
                else if (varias)
                {
                    var rangos = conocidas.Select(Rango).Distinct().ToList();
                    var fuera = vistas.Where(ip => !rangos.Contains(Rango(ip))).ToList();
                    if (fuera.Count > 0)
                    {
                        antes = string.Join(", ", rangos);
                        ahoraTxt = string.Join(", ", vistas.Select(Rango).Distinct());
                        conocidas.Clear(); // el bloque es otro: se empieza a conocer de nuevo
                    }
                }
            }
            // Se recuerdan las IPs vistas (las últimas 20) para reconocer el bloque.
            conocidas = vistas.Concat(conocidas).Distinct().Take(20).ToList();
            Ajustes.Poner("ips_conocidas", string.Join(",", conocidas));
            if (antes == null) return;

            DateTime ahora = Reloj.Ahora;
            string detalle = antes + " → " + ahoraTxt;
            string diag;
            if (primeraVez)
                diag = "La IP pública del local cambió mientras " + Programa.NOMBRE + " estaba cerrado (no se sabe la hora exacta).";
            else
            {
                ultimoCambioIp = ahora;
                ultimoCambioIpTexto = detalle;
                diag = "El proveedor cambió la IP pública del local. Esto corta las partidas online y las conexiones abiertas, aunque los ping sigan bien.";
                if (actual != null || (ahora - ultimoFin).TotalMinutes < 3) diag += " Ocurrió junto con un corte de red.";
            }
            var campos = new[] { Tipos.CambioIp, ahora.ToString("yyyy-MM-dd HH:mm:ss"), "", "", detalle, diag };
            // Durante un mantenimiento lo más probable es que el cambio lo haya causado la configuración de la red.
            bool mantenimiento = !primeraVez && EnMantenimiento;
            if (mantenimiento) campos = Tipos.ComoMantenimiento(campos);
            GuardarIncidente(campos);
            AgregarFila(campos);
            if (primeraVez) return;
            if (mantenimiento)
            {
                tray.ShowBalloonTip(8000, "Cambió la IP pública (durante el mantenimiento)", detalle, ToolTipIcon.Info);
                return;
            }

            if (chkSonido.Checked) SystemSounds.Exclamation.Play();
            tray.ShowBalloonTip(10000, "Cambió la IP pública", detalle, ToolTipIcon.Warning);
            Telegram.Avisar("🟣 CAMBIÓ LA IP PÚBLICA (" + ahora.ToString("HH:mm:ss") + ")\n" + detalle + "\n" + diag, true);
        }
    }
}
