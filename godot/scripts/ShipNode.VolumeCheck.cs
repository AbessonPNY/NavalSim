using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// LE MODÈLE PÈSE-T-IL CE QUE DIT SA FICHE ? (demandé : « si je gonfle un peu le modèle, le tonnage
/// se remettra-t-il seul ? » — non : la physique ne lit que la fiche ; d'où ce contrôle).
///
/// Une fois le navire mis à l'eau, le volume de la coque DESSINÉE sous la ligne où le solveur la fait
/// flotter, tranche par tranche sur ses triangles — la même mesure que tools/fit-ship.js —, comparé au
/// déplacement de la fiche. Au-delà de cinq pour cent, un avertissement qui dit la commande à lancer ;
/// rien n'est réécrit, la fiche reste à son auteur. Une fois par fiche et par session.
/// </summary>
public partial class ShipNode
{
    static readonly HashSet<string> VolumeChecked = new();
    const double VolumeTolerance = 0.05;

    /// <param name="seaY">La mer, dans le repère du navire (−y de son assise).</param>
    public void CheckModelVolume(double seaY)
    {
        // a buoy or a crate under a tonne is an object, not a ship: nothing to weigh
        if (ModelRoot == null || !(Spec.Raw.DisplacementTonnes >= 1) || !VolumeChecked.Add(Spec.Id) || HullPart() is not { } hull) return;
        var tris = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        var mesh = hull.Mi.Mesh;
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arr = mesh.SurfaceGetArrays(s);
            var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var ix = arr[(int)Mesh.ArrayType.Index].AsInt32Array();
            for (int i = 0; i < v.Length; i++) v[i] = hull.Rel * v[i];
            if (ix.Length > 0) for (int i = 0; i + 2 < ix.Length; i += 3) tris.Add((v[ix[i]], v[ix[i + 1]], v[ix[i + 2]]));
            else for (int i = 0; i + 2 < v.Length; i += 3) tris.Add((v[i], v[i + 1], v[i + 2]));
        }
        if (tris.Count == 0) return;

        double z0 = hull.Min.Z, z1 = hull.Max.Z, y0 = hull.Min.Y, yw = seaY;
        if (yw <= y0) return;
        const double dz = 0.5, dy = 0.1;
        double vol = 0, wlHalf = 0;
        var segs = new List<(double Ax, double Ay, double Bx, double By)>();
        for (double z = z0 + dz / 2; z < z1; z += dz)
        {
            // la tranche : chaque triangle coupé par le plan z donne un segment (x, y)
            segs.Clear();
            foreach (var (a, b, c) in tris)
            {
                double zmin = Math.Min(a.Z, Math.Min(b.Z, c.Z)), zmax = Math.Max(a.Z, Math.Max(b.Z, c.Z));
                if (zmin > z || zmax < z) continue;
                Span<(double X, double Y)> p = stackalloc (double, double)[3];
                int n = 0;
                Cut(a, b, z, p, ref n); Cut(b, c, z, p, ref n); Cut(c, a, z, p, ref n);
                if (n >= 2) segs.Add((p[0].X, p[0].Y, p[1].X, p[1].Y));
            }
            double area = 0;
            for (double y = y0; y < yw; y += dy)
            {
                double h = Math.Min(dy, yw - y), yy = y + h / 2, hb = 0;
                foreach (var s in segs)
                    if ((s.Ay - yy) * (s.By - yy) <= 0 && s.Ay != s.By)
                        hb = Math.Max(hb, Math.Abs(s.Ax + (yy - s.Ay) / (s.By - s.Ay) * (s.Bx - s.Ax)));
                area += 2 * hb * h;
                if (y + dy >= yw) wlHalf = Math.Max(wlHalf, hb);      // the band at the waterline
            }
            vol += area * dz;
        }
        double model = vol * 1.025, sheet = Spec.Raw.DisplacementTonnes;
        /* EST-CE BIEN LA COQUE ? Une coque en plusieurs morceaux (la Roter Löwe : bordé, préceinte,
           galeries) n'a pas de « plus gros maillage » qui la soit : en mesurer un annonçait 23 t pour 300.
           La largeur trouvée à la flottaison le dit sans ambiguïté — moins de la moitié du bau, ce n'est pas
           la coque qu'on a tranchée. On se tait plutôt que de crier au loup. */
        if (wlHalf < 0.25 * Spec.B)
        {
            GD.Print(FormattableString.Invariant($"[{Spec.Id}] poids du modèle non contrôlé : son plus gros maillage n'a que {2 * wlHalf:F1} m de large à la flottaison, pour {Spec.B:F1} au bau — coque en plusieurs morceaux"));
            return;
        }
        double gap = (model - sheet) / sheet;
        string line = FormattableString.Invariant($"[{Spec.Id}] le modèle déplace {model:F0} t à la flottaison où flotte la fiche, qui en annonce {sheet:F0} ({Math.Round(gap * 100):+0;-0;0} %)");
        if (Math.Abs(gap) > VolumeTolerance)
            GD.PushWarning(line + $" — modèle retouché ? node tools/fit-ship.js {Spec.Id} --ecrire");
        else GD.Print(line);
    }

    static void Cut(Vector3 a, Vector3 b, double z, Span<(double X, double Y)> p, ref int n)
    {
        if (n >= 3 || (a.Z - z) * (b.Z - z) > 0 || a.Z == b.Z) return;
        double u = (z - a.Z) / (b.Z - a.Z);
        p[n++] = (a.X + u * (b.X - a.X), a.Y + u * (b.Y - a.Y));
    }
}
