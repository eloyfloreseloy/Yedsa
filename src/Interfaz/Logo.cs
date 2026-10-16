// Logo de Yedsa dibujado desde su trazo SVG (recursos\yedsa-logo.svg), para que se vea nítido en cualquier tamaño:
// barra de título, ícono junto al reloj e ícono del .exe.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Yedsa
{
    public static class Logo
    {
        // Trazo del SVG original sin el cuadrado negro de fondo: solo las formas blancas.
        // Usa los comandos M/m (mover), l (línea), c (curva) y z (cerrar), en coordenadas del SVG.
        const string TRAZO =
            "M3378 9358 c21 -24 90 -99 152 -168 63 -69 123 -134 134 -145 12 -12 67 -72 122 -135 56 -63 115 -127 " +
            "130 -142 16 -14 43 -43 59 -64 51 -63 171 -197 206 -231 19 -18 48 -49 66 -70 101 -119 142 -165 207 " +
            "-229 39 -40 82 -86 96 -104 14 -18 45 -54 70 -80 25 -26 56 -63 70 -81 33 -45 128 -149 204 -225 33 -34 " +
            "75 -82 93 -105 18 -24 77 -91 132 -149 56 -58 102 -108 103 -112 2 -3 116 -131 254 -285 138 -153 271 " +
            "-300 295 -327 161 -179 240 -264 271 -293 7 -6 27 -28 43 -48 17 -21 58 -70 93 -110 34 -40 62 -77 62 " +
            "-84 0 -6 -10 -13 -22 -15 -152 -31 -193 -41 -250 -61 -93 -32 -188 -76 -188 -86 0 -5 -7 -9 -15 -9 -8 0 " +
            "-15 -4 -15 -10 0 -5 -7 -10 -15 -10 -8 0 -15 -4 -15 -10 0 -5 -9 -10 -20 -10 -11 0 -20 -4 -20 -10 0 -5 " +
            "-6 -10 -13 -10 -7 0 -19 -9 -27 -20 -8 -11 -19 -20 -25 -20 -6 0 -17 -9 -25 -20 -8 -11 -19 -20 -25 -20 " +
            "-5 0 -39 -32 -75 -71 -36 -38 -118 -123 -182 -188 -65 -65 -118 -123 -118 -130 0 -6 -6 -11 -12 -11 -7 " +
            "0 -24 -14 -38 -31 -59 -71 -100 -109 -116 -109 -14 0 -148 130 -199 193 -37 46 -208 237 -231 259 -16 " +
            "14 -42 43 -59 64 -25 32 -142 164 -256 289 -15 17 -131 147 -259 290 -127 143 -254 281 -281 307 -27 26 " +
            "-49 51 -49 55 0 5 -49 59 -109 121 -61 62 -111 118 -113 124 -7 19 -222 257 -234 257 -6 1 -15 11 -21 " +
            "24 -6 12 -66 83 -134 157 -68 74 -134 148 -148 164 -30 35 -224 249 -258 286 -12 14 -71 79 -130 145 " +
            "-59 66 -113 121 -120 123 -7 2 -13 7 -13 12 0 5 -16 25 -35 44 -19 20 -35 39 -35 44 0 4 -18 26 -40 47 " +
            "-28 27 -40 47 -38 62 2 13 142 150 383 373 209 193 417 389 462 436 44 46 84 84 87 84 3 0 34 27 68 60 " +
            "35 33 66 60 70 60 4 0 25 -19 46 -42z m109 -3462 c28 -13 54 -22 57 -18 3 3 6 0 6 -6 0 -7 5 -12 10 -12 " +
            "6 0 19 -8 30 -17 10 -10 40 -29 67 -42 26 -13 59 -30 73 -38 14 -8 33 -17 43 -20 9 -3 17 -9 17 -14 0 " +
            "-5 7 -9 15 -9 8 0 15 -4 15 -8 0 -5 16 -14 35 -22 19 -8 35 -17 35 -22 0 -4 7 -8 15 -8 8 0 15 -4 15 " +
            "-10 0 -5 9 -10 20 -10 11 0 20 -4 20 -10 0 -5 5 -10 10 -10 6 0 32 -21 58 -46 l47 -46 0 -342 c0 -187 " +
            "-3 -353 -7 -368 -9 -38 -43 -69 -112 -103 -32 -17 -69 -40 -83 -52 -13 -13 -31 -23 -39 -23 -7 0 -14 -4 " +
            "-14 -10 0 -5 -7 -10 -15 -10 -8 0 -15 -4 -15 -10 0 -5 -7 -10 -15 -10 -8 0 -15 -4 -15 -10 0 -5 -9 -10 " +
            "-20 -10 -11 0 -20 -4 -20 -10 0 -5 -8 -10 -17 -10 -18 0 -156 -78 -161 -92 -2 -4 -10 -8 -18 -8 -8 0 " +
            "-14 -4 -14 -10 0 -5 -9 -10 -20 -10 -11 0 -20 -4 -20 -10 0 -6 -27 -10 -60 -10 -33 0 -60 4 -60 10 0 6 " +
            "-9 10 -20 10 -11 0 -20 5 -20 10 0 6 -7 10 -15 10 -8 0 -15 5 -15 10 0 6 -9 10 -20 10 -11 0 -20 4 -20 " +
            "9 0 10 -231 151 -270 165 -14 5 -27 12 -30 15 -3 4 -39 28 -81 54 -118 73 -114 57 -112 449 2 266 5 333 " +
            "17 362 17 40 39 66 55 66 6 0 11 3 11 8 0 4 10 12 23 18 12 6 42 22 67 34 25 12 125 69 224 127 254 150 " +
            "255 150 343 109z m4851 11 c9 -6 12 -166 12 -745 l0 -737 -728 1 c-400 1 -756 -3 -792 -8 -68 -9 -163 " +
            "-38 -168 -50 -2 -5 -13 -8 -25 -8 -50 0 -133 -66 -320 -253 -106 -106 -215 -219 -242 -252 -28 -32 -59 " +
            "-66 -69 -75 -28 -21 -199 -199 -209 -216 -4 -7 -73 -78 -152 -158 -80 -80 -145 -150 -145 -155 0 -11 " +
            "-49 -64 -71 -77 -9 -5 -67 -63 -129 -129 -62 -66 -176 -186 -254 -267 -77 -81 -147 -159 -155 -173 -9 " +
            "-14 -20 -25 -26 -25 -5 0 -59 -53 -119 -118 -59 -64 -156 -167 -215 -227 -147 -153 -263 -275 -357 -375 " +
            "-43 -47 -153 -163 -244 -259 -90 -95 -171 -182 -179 -194 -8 -12 -32 -38 -54 -57 -33 -30 -161 -167 " +
            "-300 -322 -53 -59 -89 -63 -127 -16 -14 17 -36 41 -50 55 -41 40 -184 175 -234 220 -25 23 -46 46 -46 " +
            "51 0 6 -15 22 -32 36 -58 46 -176 157 -313 293 -74 73 -137 133 -140 133 -14 0 -185 186 -185 201 0 8 " +
            "56 73 125 145 69 71 125 132 125 136 0 5 16 25 35 45 19 20 35 41 35 45 0 4 6 8 13 8 13 0 188 182 196 " +
            "203 2 7 50 57 106 112 56 55 128 131 161 170 32 38 106 117 164 175 58 58 123 127 145 154 49 60 131 " +
            "143 210 215 33 29 65 63 71 74 6 11 56 65 110 121 116 117 184 194 184 207 0 5 5 9 11 9 12 0 231 224 " +
            "238 243 2 5 52 58 110 116 138 137 271 281 271 292 0 5 4 9 10 9 10 0 150 142 180 182 8 11 65 70 125 " +
            "132 61 61 142 147 180 191 39 43 82 91 98 105 40 36 317 331 350 371 56 70 65 79 72 79 5 0 38 28 75 63 " +
            "90 85 152 137 162 137 4 0 8 5 8 10 0 6 6 10 14 10 8 0 16 3 18 8 4 10 87 52 104 52 8 0 14 3 14 8 0 4 " +
            "24 13 53 20 28 7 54 17 55 22 2 6 21 10 43 10 21 0 39 5 39 10 0 6 21 10 48 10 26 0 52 4 59 8 15 10 " +
            "2035 9 2051 -1z ";

        static GraphicsPath forma;

        // Convierte el trazo SVG en una figura de GDI+ (una sola vez).
        static GraphicsPath Forma()
        {
            if (forma != null) return forma;
            var p = new GraphicsPath(FillMode.Alternate);
            var partes = new List<string>();
            foreach (Match m in Regex.Matches(TRAZO, @"[MmLlCcZz]|-?\d+(\.\d+)?")) partes.Add(m.Value);

            int i = 0;
            char comando = 'M';
            PointF actual = PointF.Empty, inicio = PointF.Empty;
            Func<float> num = () => float.Parse(partes[i++], CultureInfo.InvariantCulture);
            while (i < partes.Count)
            {
                if (char.IsLetter(partes[i][0]))
                {
                    comando = partes[i++][0];
                    if (comando == 'z' || comando == 'Z') { p.CloseFigure(); actual = inicio; continue; }
                }
                bool relativo = char.IsLower(comando);
                float ox = relativo ? actual.X : 0, oy = relativo ? actual.Y : 0;
                switch (char.ToUpper(comando))
                {
                    case 'M':
                        actual = inicio = new PointF(ox + num(), oy + num());
                        p.StartFigure();
                        comando = relativo ? 'l' : 'L'; // los pares que siguen a un M son líneas
                        break;
                    case 'L':
                        var fin = new PointF(ox + num(), oy + num());
                        p.AddLine(actual, fin);
                        actual = fin;
                        break;
                    case 'C':
                        var c1 = new PointF(ox + num(), oy + num());
                        var c2 = new PointF(ox + num(), oy + num());
                        var c3 = new PointF(ox + num(), oy + num());
                        p.AddBezier(actual, c1, c2, c3);
                        actual = c3;
                        break;
                    default:
                        i++;
                        break;
                }
            }
            // El SVG usa transform="translate(0,1045) scale(0.1,-0.1)": se aplica lo mismo.
            using (var m = new Matrix(0.1f, 0, 0, -0.1f, 0, 1045)) p.Transform(m);
            forma = p;
            return forma;
        }

        // Dibuja las formas del logo centradas dentro de un rectángulo, manteniendo la proporción.
        public static void Dibujar(Graphics g, RectangleF destino, Color color)
        {
            using (var p = (GraphicsPath)Forma().Clone())
            {
                var b = p.GetBounds();
                float escala = Math.Min(destino.Width / b.Width, destino.Height / b.Height);
                using (var m = new Matrix())
                {
                    m.Translate(destino.X + (destino.Width - b.Width * escala) / 2, destino.Y + (destino.Height - b.Height * escala) / 2);
                    m.Scale(escala, escala);
                    m.Translate(-b.X, -b.Y);
                    p.Transform(m);
                }
                var antes = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pincel = new SolidBrush(color)) g.FillPath(pincel, p);
                g.SmoothingMode = antes;
            }
        }

        // Círculo de color con el logo blanco adentro (verde = todo bien, rojo = corte).
        public static Bitmap Imagen(Color circulo, int tam)
        {
            var bmp = new Bitmap(tam, tam);
            using (var g = Graphics.FromImage(bmp))
            using (var b = new SolidBrush(circulo))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, 0.5f, 0.5f, tam - 1.5f, tam - 1.5f);
                float margen = tam * 0.19f;
                Dibujar(g, new RectangleF(margen, margen, tam - 2 * margen, tam - 2 * margen), Color.White);
            }
            return bmp;
        }

        public static Icon Icono(Color circulo, int tam)
        {
            using (var bmp = Imagen(circulo, tam)) return Icon.FromHandle(bmp.GetHicon());
        }

        // Guarda un .ico con varios tamaños (lo usa herramientas\generar-icono.ps1 para el ícono del .exe).
        public static void GuardarIco(string ruta, Color circulo)
        {
            int[] tamanos = { 16, 24, 32, 48, 64, 128, 256 };
            var imagenes = new List<byte[]>();
            foreach (int t in tamanos)
                using (var bmp = Imagen(circulo, t))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    imagenes.Add(ms.ToArray());
                }
            using (var w = new BinaryWriter(File.Create(ruta)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)tamanos.Length); // cabecera ICO
                int posicion = 6 + 16 * tamanos.Length;
                for (int k = 0; k < tamanos.Length; k++)
                {
                    w.Write((byte)(tamanos[k] >= 256 ? 0 : tamanos[k]));
                    w.Write((byte)(tamanos[k] >= 256 ? 0 : tamanos[k]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(imagenes[k].Length);
                    w.Write(posicion);
                    posicion += imagenes[k].Length;
                }
                foreach (var img in imagenes) w.Write(img);
            }
        }
    }
}
