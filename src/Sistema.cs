// Conexión a internet compartida (HTTPS), tracert, el reloj de Yedsa, el sonido de captura y control de los ping.exe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace Yedsa
{
    static class Red
    {
        public static readonly HttpClient Http = CrearHttp();

        static HttpClient CrearHttp()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var h = new HttpClient();
            h.Timeout = TimeSpan.FromSeconds(10);
            return h;
        }

        // IPs de los saltos de un tracert, en orden ("*" si ese salto no respondió). null si tracert no funcionó.
        public static List<string> Tracert(string destino, int saltos)
        {
            try
            {
                var psi = new ProcessStartInfo("tracert", "-d -h " + saltos + " -w 1000 " + destino);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;
                using (var p = Process.Start(psi))
                {
                    string salida = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    var lista = new List<string>();
                    foreach (System.Text.RegularExpressions.Match m in
                             System.Text.RegularExpressions.Regex.Matches(salida, @"^\s*\d+\s+.*$", System.Text.RegularExpressions.RegexOptions.Multiline))
                    {
                        var ip = System.Text.RegularExpressions.Regex.Match(m.Value, @"(\d{1,3}\.){3}\d{1,3}\s*$");
                        lista.Add(ip.Success ? ip.Value.Trim() : "*");
                    }
                    return lista.Count > 0 ? lista.Take(saltos).ToList() : null;
                }
            }
            catch { return null; }
        }

        // ¿La IP es de una red privada? (las de las casas y locales, y las de adentro de los proveedores)
        public static bool EsPrivada(string ip)
        {
            IPAddress a;
            if (!IPAddress.TryParse(ip, out a)) return false;
            var b = a.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) ||
                                     (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
        }

        // ¿La IP es de las que usan los routers de casas y locales? (192.168.x.x y 172.16-31.x.x)
        public static bool EsDeLocal(string ip)
        {
            IPAddress a;
            if (!IPAddress.TryParse(ip, out a)) return false;
            var b = a.GetAddressBytes();
            return b.Length == 4 && ((b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31));
        }

        public static bool Responde(string ip, int intentos)
        {
            for (int i = 0; i < intentos; i++)
                try { using (var p = new Ping()) if (p.Send(ip, 1000).Status == IPStatus.Success) return true; }
                catch { return false; }
            return false;
        }
    }

    // La hora que usa Yedsa para todo lo que anota. Es la de la PC, salvo que:
    //   - la PC tenga la hora mal: se le suma la diferencia con la hora de internet (Corregir);
    //   - alguien mueva el reloj de Windows con Yedsa abierto: el salto se compensa, para que las horas no salten.
    static class Reloj
    {
        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();   // milisegundos desde que se encendió la PC (sigue contando aunque se suspenda)

        static readonly object candado = new object();
        static TimeSpan ajuste = TimeSpan.Zero;
        static DateTime ultimoPc = DateTime.Now;
        static ulong ultimoTick = GetTickCount64();

        public static DateTime Ahora { get { lock (candado) { SeguirCambios(); return DateTime.Now + ajuste; } } }

        // Lo que se le suma a la hora de la PC (cero si la PC tiene bien la hora).
        public static TimeSpan Ajuste { get { lock (candado) return ajuste; } }

        public static void Corregir(TimeSpan nuevo) { lock (candado) { SeguirCambios(); ajuste = nuevo; } }

        // Segundos que no dependen del reloj de Windows: para medir cada cuánto se hace algo.
        public static double Segundos { get { return GetTickCount64() / 1000.0; } }

        // Si la hora de la PC saltó distinto que el contador de Windows, alguien movió el reloj: se compensa.
        static void SeguirCambios()
        {
            DateTime pc = DateTime.Now;
            ulong tick = GetTickCount64();
            double salto = (pc - ultimoPc).TotalSeconds - (tick - ultimoTick) / 1000.0;
            if (Math.Abs(salto) > 5) ajuste -= TimeSpan.FromSeconds(salto);
            ultimoPc = pc;
            ultimoTick = tick;
        }
    }

    // El "clic" de cámara al capturar. Se arma en memoria (un .wav cortito), así no hace falta ningún archivo de sonido.
    static class Sonidos
    {
        static SoundPlayer clic;

        public static void Clic()
        {
            try
            {
                if (clic == null) clic = new SoundPlayer(new MemoryStream(CrearClic()));
                clic.Play();   // no espera a que termine
            }
            catch (Exception ex) { Programa.AnotarError("Sonido de captura", ex); }
        }

        // Dos chasquidos cortos (el obturador al abrir y al cerrar), suaves: ruido que se apaga en pocos milisegundos.
        static byte[] CrearClic()
        {
            const int HZ = 22050;
            int total = HZ * 130 / 1000;
            var muestras = new short[total];
            var azar = new Random(7);
            foreach (int inicio in new[] { 0, HZ * 65 / 1000 })
                for (int i = 0; i < HZ * 25 / 1000 && inicio + i < total; i++)
                {
                    double caida = Math.Exp(-i / (HZ * 0.004));             // se apaga en ~4 ms
                    double fuerza = inicio == 0 ? 0.22 : 0.16;              // el segundo, un poco más suave
                    muestras[inicio + i] = (short)((azar.NextDouble() * 2 - 1) * caida * fuerza * short.MaxValue);
                }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int datos = total * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + datos); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(HZ); w.Write(HZ * 2); w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(datos);
                foreach (var m in muestras) w.Write(m);
                return ms.ToArray();
            }
        }
    }

    // Job de Windows: si Yedsa se cierra (aunque sea a la fuerza), mata los ping.exe que abrió.
    static class Job
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr atributos, string nombre);
        [DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int clase, IntPtr info, uint largo);
        [DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proceso);

        [StructLayout(LayoutKind.Sequential)]
        struct Basico
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Extendido
        {
            public Basico Basic;
            public ulong Io1, Io2, Io3, Io4, Io5, Io6;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        static IntPtr handle;

        public static void Asignar(Process p)
        {
            try
            {
                if (handle == IntPtr.Zero)
                {
                    handle = CreateJobObject(IntPtr.Zero, null);
                    var info = new Extendido();
                    info.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                    int largo = Marshal.SizeOf(typeof(Extendido));
                    IntPtr ptr = Marshal.AllocHGlobal(largo);
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(handle, 9, ptr, (uint)largo);
                    Marshal.FreeHGlobal(ptr);
                }
                AssignProcessToJobObject(handle, p.Handle);
            }
            catch (Exception ex) { Programa.AnotarError("No se pudo asociar ping.exe al Job (si Yedsa se cierra a la fuerza, quedaría abierto)", ex); }
        }
    }
}
