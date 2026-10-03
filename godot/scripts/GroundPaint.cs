using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE SOL PEINT D'UN PORT — les images que le pinceau du mode création remplit, et
/// que le shader de la terre lit (ground_paint.gdshaderinc).
///
/// DEUX COUCHES, de trois matières chacune, parce qu'un pixel RGBA n'en porte que
/// trois et ce qu'il recouvre :
///   · la TERRE (l'image de la fiche) : r herbe, g pavés, b sable ;
///   · les FONDS (la même, suffixée « -fonds ») : r sable blanc, g vase, b herbier.
/// a = combien la couche recouvre la teinte du relief. Un pixel jamais peint
/// (a = 0) laisse le relief tel qu'il est. Peindre une couche EFFACE l'autre au même
/// endroit, d'autant : la dernière matière posée est celle qu'on voit, comme sur
/// une toile.
///
/// L'IMAGE VIT EN OCTETS, côté C# : un coup de pinceau touche quelques milliers
/// de pixels, et les passer un à un par l'interface du moteur coûterait plus que
/// le calcul. On ne la recopie dans la texture qu'une fois par huitième de
/// seconde pendant qu'on peint (seize mégaoctets à chaque fois), et au lever du
/// pinceau.
/// </summary>
public sealed class GroundPaint
{
    public enum Brush { Grass, Cobble, Sand, Erase, WhiteSand, Mud, Seagrass }

    public readonly PaintSpec Spec;
    /// <summary>Les deux fichiers : la terre, puis les fonds.</summary>
    public readonly string[] Paths;
    /// <summary>Le coin du carré, en mètres VRAIS, et son côté.</summary>
    public readonly double X0, Z0, Side;
    public readonly int N;
    public readonly ImageTexture[] Textures = new ImageTexture[2];
    public bool Dirty;

    readonly byte[][] _px = new byte[2][];
    readonly Image[] _img = new Image[2];
    readonly bool[] _stale = new bool[2];
    double _sinceUpload;
    readonly LinkedList<(byte[], byte[])> _undo = new();
    const int UndoDepth = 8;

    /// <summary>La seconde couche s'appelle comme la première, suffixée : port-royal.png → port-royal-fonds.png.</summary>
    public static string SeabedPath(string path) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "",
                               System.IO.Path.GetFileNameWithoutExtension(path) + "-fonds" + System.IO.Path.GetExtension(path));

    public GroundPaint(PaintSpec spec, double cx, double cz, string path)
    {
        Spec = spec; Side = spec.Side;
        Paths = new[] { path, SeabedPath(path) };
        N = Math.Max(16, (int)Math.Round(spec.Side / spec.Step));
        X0 = cx - Side * 0.5; Z0 = cz - Side * 0.5;
        for (int l = 0; l < 2; l++)
        {
            _px[l] = new byte[N * N * 4];
            if (System.IO.File.Exists(Paths[l]))
            {
                var loaded = Image.LoadFromFile(Paths[l]);
                if (loaded != null && loaded.GetWidth() == N && loaded.GetHeight() == N)
                {
                    loaded.Convert(Image.Format.Rgba8);
                    Buffer.BlockCopy(loaded.GetData(), 0, _px[l], 0, _px[l].Length);
                }
                else GD.PushWarning($"[peinture] {Paths[l]} : {loaded?.GetWidth()}×{loaded?.GetHeight()} au lieu de {N}×{N} — on repart d'un sol vierge, l'ancien n'est pas écrasé tant qu'on ne peint pas.");
            }
            _img[l] = Image.CreateFromData(N, N, false, Image.Format.Rgba8, _px[l]);
            Textures[l] = ImageTexture.CreateFromImage(_img[l]);
        }
    }

    /// <summary>Ce point (mètres vrais) est-il dans le carré ?</summary>
    public bool Covers(double x, double z) => x >= X0 && z >= Z0 && x < X0 + Side && z < Z0 + Side;

    /// <summary>Une matière des fonds ?</summary>
    public static bool Seabed(Brush b) => b >= Brush.WhiteSand;

    /// <summary>Un coup de pinceau commence : l'état d'avant va sur la pile.</summary>
    public void BeginStroke()
    {
        _undo.AddLast(((byte[])_px[0].Clone(), (byte[])_px[1].Clone()));
        while (_undo.Count > UndoDepth) _undo.RemoveFirst();
    }

    /// <summary>
    /// UNE TOUCHE : vers la matière choisie, d'autant plus qu'on est près du
    /// centre (plein jusqu'à mi-rayon, puis un fondu), et d'autant plus qu'on
    /// appuie longtemps (<paramref name="amount"/>, de 0 à 1). L'autre couche
    /// s'efface d'autant ; la gomme efface les deux et rend le sol au relief.
    /// </summary>
    public void Dab(double x, double z, double radius, Brush b, double amount)
    {
        double step = Spec.Step;
        double ci = (x - X0) / step, cj = (z - Z0) / step, rp = radius / step;
        int i0 = Math.Max(0, (int)(ci - rp)), i1 = Math.Min(N - 1, (int)(ci + rp) + 1);
        int j0 = Math.Max(0, (int)(cj - rp)), j1 = Math.Min(N - 1, (int)(cj + rp) + 1);
        if (i0 > i1 || j0 > j1) return;
        bool erase = b == Brush.Erase;
        int layer = Seabed(b) ? 1 : 0;
        byte tr = 0, tg = 0, tb = 0;
        switch (b)
        {
            case Brush.Grass: case Brush.WhiteSand: tr = 255; break;
            case Brush.Cobble: case Brush.Mud: tg = 255; break;
            case Brush.Sand: case Brush.Seagrass: tb = 255; break;
        }
        byte[] own = _px[layer], other = _px[1 - layer];
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double dx = (i + 0.5 - ci) / rp, dz = (j + 0.5 - cj) / rp;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d >= 1) continue;
                double w = d < 0.5 ? 1 : 1 - (d - 0.5) / 0.5;
                double k = Math.Clamp(MathX.Smooth01(w) * amount, 0, 1);
                int o = (j * N + i) * 4;
                // l'autre couche cède ce que celle-ci prend ; la gomme les rend toutes deux
                other[o + 3] = (byte)Math.Round(other[o + 3] * (1 - k));
                if (erase) { own[o + 3] = (byte)Math.Round(own[o + 3] * (1 - k)); continue; }
                own[o] = (byte)Math.Round(own[o] + (tr - own[o]) * k);
                own[o + 1] = (byte)Math.Round(own[o + 1] + (tg - own[o + 1]) * k);
                own[o + 2] = (byte)Math.Round(own[o + 2] + (tb - own[o + 2]) * k);
                own[o + 3] = (byte)Math.Round(own[o + 3] + (255 - own[o + 3]) * k);
            }
        _stale[0] = _stale[1] = true;
        Dirty = true;
    }

    /// <summary>Recopier dans les textures, au plus souvent tous les huitièmes de seconde ; maintenant si <paramref name="now"/>.</summary>
    public void Flush(double dt, bool now = false)
    {
        _sinceUpload += dt;
        if (!(_stale[0] || _stale[1]) || (!now && _sinceUpload < 0.125)) return;
        for (int l = 0; l < 2; l++)
        {
            if (!_stale[l]) continue;
            _img[l].SetData(N, N, false, Image.Format.Rgba8, _px[l]);
            Textures[l].Update(_img[l]);
            _stale[l] = false;
        }
        _sinceUpload = 0;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        var (a, b) = _undo.Last!.Value;
        Buffer.BlockCopy(a, 0, _px[0], 0, a.Length);
        Buffer.BlockCopy(b, 0, _px[1], 0, b.Length);
        _undo.RemoveLast();
        _stale[0] = _stale[1] = true; Dirty = true;
        Flush(0, true);
        return true;
    }

    /// <summary>Écrire les deux couches. Celle des fonds n'est écrite que si elle porte quelque chose — ou existait déjà.</summary>
    public bool Save()
    {
        bool ok = true;
        for (int l = 0; l < 2; l++)
        {
            if (l == 1 && !System.IO.File.Exists(Paths[1]) && !Any(_px[1])) continue;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Paths[l])!);
            _img[l].SetData(N, N, false, Image.Format.Rgba8, _px[l]);
            ok &= _img[l].SavePng(Paths[l]) == Error.Ok;
        }
        if (ok) Dirty = false;
        return ok;
    }

    static bool Any(byte[] px)
    {
        for (int o = 3; o < px.Length; o += 4) if (px[o] != 0) return true;
        return false;
    }
}
