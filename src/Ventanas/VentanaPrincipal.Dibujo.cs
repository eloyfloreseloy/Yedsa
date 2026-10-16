// Dibujo de la tarjeta de equipos: estado, pérdida y línea de tiempo de 10 minutos.
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
        // Fila de títulos de las columnas: queda fija arriba aunque la lista de equipos se desplace.
        void PintarCabecera(object sender, PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.Clear(Tema.Superficie);
            int tlX = 616;
            var izq = TextFormatFlags.Left | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, "#", fCabecera, new Rectangle(4, 6, 24, 16), Tema.TextoTenue, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "EQUIPO", fCabecera, new Point(62, 6), Tema.TextoTenue, izq);
            TextRenderer.DrawText(g, "ESTADO", fCabecera, new Point(236, 6), Tema.TextoTenue, izq);
            TextRenderer.DrawText(g, "ÚLTIMO PING", fCabecera, new Point(366, 6), Tema.TextoTenue, izq);
            TextRenderer.DrawText(g, "PÉRDIDA " + (HIST / 60) + " MIN", fCabecera, new Point(462, 6), Tema.TextoTenue, izq);
            TextRenderer.DrawText(g, "CORTES", fCabecera, new Point(562, 6), Tema.TextoTenue, izq);
            TextRenderer.DrawText(g, "ÚLTIMOS " + (HIST / 60) + " MIN   ·   cada rayita = 1 ping   ·   verde ok, amarillo lento, rojo sin respuesta",
                                  fCabecera, new Point(tlX, 6), Tema.TextoTenue, izq);
        }

        // Una fila por equipo. Si hay más de los que entran, la lista se desplaza con su barra.
        void PintarPanel(object sender, PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.Clear(Tema.Superficie);
            int w = panel.ClientSize.Width;
            int tlX = 616, tlW = Math.Max(100, w - tlX - 8);
            var izq = TextFormatFlags.Left | TextFormatFlags.NoPrefix;

            using (var linea = new Pen(Tema.Borde))
            using (var bVerde = new SolidBrush(VERDE))
            using (var bAmarillo = new SolidBrush(AMARILLO))
            using (var bRojo = new SolidBrush(ROJO))
            {
                for (int i = 0; i < equipos.Count; i++)
                {
                    var e = equipos[i];
                    int y = i * ALTO;
                    if (i > 0) g.DrawLine(linea, 8, y, w - 8, y);

                    // Un equipo que "puede estar apagado" se muestra en amarillo los primeros minutos (puede ser una falla)
                    // y en gris después (se asume que lo apagaron).
                    bool ausenteReciente = e.Apagado && e.SeApaga && (Reloj.Ahora - e.PrimerFallo).TotalMinutes < MINUTOS_AUSENCIA;
                    bool gris = e.Enviados == 0 || (e.Apagado && !ausenteReciente);
                    Color c = gris ? Tema.TextoTenue : ausenteReciente ? AMARILLO : e.Caido ? ROJO : e.FallosSeguidos > 0 ? AMARILLO : VERDE;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // Número de orden (el mismo de la ventana Equipos): sirve para ubicarse al desplazar la lista.
                    TextRenderer.DrawText(g, (i + 1).ToString(), fNormal, new Rectangle(4, y, 24, ALTO), Tema.TextoTenue,
                                          TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, 38, y + ALTO / 2 - 6, 12, 12);
                    g.SmoothingMode = SmoothingMode.None;

                    TextRenderer.DrawText(g, e.Nombre, fNombre, new Rectangle(60, y + 9, 170, 22), Tema.Texto, izq | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, e.Ip, fNormal, new Point(62, y + 31), Tema.TextoTenue, izq);

                    string estado = e.Enviados == 0 ? "Esperando..." :
                        e.Apagado && !ausenteReciente ? "APAGADO\n" + Dur((Reloj.Ahora - e.PrimerFallo).TotalSeconds) :
                        e.Caido ? "SIN RESPUESTA\n" + Dur((Reloj.Ahora - e.PrimerFallo).TotalSeconds) :
                        e.FallosSeguidos > 0 ? "Perdiendo pings" : "OK";
                    Color ce = gris ? Tema.TextoTenue : ausenteReciente ? Tema.Amarillo : e.Caido ? Tema.RojoTexto : e.FallosSeguidos > 0 ? Tema.Amarillo : Tema.VerdeTexto;
                    TextRenderer.DrawText(g, estado, Tema.Negrita, new Rectangle(236, y, 125, ALTO), ce,
                                          TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    var centro = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
                    TextRenderer.DrawText(g, e.UltimoMs >= 0 ? e.UltimoMs + " ms" : "—", fNormal, new Rectangle(366, y, 90, ALTO), Tema.Texto, centro);
                    // Pérdida de los mismos 10 minutos de la línea de tiempo: así se nota si la red está mal ahora.
                    int perdidos = e.Historial.Count(m => m == Muestra.Perdido);
                    double pct = e.Historial.Count == 0 ? 0 : 100.0 * perdidos / e.Historial.Count;
                    // Recién abierto hay pocos pings y uno solo perdido ya pesa mucho: se colorea desde el primer minuto.
                    Color cp = e.Historial.Count < 60 ? Tema.Texto : pct >= 10 ? Tema.RojoTexto : pct >= 2 ? Tema.Amarillo : Tema.Texto;
                    TextRenderer.DrawText(g, pct.ToString("0.0") + " %", fNormal, new Rectangle(462, y, 96, ALTO), cp, centro);
                    TextRenderer.DrawText(g, e.Cortes.ToString(), fNormal, new Rectangle(562, y, 50, ALTO), Tema.Texto, centro);

                    // Línea de tiempo con esquinas redondeadas.
                    var barra = new RectangleF(tlX, y + 14, tlW, ALTO - 28);
                    Tema.Rellenar(g, Tema.Superficie2, barra, 6);
                    using (var forma = Tema.Redondeado(barra, 6))
                    {
                        g.SetClip(forma);
                        int n = e.Historial.Count;
                        for (int k = 0; k < n; k++)
                        {
                            int slot = HIST - n + k;
                            int xa = tlX + (int)((long)slot * tlW / HIST);
                            int xb = tlX + (int)((long)(slot + 1) * tlW / HIST);
                            var m = e.Historial[k];
                            g.FillRectangle(m == Muestra.Ok ? bVerde : m == Muestra.Lento ? bAmarillo : bRojo,
                                            xa, (int)barra.Y, Math.Max(1, xb - xa), (int)barra.Height + 1);
                        }
                        g.ResetClip();
                    }
                }
            }
        }
    }
}
