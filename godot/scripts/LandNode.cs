using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA TERRE, DESSINÉE.
///
/// EN CARREAUX, parce que la terre est une côte et non quatre îles : une grille
/// carrée en mètres VRAIS du monde, chaque carreau bâti la première fois qu'il
/// arrive à portée et gardé ensuite — le relief ne change jamais. Seuls les
/// carreaux qui portent de la terre ou un haut-fond sont bâtis ; la haute mer
/// est le shader de la mer. Près d'elle un carreau est dessiné à la résolution
/// de l'image, plus loin au quart, et rien ne pend sous leurs bords : voir plus
/// bas pourquoi la jupe de la page a été retirée des deux côtés.
///
/// L'essentiel reste le PLACEMENT. Chaque carreau est bâti dans SON PROPRE
/// repère et simplement POSÉ à <c>carreau − origine</c> à chaque image :
/// l'origine flottante peut glisser tant qu'elle veut, déplacer un carreau est
/// une affectation de vecteur. Rien ici n'a donc de Rebase — c'est le monde qui
/// est fixe et le repère local qui bouge.
/// </summary>
public partial class LandNode : Node3D
{
    public readonly World World;

    /// <summary>Jusqu'où la terre est dessinée, en mètres.</summary>
    public double Range = 14000;
    /// <summary>En deçà, le plein détail.</summary>
    public double NearRange = 4500;
    /// <summary>Le plus fin qu'un carreau se bâtit : 1440 m en 288 vaut cinq mètres.</summary>
    public int FineMax = 288;
    /// <summary>Le côté d'un carreau : 32 pixels du relief au départ.</summary>
    public double Tile = 1440;
    /// <summary>La lumière de la houle rassemblée sur le fond (settings.json → caustics).</summary>
    public CausticSettings CausticRules = new();

    /// <summary>Les matériaux que <see cref="SkyNode.PushTo"/> doit tenir à jour.</summary>
    public readonly List<ShaderMaterial> Hazed = new();

    /// <summary>
    /// LA MATIÈRE DU RELIEF — et, avec elle, les caustiques du fond. Elle lit la
    /// MÊME mer que la surface : la démo lui pousse le spectre à chaque image
    /// (<see cref="OceanNode.PushWaves"/>), le soleil, les rides et le havre.
    /// </summary>
    public ShaderMaterial Ground { get; private set; } = null!;

    sealed class Patch
    {
        public bool Has;
        public MeshInstance3D? Near, Far;
    }

    readonly Dictionary<(int, int), Patch> _tiles = new();
    readonly HashSet<MeshInstance3D> _seen = new();
    ShaderMaterial _mat = null!;

    public LandNode(World world)
    {
        World = world;
    }

    public override void _Ready()
    {
        /* Du sable au bord de l'eau qui monte en herbe, en forêt et en roche.
           Couleurs de SOMMET plutôt qu'une texture : la bande à laquelle un
           point appartient est une fonction de sa hauteur, et rien à charger.
           Color8 et non Color : le constructeur de Godot prend des FLOTTANTS, et
           0xc9 y valait 201, donc une côte blanche à souhait.

           SON PROPRE SHADER, ET NON UNE StandardMaterial3D, depuis que le fond
           porte les caustiques : la lumière rassemblée par la houle MULTIPLIE ce
           qui repart du sable, et une seconde passe multiplicative n'est pas
           dessinée sous Forward+ (mesuré ; voir land.gdshader). Il ne fait rien
           d'autre que ce que faisait la matière standard — l'albédo des sommets,
           mat, éclairé par le moteur. */
        _mat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/land.gdshader"),
            // l'air devant tout le reste, la MÊME passe que la coque porte
            NextPass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") }
        };
        Ground = _mat;
        Hazed.Add((ShaderMaterial)_mat.NextPass);

        // les cadrans une fois pour toutes ; le spectre et le soleil, à chaque image
        _mat.SetShaderParameter("u_caustic_gain", CausticRules.Enabled ? (float)CausticRules.Gain : 0f);
        _mat.SetShaderParameter("u_caustic_depth", (float)CausticRules.Depth);
        _mat.SetShaderParameter("u_caustic_far", (float)CausticRules.Far);
        _mat.SetShaderParameter("u_caustic_floor", (float)CausticRules.Floor);
        _mat.SetShaderParameter("u_caustic_spread", (float)CausticRules.Spread);

    }

    /* Les teintes de la page, en sRGB. Les couleurs de sommet de Godot sont
       prises pour LINÉAIRES : sans la conversion, la côte sortirait délavée. */
    static readonly Color Sand = Color.Color8(0xc9, 0xb1, 0x83).SrgbToLinear();
    static readonly Color Grass = Color.Color8(0x4f, 0x6a, 0x3a).SrgbToLinear();
    static readonly Color Wood = Color.Color8(0x34, 0x50, 0x2c).SrgbToLinear();
    static readonly Color Rock = Color.Color8(0x6c, 0x66, 0x5c).SrgbToLinear();

    static Color Tint(double h)
    {
        if (h < 3) return Sand;
        if (h < 18) return Sand.Lerp(Grass, (float)((h - 3) / 15));
        if (h < 120) return Grass.Lerp(Wood, (float)((h - 18) / 102));
        if (h < 700) return Wood.Lerp(Rock, (float)(Math.Min(1, (h - 120) / 580) * 0.7));
        return Wood.Lerp(Rock, (float)(0.7 + 0.3 * Math.Min(1, (h - 700) / 600)));
    }

    /// <summary>Ce carreau porte-t-il quelque chose qui vaille d'être dessiné ? Un regard grossier, une fois.</summary>
    bool Worth(int i, int j)
    {
        for (int a = 0; a <= 8; a++)
            for (int b = 0; b <= 8; b++)
                if (World.HeightAt((i + a / 8.0) * Tile, (j + b / 8.0) * Tile) > -20) return true;
        return false;
    }

    MeshInstance3D Build(int i, int j, int n)
    {
        var pos = new List<Vector3>((n + 1) * (n + 1));
        var col = new List<Color>(pos.Capacity);
        var idx = new List<int>(n * n * 6);
        double x0 = i * Tile, z0 = j * Tile;
        for (int b = 0; b <= n; b++)
            for (int a = 0; a <= n; a++)
            {
                double x = (double)a / n * Tile, z = (double)b / n * Tile;
                double h = World.HeightAt(x0 + x, z0 + z);
                pos.Add(new Vector3((float)x, (float)h, (float)z));
                col.Add(Tint(h));
            }
        for (int b = 0; b < n; b++)
            for (int a = 0; a < n; a++)
            {
                int k = b * (n + 1) + a;
                idx.Add(k); idx.Add(k + n + 1); idx.Add(k + n + 2);
                idx.Add(k); idx.Add(k + n + 2); idx.Add(k + 1);
            }

        /* PAS DE JUPE, et c'est un écart ASSUMÉ avec la page — qui en avait une,
           et qui vient d'en être débarrassée elle aussi.

           Un rideau qui pend sous l'arête d'un carreau ne peut pas ne pas se
           voir : vu depuis le bas d'une pente, il masque le pied du versant d'en
           face sur toute sa hauteur. Trente mètres à trois kilomètres font six
           pixels, et cela donnait un réseau de rubans en travers du paysage —
           signalé à l'écran, puis identifié en peignant la jupe en rouge.

           Elle ne servait d'ailleurs qu'aux fentes entre un carreau fin et un
           grossier, or cette frontière est à 4,5 km, où la brume éteint déjà
           98 % du contraste (1 − exp(−0,00085 × 4500)). Rien ne s'y voit, pas
           même une fente. Deux carreaux de MÊME finesse, eux, partagent leur
           arête au bit près : il n'y a jamais eu de fente entre eux. */

        /* Les NORMALES calculées ici, et par la fonction du noyau : SurfaceTool
           les aurait faites aussi, mais son aller-retour perdait les couleurs de
           sommet en chemin — une côte entièrement blanche, vue à la capture. Un
           maillage livré complet d'un coup ne peut pas perdre un attribut. */
        var vert = new float[pos.Count * 3];
        for (int k = 0; k < pos.Count; k++)
        {
            vert[k * 3] = pos[k].X; vert[k * 3 + 1] = pos[k].Y; vert[k * 3 + 2] = pos[k].Z;
        }
        var nrm = new float[vert.Length];
        var tri = idx.ToArray();
        NavalSim.Core.SailCloth.ComputeNormals(vert, nrm, tri);

        /* LE SENS DES TRIANGLES, RETOURNÉ — la même règle que la toile et les
           pavillons, et la côte était la seule à l'avoir manquée : three.js tient
           pour face avant le sens direct, Godot le sens HORAIRE. Godot voyait
           donc tout le relief par l'envers et le supprimait dès qu'on le
           regardait d'en haut : il ne restait qu'une mer plate avec des maisons
           posées dessus, ce qui se lisait comme une terre noyée. Les normales
           sont calculées AVANT l'échange — c'est la même surface, on ne fait que
           dire à Godot de quel côté elle regarde. */
        for (int t = 0; t + 2 < tri.Length; t += 3) (tri[t + 1], tri[t + 2]) = (tri[t + 2], tri[t + 1]);
        var normals = new Vector3[pos.Count];
        for (int k = 0; k < pos.Count; k++)
            normals[k] = new Vector3(nrm[k * 3], nrm[k * 3 + 1], nrm[k * 3 + 2]);

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = pos.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = normals;
        arr[(int)Mesh.ArrayType.Color] = col.ToArray();
        arr[(int)Mesh.ArrayType.Index] = tri;
        var final = new ArrayMesh();
        final.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);

        var mi = new MeshInstance3D
        {
            Mesh = final,
            MaterialOverride = _mat,
            // l'ombre portée d'une côte ne vaut pas sa carte d'ombres
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(mi);
        return mi;
    }

    /// <summary>
    /// Montrer ce qui est à portée, cacher le reste, et tout poser contre
    /// l'origine du moment. <paramref name="centre"/> est sa position VRAIE.
    /// </summary>
    public void Update(Vec3d centre, Vec3d origin, bool eager = false)
    {
        int i0 = (int)Math.Floor((centre.X - Range) / Tile), i1 = (int)Math.Floor((centre.X + Range) / Tile);
        int j0 = (int)Math.Floor((centre.Z - Range) / Tile), j1 = (int)Math.Floor((centre.Z + Range) / Tile);
        _seen.Clear();
        int built = 0;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double cx = (i + 0.5) * Tile, cz = (j + 0.5) * Tile;
                double d = Math.Sqrt((cx - centre.X) * (cx - centre.X) + (cz - centre.Z) * (cz - centre.Z));
                if (d > Range + Tile * 0.71) continue;
                if (!_tiles.TryGetValue((i, j), out var t))
                    _tiles[(i, j)] = t = new Patch { Has = Worth(i, j) };
                if (!t.Has) continue;

                bool fine = d < NearRange + Tile * 0.71;
                /* Quelques-uns par image au plus — vingt carreaux dans la même
                   image font un à-coup que l'œil attrape —, sauf quand on demande
                   d'être PRESSÉ, au départ, pour qu'elle ne sorte pas d'un port
                   dont la rive d'en face est encore à venir. */
                if (fine && t.Near == null)
                {
                    if (!eager && built++ > 3) continue;
                    /* AUSSI FIN QUE LA SOURCE, et pas plus : un carreau bâti plus
                       fin que le relief qui le nourrit n'ajoute que des triangles
                       qui interpolent. Un relief LOCAL (un port modelé à la main)
                       vaut donc des carreaux serrés, bornés à FineMax pour qu'une
                       rade ne coûte pas un million de sommets. */
                    double px = World.ReliefPx(i * Tile, j * Tile, (i + 1) * Tile, (j + 1) * Tile);
                    int n = (int)Math.Round(Tile / px);
                    t.Near = Build(i, j, Math.Clamp(n, 8, FineMax));
                }
                if (!fine && t.Far == null)
                {
                    if (!eager && built++ > 3) continue;
                    t.Far = Build(i, j, Math.Max(4, (int)Math.Round(Tile / World.Px / 4)));
                }
                var show = fine ? t.Near : t.Far;
                var hide = fine ? t.Far : t.Near;
                if (hide != null) hide.Visible = false;
                if (show == null) continue;
                show.Visible = true;
                show.Position = new Vector3((float)(i * Tile - origin.X), 0, (float)(j * Tile - origin.Z));
                _seen.Add(show);
            }
        foreach (var t in _tiles.Values)
        {
            if (t.Near != null && !_seen.Contains(t.Near)) t.Near.Visible = false;
            if (t.Far != null && !_seen.Contains(t.Far)) t.Far.Visible = false;
        }
    }
}
