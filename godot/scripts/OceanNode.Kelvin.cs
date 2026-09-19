using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE V DE KELVIN, QUI SUIT LA ROUTE. Dessiné d'abord dans le repère du navire,
/// il pivotait d'un bloc avec lui dans un virage, pendant que le sillage de poupe
/// suivait sa vraie route — relevé à l'usage.
///
/// Ce qui fait un sillage de Kelvin : chaque point où l'étrave est passée émet
/// une onde qui s'écarte de la route, et les bras sont le lieu de ces ondes,
/// à 19,5° du cap (tan = 0,354) en ligne droite. On retient donc la ROUTE de
/// l'étrave — un point tous les deux mètres, sur une centaine de mètres —, et
/// chaque point pousse de part et d'autre une crête perpendiculaire à SON cap
/// d'alors, à 0,354 fois la distance parcourue depuis. En ligne droite, c'est
/// exactement le V ; en virage, les bras se courbent avec la route passée.
///
/// Les deux bras de chaque navire vont à la mer dans une petite texture — une
/// rangée par bras, une colonne par point : (x, z, âge en mètres, valide) —, et
/// la mer mesure sa distance à ces lignes brisées.
/// </summary>
public partial class OceanNode
{
    const int KelvinN = 48;          // points par bras
    const double KelvinStep = 2.0;   // mètres de route entre deux points
    const double KelvinTan = 0.354;  // tan 19,47°

    sealed class Wake
    {
        public readonly Vector2[] Bow = new Vector2[KelvinN];
        public readonly Vector2[] Perp = new Vector2[KelvinN];
        public readonly double[] At = new double[KelvinN];   // la distance parcourue quand l'étrave y était
        public int Count, Head;                             // anneau : Head est le plus récent
        public double Travel;                               // distance parcourue par l'étrave
        public Vector2 Last;
        public bool Started;
        public int Seen;                                    // la dernière image où elle était à flot
    }

    readonly Dictionary<ShipPhysics, Wake> _wakes = new();
    readonly float[] _kelvinData = new float[KelvinN * Config.MaxShips * 2 * 4];
    byte[]? _kelvinBytes;
    Image? _kelvinImg;
    ImageTexture? _kelvinTex;
    int _kelvinFrame;

    /// <summary>La route de chaque étrave, et les bras qu'elle a jetés, vers la mer. Après TrackShips.</summary>
    void TrackWakes(IReadOnlyList<ShipPhysics> fleet)
    {
        _kelvinFrame++;
        Array.Clear(_kelvinData);
        for (int i = 0; i < _shipCount; i++)
        {
            var p = fleet[i];
            if (!_wakes.TryGetValue(p, out var w)) _wakes[p] = w = new Wake();
            w.Seen = _kelvinFrame;
            var fwd = _shipFwd[i];
            var bow = new Vector2(_shipPos[i].X, _shipPos[i].Z) + fwd * _hullEnds[i].Y;
            var perp = new Vector2(fwd.Y, -fwd.X);
            if (!w.Started) { w.Started = true; w.Last = bow; }
            w.Travel += w.Last.DistanceTo(bow);
            w.Last = bow;
            if (w.Count == 0 || w.Travel - w.At[w.Head] >= KelvinStep)
            {
                w.Head = (w.Head + 1) % KelvinN;
                w.Bow[w.Head] = bow; w.Perp[w.Head] = perp; w.At[w.Head] = w.Travel;
                if (w.Count < KelvinN) w.Count++;
            }
            // les deux bras : l'étrave d'abord (âge nul), puis la route, du plus récent au plus ancien
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 0 ? 1 : -1;
                int row = i * 2 + side, o = row * KelvinN * 4;
                Put(o, bow + perp * sgn * 0.4f, 0);
                for (int j = 0; j < w.Count && j + 1 < KelvinN; j++)
                {
                    int k = (w.Head - j + KelvinN) % KelvinN;
                    double age = w.Travel - w.At[k];
                    Put(o + (j + 1) * 4, w.Bow[k] + w.Perp[k] * sgn * (float)(KelvinTan * age + 0.4), (float)age);
                }
            }
        }
        // les coques qui ont quitté la flotte emportent leur route
        if (_kelvinFrame % 120 == 0)
        {
            var gone = new List<ShipPhysics>();
            foreach (var (p, w) in _wakes) if (w.Seen != _kelvinFrame) gone.Add(p);
            foreach (var p in gone) _wakes.Remove(p);
        }

        _kelvinBytes ??= new byte[_kelvinData.Length * 4];
        Buffer.BlockCopy(_kelvinData, 0, _kelvinBytes, 0, _kelvinBytes.Length);
        if (_kelvinImg == null)
        {
            _kelvinImg = Image.CreateFromData(KelvinN, Config.MaxShips * 2, false, Image.Format.Rgbaf, _kelvinBytes);
            _kelvinTex = ImageTexture.CreateFromImage(_kelvinImg);
        }
        else
        {
            _kelvinImg.SetData(KelvinN, Config.MaxShips * 2, false, Image.Format.Rgbaf, _kelvinBytes);
            _kelvinTex!.Update(_kelvinImg);
        }
    }

    void Put(int o, Vector2 at, float age)
    {
        _kelvinData[o] = at.X; _kelvinData[o + 1] = at.Y; _kelvinData[o + 2] = age; _kelvinData[o + 3] = 1;
    }

    /// <summary>L'origine flottante : la route retenue glisse avec le reste du monde.</summary>
    public void ShiftWakes(float dx, float dz)
    {
        var d = new Vector2(dx, dz);
        foreach (var w in _wakes.Values)
        {
            for (int k = 0; k < KelvinN; k++) w.Bow[k] += d;
            w.Last += d;
        }
    }
}
