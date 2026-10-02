using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE SOL PEINT D'UN PORT — l'image que le pinceau du mode création remplit, et
/// que le shader de la terre lit (ground_paint.gdshaderinc).
///
/// Un pixel par demi-mètre : rgb = combien d'herbe, de pavés, de sable ; a =
/// combien il recouvre la teinte que le relief donne de lui-même. Un pixel jamais
/// peint (a = 0) laisse le relief tel qu'il est — la peinture ne remplace que là
/// où l'on est passé.
///
/// L'IMAGE VIT EN OCTETS, côté C# : un coup de pinceau touche quelques milliers
/// de pixels, et les passer un à un par l'interface du moteur coûterait plus que
/// le calcul. On ne la recopie dans la texture qu'une fois par huitième de
/// seconde pendant qu'on peint (seize mégaoctets à chaque fois), et au lever du
/// pinceau.
/// </summary>
public sealed class GroundPaint
{
    public enum Brush { Grass, Cobble, Sand, Erase }

    public readonly PaintSpec Spec;
    public readonly string Path;
    /// <summary>Le coin du carré, en mètres VRAIS, et son côté.</summary>
    public readonly double X0, Z0, Side;
    public readonly int N;
    public readonly ImageTexture Texture;
    public bool Dirty;

    readonly byte[] _px;
    readonly Image _img;
    bool _stale;
    double _sinceUpload;
    readonly LinkedList<byte[]> _undo = new();
    const int UndoDepth = 8;

    public GroundPaint(PaintSpec spec, double cx, double cz, string path)
    {
        Spec = spec; Path = path; Side = spec.Side;
        N = Math.Max(16, (int)Math.Round(spec.Side / spec.Step));
        X0 = cx - Side * 0.5; Z0 = cz - Side * 0.5;
        _px = new byte[N * N * 4];
        if (System.IO.File.Exists(path))
        {
            var loaded = Image.LoadFromFile(path);
            if (loaded != null && loaded.GetWidth() == N && loaded.GetHeight() == N)
            {
                loaded.Convert(Image.Format.Rgba8);
                Buffer.BlockCopy(loaded.GetData(), 0, _px, 0, _px.Length);
            }
            else GD.PushWarning($"[peinture] {path} : {loaded?.GetWidth()}×{loaded?.GetHeight()} au lieu de {N}×{N} — on repart d'un sol vierge, l'ancien n'est pas écrasé tant qu'on ne peint pas.");
        }
        _img = Image.CreateFromData(N, N, false, Image.Format.Rgba8, _px);
        Texture = ImageTexture.CreateFromImage(_img);
    }

    /// <summary>Ce point (mètres vrais) est-il dans le carré ?</summary>
    public bool Covers(double x, double z) => x >= X0 && z >= Z0 && x < X0 + Side && z < Z0 + Side;

    /// <summary>Un coup de pinceau commence : l'état d'avant va sur la pile.</summary>
    public void BeginStroke()
    {
        _undo.AddLast((byte[])_px.Clone());
        while (_undo.Count > UndoDepth) _undo.RemoveFirst();
    }

    /// <summary>
    /// UNE TOUCHE : vers la matière choisie, d'autant plus qu'on est près du
    /// centre (plein jusqu'à mi-rayon, puis un fondu), et d'autant plus qu'on
    /// appuie longtemps (<paramref name="amount"/>, de 0 à 1). La gomme rend le
    /// sol au relief.
    /// </summary>
    public void Dab(double x, double z, double radius, Brush b, double amount)
    {
        double step = Spec.Step;
        double ci = (x - X0) / step, cj = (z - Z0) / step, rp = radius / step;
        int i0 = Math.Max(0, (int)(ci - rp)), i1 = Math.Min(N - 1, (int)(ci + rp) + 1);
        int j0 = Math.Max(0, (int)(cj - rp)), j1 = Math.Min(N - 1, (int)(cj + rp) + 1);
        if (i0 > i1 || j0 > j1) return;
        byte tr = 0, tg = 0, tb = 0;
        if (b == Brush.Grass) tr = 255; else if (b == Brush.Cobble) tg = 255; else if (b == Brush.Sand) tb = 255;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double dx = (i + 0.5 - ci) / rp, dz = (j + 0.5 - cj) / rp;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d >= 1) continue;
                double w = d < 0.5 ? 1 : 1 - (d - 0.5) / 0.5;
                double k = Math.Clamp(w * w * (3 - 2 * w) * amount, 0, 1);
                int o = (j * N + i) * 4;
                if (b == Brush.Erase)
                    _px[o + 3] = (byte)Math.Round(_px[o + 3] * (1 - k));
                else
                {
                    _px[o] = (byte)Math.Round(_px[o] + (tr - _px[o]) * k);
                    _px[o + 1] = (byte)Math.Round(_px[o + 1] + (tg - _px[o + 1]) * k);
                    _px[o + 2] = (byte)Math.Round(_px[o + 2] + (tb - _px[o + 2]) * k);
                    _px[o + 3] = (byte)Math.Round(_px[o + 3] + (255 - _px[o + 3]) * k);
                }
            }
        _stale = true;
        Dirty = true;
    }

    /// <summary>Recopier dans la texture, au plus souvent tous les huitièmes de seconde ; maintenant si <paramref name="now"/>.</summary>
    public void Flush(double dt, bool now = false)
    {
        _sinceUpload += dt;
        if (!_stale || (!now && _sinceUpload < 0.125)) return;
        _img.SetData(N, N, false, Image.Format.Rgba8, _px);
        Texture.Update(_img);
        _stale = false;
        _sinceUpload = 0;
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        Buffer.BlockCopy(_undo.Last!.Value, 0, _px, 0, _px.Length);
        _undo.RemoveLast();
        _stale = true; Dirty = true;
        Flush(0, true);
        return true;
    }

    public bool Save()
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _img.SetData(N, N, false, Image.Format.Rgba8, _px);
        bool ok = _img.SavePng(Path) == Error.Ok;
        if (ok) Dirty = false;
        return ok;
    }
}
