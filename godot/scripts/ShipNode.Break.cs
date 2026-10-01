using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA COQUE ROMPUE PAR SA SOUTE — ce qu'on voit. Le solveur se coupe dans
/// ShipPhysics.Split ; ici, le bois.
///
/// L'ARRIÈRE RESTE CE NAVIRE : son modèle est recoupé sur place, il garde ses
/// feux, ses couleurs, sa rangée dans la flotte. L'AVANT est une COPIE du modèle,
/// recoupée de l'autre côté, pendue à un nœud que le moteur fait suivre au corps
/// de la moitié avant. Les deux partent du même repère, puisque les deux moitiés
/// du solveur partent de la même pose.
/// </summary>
public partial class ShipNode
{
    /// <summary>Rompue : il ne reste d'elle que l'arrière.</summary>
    public bool Broken { get; private set; }
    /// <summary>Où elle s'est rompue, le long d'elle : l'épave inscrite gît en deux tronçons coupés là.</summary>
    public float BreakZ { get; private set; }

    /// <summary>
    /// La rompre à <paramref name="zCut"/> (repère du navire). <paramref name="bowHolder"/>
    /// doit déjà être dans l'arbre, à la même pose qu'elle : il reçoit la copie de
    /// l'avant, ses mâts tombés et sa tranche. Rend la matière des deux tranches
    /// (pour que le moteur en éteigne les braises), ou null si rien ne se coupe.
    /// </summary>
    public StandardMaterial3D? Break(float zCut, Node3D bowHolder)
    {
        Node3D? root = ModelRoot ?? (IsInstanceValid(_hull) ? _hull : null);
        if (root == null || Broken) return null;
        var toShip = root.Transform;

        /* LA COPIE D'ABORD, avant qu'on touche à l'original : elle partage ses
           maillages, et la coupe de l'arrière remplace les siens par des neufs. */
        var dup = (Node3D)root.Duplicate();
        bowHolder.AddChild(dup);
        dup.Transform = toShip;

        /* SES MATIÈRES, SANS CE QUI EST À ELLE. Une matière de coque porte en
           chaîne les blessures, la neige et la brume DE CE NAVIRE ; les blessures
           se lisent dans le repère de l'arrière, qui ne va plus où va l'avant.
           L'avant garde le bois et la brume (qui ne dépend que du ciel) et laisse
           le reste. */
        var cache = new Dictionary<Material, Material>();
        Material? Strip(Material? m)
        {
            if (m == null) return null;
            if (cache.TryGetValue(m, out var done)) return done;
            var c = (Material)m.Duplicate();
            if (m is BaseMaterial3D) c.NextPass = HazeIn(m.NextPass);
            cache[m] = c;
            return c;
        }

        var aftCut = HullCut.Cut(root, toShip, zCut, keepFront: false);
        var bowCut = HullCut.Cut(dup, toShip, zCut, keepFront: true, Strip);

        /* LES MÂTS DE L'AVANT PARTENT AVEC LUI, tombés ou debout : chacun pend à
           son pied, et c'est la cote de ce pied qui dit de quel côté il est. Ils
           gardent leur pose dans le monde ; la chute qui les couchait continue
           (StepRigging tient leur nœud, pas leur parent). */
        foreach (var d in _damage)
            if (IsInstanceValid(d.Fall) && d.Fall.Position.Z > zCut)
                d.Fall.Reparent(bowHolder, true);

        var mat = CharMaterial();
        AddChild(Cap(aftCut, zCut, +1, mat));
        bowHolder.AddChild(Cap(bowCut, zCut, -1, mat));
        Broken = true;
        BreakZ = zCut;
        return mat;
    }

    /// <summary>La passe de brume d'une chaîne, si elle en porte une : la seule qu'une copie peut partager.</summary>
    static Material? HazeIn(Material? m)
    {
        for (; m != null; m = m.NextPass)
            if (m is ShaderMaterial sm && sm.Shader?.ResourcePath.Contains("hull_haze") == true) return m;
        return null;
    }

    /// <summary>
    /// LE BOIS DE LA TRANCHE : noir de la flamme, et des braises dedans. Elles
    /// émettent — donc ne suivent pas la lumière, comme une flamme — et le moteur
    /// les éteint en une vingtaine de secondes. Le grain des braises vient d'un
    /// bruit et non d'une texture : ce que le code peut dessiner, il le dessine.
    /// </summary>
    StandardMaterial3D CharMaterial()
    {
        var noise = new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true,
            Noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.045f, Seed = (int)(GD.Randi() & 0x7fff) },
            ColorRamp = new Gradient
            {
                Offsets = new[] { 0f, 0.55f, 0.8f, 1f },
                Colors = new[] { Colors.Black, Colors.Black, new Color(0.6f, 0.6f, 0.6f), Colors.White }
            }
        };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.055f, 0.04f, 0.03f),
            Roughness = 1f,
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.32f, 0.07f),
            EmissionTexture = noise,
            EmissionEnergyMultiplier = 3f,
            Uv1Scale = new Vector3(0.35f, 0.35f, 1)
        };
        var haze = HazeIn(_hazePass);
        if (haze != null) mat.NextPass = haze;
        return mat;
    }

    /// <summary>
    /// LA TRANCHE, bouchée d'après le PLAN DE FORMES — la section de la coque à
    /// la cote de la coupe —, puis posée sur le bois qu'on voit : les points où la
    /// coque du modèle franchit le plan donnent sa largeur et sa hauteur réelles,
    /// et la section y est mise à l'échelle. Un rien en retrait, et le bord
    /// déchiqueté : une coque ne se rompt pas au cordeau, et une tranche lisse
    /// trahirait le plan qui l'a taillée.
    /// </summary>
    MeshInstance3D Cap(List<Vector3> seen, float zCut, int facing, Material mat)
    {
        double L = Spec.L, tz = Math.Clamp((zCut + L / 2) / L, 0.02, 0.98);
        double hb = Math.Max(0.1, Lines.HalfB(tz)), deck = Lines.DeckY(tz), keel = Lines.KeelY(tz);

        // la section, du pont à la quille sur un bord, puis remontée sur l'autre
        const int N = 14;
        var ring = new List<Vector2>();
        for (int i = 0; i <= N; i++) { double s = i / (double)N; ring.Add(new Vector2((float)(hb * Lines.BeamFactor(s)), (float)(deck - s * (deck - keel)))); }
        for (int i = N; i >= 0; i--) { double s = i / (double)N; ring.Add(new Vector2((float)(-hb * Lines.BeamFactor(s)), (float)(deck - s * (deck - keel)))); }

        // mise à l'échelle sur ce que le modèle montre, s'il a été touché
        if (seen.Count >= 6)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var p in seen) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); }
            float cx = (x0 + x1) * 0.5f, sx = (x1 - x0) * 0.5f / (float)hb, sy = (y1 - y0) / (float)(deck - keel);
            for (int i = 0; i < ring.Count; i++)
                ring[i] = new Vector2(cx + ring[i].X * sx, y0 + (ring[i].Y - (float)keel) * sy);
        }

        Vector2 c = Vector2.Zero;
        foreach (var p in ring) c += p;
        c /= ring.Count;
        var rng = new RandomNumberGenerator { Seed = (ulong)(Spec.Id.GetHashCode() & 0x7fffffff) + (ulong)(facing + 2) };
        for (int i = 0; i < ring.Count; i++)
            ring[i] = c + (ring[i] - c) * (0.975f - 0.07f * rng.Randf());     // en retrait, et déchiqueté

        /* LES DEUX FACES, chacune son triangle et sa normale : vue de la brèche,
           c'est du bois ; vue de dedans, aussi. Moins de surprises qu'une matière
           sans élimination des faces, dont l'envers prend la lumière à rebours. */
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int side = 0; side < 2; side++)
        {
            var n = new Vector3(0, 0, side == 0 ? facing : -facing);
            for (int i = 0; i + 1 < ring.Count; i++)
            {
                Vector2 a = ring[i], b = ring[i + 1];
                bool flip = (side == 0) == (facing > 0);
                foreach (var p in flip ? new[] { c, b, a } : new[] { c, a, b })
                {
                    st.SetNormal(n);
                    st.SetUV(new Vector2(p.X, p.Y));
                    st.AddVertex(new Vector3(p.X, p.Y, zCut + facing * 0.02f));
                }
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = mat, Name = "tranche" };
    }
}
