using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE DESSUS DU PONT, point par point — là où tombe ce qu'on jette à bord.
///
/// <see cref="DeckNear"/> ne dit qu'une hauteur par station, la plus haute des
/// environs, celle d'où pend une lanterne. Un poisson qui tombe sur le pont
/// doit trouver les planches SOUS LUI : le passavant, le panneau de cale, le toit
/// d'un rouf. On relève donc, sur une grille de quinze centimètres, la plus
/// haute surface horizontale du modèle sous un plafond d'un mètre au-dessus du
/// pont — assez bas pour ne prendre ni la bôme ni les vergues.
/// </summary>
public partial class ShipNode
{
    float[]? _deckTop;
    int _dtNx, _dtNz;
    double _dtX0, _dtZ0;
    bool _dtTried;
    const double DtCell = 0.15;

    /// <summary>La hauteur des planches en (x, z) du repère du navire.</summary>
    public double DeckTop(double x, double z)
    {
        if (!_dtTried) BuildDeckTop();
        if (_deckTop == null)
            return Lines.DeckY(Math.Clamp(z / Spec.L + 0.5, 0, 1));
        int ix = Math.Clamp((int)Math.Round((x - _dtX0) / DtCell), 0, _dtNx - 1);
        int iz = Math.Clamp((int)Math.Round((z - _dtZ0) / DtCell), 0, _dtNz - 1);
        return _deckTop[iz * _dtNx + ix];
    }

    void BuildDeckTop()
    {
        _dtTried = true;
        if (ModelRoot == null || HullPart() is not { } hull) return;
        double x0 = hull.Min.X, x1 = hull.Max.X, z0 = hull.Min.Z, z1 = hull.Max.Z;
        int nx = (int)Math.Ceiling((x1 - x0) / DtCell) + 1, nz = (int)Math.Ceiling((z1 - z0) / DtCell) + 1;
        var g = new float[nx * nz];
        Array.Fill(g, float.NegativeInfinity);
        foreach (var (mi, rel) in Meshes(ModelRoot))
        {
            var mesh = mi.Mesh;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arr = mesh.SurfaceGetArrays(s);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (v.Length < 3) continue;
                var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil
                    ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                int n = idx?.Length ?? v.Length;
                for (int t = 0; t + 2 < n; t += 3)
                {
                    Vector3 a = rel * v[idx?[t] ?? t], b = rel * v[idx?[t + 1] ?? t + 1], c = rel * v[idx?[t + 2] ?? t + 2];
                    var nn = (b - a).Cross(c - a);
                    float len = nn.Length();
                    // horizontale ou presque, dans les deux sens : l'enroulement d'un modèle n'est pas sûr
                    if (len < 1e-8f || Math.Abs(nn.Y) < 0.6f * len) continue;
                    double lo = Math.Min(a.X, Math.Min(b.X, c.X)), hi = Math.Max(a.X, Math.Max(b.X, c.X));
                    double lz = Math.Min(a.Z, Math.Min(b.Z, c.Z)), hz = Math.Max(a.Z, Math.Max(b.Z, c.Z));
                    int i0 = Math.Max(0, (int)Math.Ceiling((lo - x0) / DtCell)), i1 = Math.Min(nx - 1, (int)Math.Floor((hi - x0) / DtCell));
                    int k0 = Math.Max(0, (int)Math.Ceiling((lz - z0) / DtCell)), k1 = Math.Min(nz - 1, (int)Math.Floor((hz - z0) / DtCell));
                    double den = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                    if (Math.Abs(den) < 1e-12) continue;
                    for (int k = k0; k <= k1; k++)
                    {
                        double pz = z0 + k * DtCell;
                        double ceil = DeckNear(pz) + 1.0;
                        for (int i = i0; i <= i1; i++)
                        {
                            double px = x0 + i * DtCell;
                            double wa = ((b.Z - c.Z) * (px - c.X) + (c.X - b.X) * (pz - c.Z)) / den;
                            double wb = ((c.Z - a.Z) * (px - c.X) + (a.X - c.X) * (pz - c.Z)) / den;
                            double wc = 1 - wa - wb;
                            if (wa < 0 || wb < 0 || wc < 0) continue;
                            float y = (float)(wa * a.Y + wb * b.Y + wc * c.Y);
                            if (y > ceil) continue;
                            int q = k * nx + i;
                            if (y > g[q]) g[q] = y;
                        }
                    }
                }
            }
        }
        /* LES TROUS (un panneau ouvert, une maille qu'aucun triangle ne couvre au
           bon endroit) prennent la hauteur de leurs voisines : un poisson ne doit
           pas tomber à travers le pont faute d'un triangle. */
        for (int pass = 0; pass < 12; pass++)
        {
            bool any = false;
            var copy = (float[])g.Clone();
            for (int k = 0; k < nz; k++)
                for (int i = 0; i < nx; i++)
                {
                    int q = k * nx + i;
                    if (!float.IsNegativeInfinity(copy[q])) continue;
                    float best = float.NegativeInfinity;
                    if (i > 0) best = Math.Max(best, copy[q - 1]);
                    if (i < nx - 1) best = Math.Max(best, copy[q + 1]);
                    if (k > 0) best = Math.Max(best, copy[q - nx]);
                    if (k < nz - 1) best = Math.Max(best, copy[q + nx]);
                    if (!float.IsNegativeInfinity(best)) { g[q] = best; any = true; }
                }
            if (!any) break;
        }
        for (int q = 0; q < g.Length; q++)
            if (float.IsNegativeInfinity(g[q])) g[q] = (float)Spec.DeckMid;
        _deckTop = g; _dtNx = nx; _dtNz = nz; _dtX0 = x0; _dtZ0 = z0;
    }
}
