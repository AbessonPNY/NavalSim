using System;
using System.IO;
using System.IO.Compression;

namespace NavalSim.Core;

/// <summary>
/// LE RELIEF, LU COMME UNE IMAGE — l'équivalent du canvas de la page, qui
/// dessine le PNG puis relit ses octets (<c>World.load</c>).
///
/// Un seul octet par pixel en sortie : le GRIS, canal rouge pour une image en
/// couleurs, exactement comme la page (« the red channel is the grey »). Le
/// reste du jeu ne connaît que ce tableau.
///
/// Pourquoi ici, et pas dans le moteur qui sait déjà lire un PNG : le banc de
/// parité tourne en console, sans Godot, et doit lire LE MÊME fichier que le
/// jeu. Un décodeur partagé vaut mieux que deux chemins dont l'un n'est jamais
/// vérifié — et PNG étant sans perte, ce que ce code rend est ce que n'importe
/// quel autre décodeur rendrait, à ceci près qu'on peut le prouver.
/// </summary>
public static class GreyPng
{
    /// <summary>
    /// Rend le gris, une valeur par pixel, lignes de haut en bas.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// Ce qui n'est pas un PNG simple : entrelacé, 16 bits, ou palettisé. Le
    /// relief du jeu est écrit par tools/region-heightmap.js, qui n'en produit
    /// jamais de tels ; mieux vaut le dire que rendre une image fausse.
    /// </exception>
    public static (int W, int H, byte[] Grey) Decode(byte[] png)
    {
        if (png.Length < 8 || png[0] != 0x89 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G')
            throw new InvalidDataException("ce n'est pas un PNG");

        int w = 0, h = 0, bits = 0, color = 0;
        var idat = new MemoryStream();
        int p = 8;
        while (p + 8 <= png.Length)
        {
            int len = Be32(png, p);
            string type = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
            int data = p + 8;
            switch (type)
            {
                case "IHDR":
                    w = Be32(png, data); h = Be32(png, data + 4);
                    bits = png[data + 8]; color = png[data + 9];
                    if (png[data + 12] != 0) throw new InvalidDataException("PNG entrelacé (Adam7) non géré");
                    if (bits != 8) throw new InvalidDataException($"PNG {bits} bits : seul le 8 bits est géré");
                    if (color == 3) throw new InvalidDataException("PNG palettisé non géré");
                    break;
                case "IDAT": idat.Write(png, data, len); break;
                case "IEND": p = png.Length; break;
            }
            p = data + len + 4;                       // + le CRC, qu'on ne vérifie pas
        }
        if (w <= 0 || h <= 0) throw new InvalidDataException("PNG sans IHDR");

        // combien d'octets par pixel : c'est le PAS du filtre, et s'en tromper
        // décale toute l'image d'une manière qui ressemble à du bruit
        int bpp = color switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        int stride = w * bpp;

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new byte[(stride + 1) * h];
        int got = 0;
        while (got < raw.Length)
        {
            int n = z.Read(raw, got, raw.Length - got);
            if (n <= 0) break;
            got += n;
        }
        if (got < raw.Length) throw new InvalidDataException("PNG tronqué");

        /* LE DÉFILTRAGE, sur place et ligne par ligne. Chaque ligne porte en
           tête le filtre qui l'a écrite, et se relit contre la ligne PRÉCÉDENTE
           déjà défiltrée — d'où l'ordre, qui ne peut pas être parallélisé. */
        var grey = new byte[w * h];
        var prev = new byte[stride];
        var cur = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0;   // à gauche
                int b = prev[i];                        // au-dessus
                int c = i >= bpp ? prev[i - bpp] : 0;   // en diagonale
                cur[i] = f switch
                {
                    0 => cur[i],
                    1 => (byte)(cur[i] + a),
                    2 => (byte)(cur[i] + b),
                    3 => (byte)(cur[i] + (a + b) / 2),
                    4 => (byte)(cur[i] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"filtre PNG inconnu : {f}")
                };
            }
            // le rouge porte le gris, comme dans la page
            for (int x = 0; x < w; x++) grey[y * w + x] = cur[x * bpp];
            (prev, cur) = (cur, prev);
        }
        return (w, h, grey);
    }

    /// <summary>Le prédicteur de Paeth : celui des trois voisins dont la somme s'écarte le moins.</summary>
    static int Paeth(int a, int b, int c)
    {
        int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static int Be32(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
}
