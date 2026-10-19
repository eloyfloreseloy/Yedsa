// Cosas que no son un corte pero igual dejan sin servicio a los clientes:
//   - el DNS no responde: el ping funciona, pero las páginas y los juegos que buscan su servidor por nombre no cargan;
//   - internet lento: todo responde, pero tarda tanto que los juegos se traban.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text;
using System.Windows.Forms;

namespace Yedsa
{
    partial class VentanaPrincipal
    {
        // ---------- DNS ----------
        // Cada 10 segundos se le pregunta a los DNS que usa esta PC por "www.google.com", directamente (sin la memoria
        // de Windows, que seguiría contestando aunque el DNS esté caído). Si ninguno contesta 3 veces seguidas
        // mientras el internet funciona, es una falla de DNS.

        const int SEGUNDOS_DNS = 10;
        const int FALLOS_DNS = 3;

        double proximoDns = Reloj.Segundos + 20;
        bool revisandoDns;
        int fallosDns;
        DateTime dnsCaidoDesde = DateTime.MinValue;   // MinValue = el DNS está bien
        bool dnsEnMantenimiento;
        string servidoresDns = "";

        // Los DNS (IPv4) configurados en las tarjetas de red conectadas.
        static List<IPAddress> ServidoresDns()
        {
            var lista = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;   // solo la tarjeta que sale a internet
                    lista.AddRange(props.DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork));
                }
            }
            catch { }   // si Windows no deja ver las tarjetas, no se revisa el DNS esta vez
            return lista.Distinct().Take(3).ToList();
        }

        // Pregunta por un nombre a un DNS (UDP, puerto 53). true si contestó, aunque sea "ese nombre no existe".
        static bool PreguntarDns(IPAddress servidor, string nombre)
        {
            try
            {
                var paquete = new List<byte>();
                var id = (ushort)new Random().Next(1, 65535);
                paquete.AddRange(new byte[] { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 });   // pregunta normal, 1 pregunta
                foreach (var parte in nombre.Split('.'))
                {
                    paquete.Add((byte)parte.Length);
                    paquete.AddRange(Encoding.ASCII.GetBytes(parte));
                }
                paquete.AddRange(new byte[] { 0, 0, 1, 0, 1 });   // fin del nombre, tipo A, clase IN
                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 2000;
                    udp.Send(paquete.ToArray(), paquete.Count, new IPEndPoint(servidor, 53));
                    var desde = new IPEndPoint(IPAddress.Any, 0);
                    var r = udp.Receive(ref desde);
                    int codigo = r.Length >= 12 ? r[3] & 0x0F : -1;
                    return r.Length >= 12 && r[0] == paquete[0] && r[1] == paquete[1] && (codigo == 0 || codigo == 3);
                }
            }
            catch { return false; }
        }

        bool HayInternet() { return equipos.Any(x => EsInternet(x) && !x.Apagado && x.Enviados > 0 && x.FallosSeguidos == 0); }

        void RevisarDns(DateTime ahora)
        {
            if (Reloj.Segundos < proximoDns || revisandoDns) return;
            proximoDns = Reloj.Segundos + SEGUNDOS_DNS;
            revisandoDns = true;
            Task.Factory.StartNew(() =>
            {
                var servidores = ServidoresDns();
                bool alguno = servidores.Count == 0 || servidores.Any(s => PreguntarDns(s, "www.google.com"));
                return new KeyValuePair<bool, string>(alguno, string.Join(", ", servidores));
            }).ContinueWith(t =>
            {
                revisandoDns = false;
                if (!cerrando) ResultadoDns(t.Result.Key, t.Result.Value);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ResultadoDns(bool responde, string servidores)
        {
            DateTime ahora = Reloj.Ahora;
            if (responde)
            {
                fallosDns = 0;
                if (dnsCaidoDesde != DateTime.MinValue) CerrarFallaDns(ahora);
                return;
            }
            // Sin internet, que el DNS no conteste es parte del corte, no una falla aparte.
            if (actual != null || !HayInternet()) { fallosDns = 0; return; }
            if (++fallosDns < FALLOS_DNS || dnsCaidoDesde != DateTime.MinValue) { if (EnMantenimiento) dnsEnMantenimiento = true; return; }
            dnsCaidoDesde = ahora.AddSeconds(-SEGUNDOS_DNS * (FALLOS_DNS - 1));
            dnsEnMantenimiento = EnMantenimiento;
            servidoresDns = servidores;
            if (!EnMantenimiento)
            {
                if (chkSonido.Checked) SystemSounds.Exclamation.Play();
                tray.ShowBalloonTip(8000, "Las páginas no cargan: el DNS no responde",
                    "El ping funciona, pero el DNS (" + servidores + ") no contesta.", ToolTipIcon.Warning);
            }
        }

        void CerrarFallaDns(DateTime fin)
        {
            DateTime inicio = dnsCaidoDesde;
            double seg = (fin - inicio).TotalSeconds;
            // ¿De quién es el DNS? Uno con IP de red privada es el del router del local; si no, uno de afuera.
            bool deRouter = servidoresDns.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).All(Red.EsPrivada);
            string diag = "El ping funcionaba, pero el DNS (" + servidoresDns + ") no contestaba: las páginas y los juegos que buscan su " +
                          "servidor por nombre no cargaban. " +
                          (deRouter ? "Ese DNS es el de tu router: revisa su DNS o a quién le reenvía las preguntas (normalmente, al proveedor)."
                                    : "Ese DNS es de afuera: puede ser una falla del proveedor al llegar a él, o del propio DNS.");
            var campos = new[] { Tipos.Dns, dnsCaidoDesde.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture), fin.ToString("HH:mm:ss"),
                                 Math.Round(seg).ToString(CultureInfo.InvariantCulture), "DNS " + servidoresDns + " (" + Dur(seg) + ")", diag };
            bool mant = dnsEnMantenimiento || EnMantenimiento;
            if (mant) campos = Tipos.ComoMantenimiento(campos);
            dnsCaidoDesde = DateTime.MinValue;
            GuardarIncidente(campos);
            AgregarFila(campos);
            CapturaAlTerminar("dns", Tipos.Dns, inicio, mant);
            if (cerrando || mant) return;
            tray.ShowBalloonTip(8000, "El DNS volvió (falló " + Dur(seg) + ")", diag, ToolTipIcon.Info);
            if (seg >= 60) Telegram.Avisar("🟠 FALLA DE DNS (" + Dur(seg) + ", desde las " + campos[1].Substring(11) + ")\n" + diag, true);
        }

        // ---------- Internet lento ----------
        // Si TODOS los destinos en internet tardan más de LENTO ms (mediana del último minuto), el internet está lento.
        // Para saber dónde empieza, se mira el primer equipo del camino que también tardaba.

        const int MS_EQUIPO_LENTO = 100;   // un equipo del camino que tarda esto ya está lento (lo normal es 0 a 5 ms)
        const int MUESTRAS_LENTO = 20;     // pings del último minuto que hacen falta para opinar

        class RatoLento
        {
            public DateTime Inicio, UltimaVez;
            public bool EnMantenimiento;
            public Dictionary<Equipo, long> Suma = new Dictionary<Equipo, long>();
            public Dictionary<Equipo, int> Cuenta = new Dictionary<Equipo, int>();
            public long Promedio(Equipo e) { int n; return Cuenta.TryGetValue(e, out n) && n > 0 ? Suma[e] / n : 0; }

            public void Sumar(Equipo e, long ms)
            {
                long s; int n;
                Suma.TryGetValue(e, out s); Suma[e] = s + ms;
                Cuenta.TryGetValue(e, out n); Cuenta[e] = n + 1;
            }
        }
        RatoLento lento;

        // Se llama con cada ping que respondió.
        void AnotarLatencia(Equipo e, DateTime cuando, long ms)
        {
            e.Latencias.Enqueue(new KeyValuePair<DateTime, long>(cuando, ms));
            if (lento != null) lento.Sumar(e, ms);
        }

        static long Mediana(IEnumerable<long> valores)
        {
            var v = valores.OrderBy(x => x).ToList();
            return v.Count == 0 ? 0 : v[v.Count / 2];
        }

        void RevisarLentitud(DateTime ahora)
        {
            DateTime haceUnMinuto = ahora.AddSeconds(-60);
            foreach (var e in equipos)
                while (e.Latencias.Count > 0 && e.Latencias.Peek().Key < haceUnMinuto) e.Latencias.Dequeue();

            var destinos = equipos.Where(x => EsInternet(x) && !x.Apagado).ToList();
            bool estaLento = actual == null && destinos.Count > 0 &&
                             destinos.All(x => x.Latencias.Count >= MUESTRAS_LENTO && Mediana(x.Latencias.Select(m => m.Value)) >= LENTO);
            if (estaLento)
            {
                if (lento == null)
                {
                    lento = new RatoLento();
                    // La mediana es del último minuto: empezó más o menos con el ping más viejo de esa cuenta.
                    lento.Inicio = destinos.Min(x => x.Latencias.Peek().Key);
                    foreach (var e in equipos)
                        foreach (var m in e.Latencias) lento.Sumar(e, m.Value);
                }
                lento.UltimaVez = ahora;
                if (EnMantenimiento) lento.EnMantenimiento = true;
            }
            else if (lento != null && (ahora - lento.UltimaVez).TotalSeconds >= SEGUNDOS_ESTABLE)
                CerrarLentitud();
        }

        void CerrarLentitud()
        {
            var r = lento;
            lento = null;
            double seg = (r.UltimaVez - r.Inicio).TotalSeconds;
            var destinos = equipos.Where(x => EsInternet(x) && r.Cuenta.ContainsKey(x)).ToList();
            // Dónde empieza: el primer equipo del camino (sin los de fuera del camino ni las PCs) que ya tardaba.
            var camino = equipos.Where(x => !x.EsFuera && !x.SeApaga && !EsInternet(x) && r.Cuenta.ContainsKey(x)).OrderBy(x => x.Indice).ToList();
            var primero = camino.FirstOrDefault(x => r.Promedio(x) >= MS_EQUIPO_LENTO);
            var previo = primero == null ? null : camino.LastOrDefault(x => x.Indice < primero.Indice);
            string internetMs = string.Join(", ", destinos.Select(x => Etiqueta(x) + " (" + r.Promedio(x) + " ms)"));
            string diag;
            if (primero != null && EsDelLocal(primero))
                diag = primero.Nombre + " ya respondía lento (" + r.Promedio(primero) + " ms en promedio; lo normal es 0 a 5 ms): la red del local " +
                       "estaba saturada (alguien descargando mucho, actualizaciones) o el router muy cargado. No es del proveedor.";
            else if (primero != null)
                diag = (previo != null ? previo.Nombre + " respondía rápido (" + r.Promedio(previo) + " ms), pero desde " : "Desde ") +
                       primero.Nombre + " (" + r.Promedio(primero) + " ms) todo iba lento: la red del PROVEEDOR estaba saturada o con problemas.";
            else
                diag = "Tus equipos y los del proveedor respondían rápido, pero internet iba lento: problema en la salida a internet del PROVEEDOR.";
            diag += " No se cortó, pero los juegos en línea se traban con esta demora.";
            var campos = new[] { Tipos.Lento, r.Inicio.ToString(FORMATO_FECHA, CultureInfo.InvariantCulture), r.UltimaVez.ToString("HH:mm:ss"),
                                 Math.Round(seg).ToString(CultureInfo.InvariantCulture), internetMs, diag };
            if (r.EnMantenimiento) campos = Tipos.ComoMantenimiento(campos);
            GuardarIncidente(campos);
            AgregarFila(campos);
            CapturaAlTerminar("lento", Tipos.Lento, r.Inicio, r.EnMantenimiento);
            if (!cerrando && !r.EnMantenimiento) tray.ShowBalloonTip(8000, "Internet lento durante " + Dur(seg), diag, ToolTipIcon.Info);
        }
    }
}
