using Godot;
using System;
using System.Text;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// MESURE (-- --flore-carte dossier) : la flore semée autour du port de départ, vue
/// d'en haut — chaque pièce de chaque semis de props/flore/ en CSV (espèce ; est ; nord,
/// en mètres depuis le port), et la terre en caractères (# terre, B bâti, . mer), une
/// case de quarante mètres sur douze kilomètres. De quoi juger une répartition sans
/// cent captures.
/// </summary>
public partial class ShipDemo
{
    void DumpFlora(string dir)
    {
        if (_world == null) return;
        // autour du navire, là où il est (un --quai l'a pu poser ailleurs qu'au départ)
        var o0 = _sea.Core.Origin; var b0 = _ship.Physics.Body.Pos;
        var sp = (X: o0.X + b0.X, Z: o0.Z + b0.Z);
        var pts = new StringBuilder();
        foreach (var s in _world.Region.Scatters)
        {
            if (!s.Glb.Contains("flore")) continue;
            string kind = s.Name.Split(' ')[0];
            foreach (var p in Scatter.Place(_world, s))
            {
                double dx = p.X - sp.X, dz = p.Z - sp.Z;
                // est = −x, nord = +z (Repères)
                if (Math.Abs(dx) < 6000 && Math.Abs(dz) < 6000)
                    pts.Append(FormattableString.Invariant($"{kind};{-dx:F0};{dz:F0}")).Append('\n');
            }
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "flore-pts.csv"), pts.ToString());
        var land = new StringBuilder();
        for (double dz = 6000; dz > -6000; dz -= 40)
        {
            for (double ex = -6000; ex < 6000; ex += 40)
            {
                double x = sp.X - ex, z = sp.Z + dz;
                land.Append(_world.HeightAt(x, z) > 0 ? (_world.Built(x, z, 0) ? 'B' : '#') : '.');
            }
            land.Append('\n');
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "flore-terre.txt"), land.ToString());
        GD.Print($"[flore] carte écrite dans {dir}");
    }
}
