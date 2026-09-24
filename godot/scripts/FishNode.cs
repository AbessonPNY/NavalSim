using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES BANCS DE POISSONS DES HAUTS-FONDS — <c>creatures/fish.glb</c>, instancié.
///
/// LE PRINCIPE EST DE NE RIEN FAIRE. Un banc est un <see cref="MultiMesh"/> dont
/// les transformations d'instance ne changent JAMAIS après sa création : chaque
/// poisson porte quatre nombres (phase, rayon de ronde, hauteur, taille) et le
/// shader en tire à chaque image sa position, son cap et la flexion de son
/// corps. Vingt-six poissons coûtent donc un appel de dessin et pas une ligne
/// de C# par image. C'était la condition posée — de la vie sous l'eau sans
/// toucher aux images par seconde.
///
/// LES BANCS SONT GARDÉS EN MÈTRES VRAIS et simplement POSÉS à
/// <c>centre − origine</c> à chaque image, comme les carreaux de la terre :
/// l'origine flottante peut glisser tant qu'elle veut, déplacer un banc est une
/// affectation de vecteur. Rien ici n'a donc de Rebase.
///
/// Les nœuds sont créés une fois pour toutes, au nombre maximum de bancs, et
/// resservent : peupler un banc n'alloue rien, on ne fait que réécrire ses
/// données d'instance et le rendre visible.
/// </summary>
public partial class FishNode : Node3D
{
    public FishSettings Rules = new();

    /// <summary>Le matériau, pour que la démo lui pousse la mer et le soleil.</summary>
    public ShaderMaterial? Material { get; private set; }

    sealed class Shoal
    {
        public MultiMeshInstance3D Node = null!;
        public MultiMesh Mm = null!;
        public Vec3d Centre;          // en mètres VRAIS
        public bool Alive;
    }

    readonly List<Shoal> _shoals = new();
    readonly Random _rng = new();
    double _look;                     // le temps avant le prochain regard

    /// <summary>Lire le modèle et bâtir les bancs, vides. Faux s'il manque.</summary>
    public bool Build(string? path)
    {
        if (!Rules.Enabled || Rules.Shoals <= 0) return false;
        if (path == null || !System.IO.File.Exists(path))
        {
            GD.PushWarning($"poissons : modèle introuvable ({path})");
            return false;
        }
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D root)
        {
            GD.PushWarning($"poissons : {path} illisible");
            return false;
        }
        Mesh? mesh = null;
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0 && mesh == null)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null && mi.Mesh.GetSurfaceCount() > 0) mesh = mi.Mesh;
            foreach (var ch in n.GetChildren()) stack.Push(ch);
        }
        root.QueueFree();
        if (mesh == null) { GD.PushWarning($"poissons : aucune maille dans {path}"); return false; }

        /* LE MODÈLE EST RAMENÉ À LA TAILLE D'UN VRAI POISSON, sur son axe — la
           même convention que les coques, qui normalisent leur plus grosse maille
           à la longueur de la fiche. Un modeleur travaille à l'échelle qui lui
           plaît ; ce n'est pas au modèle de connaître le jeu. Les bornes vont au
           shader en unités du MODÈLE, et c'est lui qui met à l'échelle : ainsi un
           poisson peut être un peu plus gros que son voisin sans qu'on touche au
           maillage. */
        var box = mesh.GetAabb();
        double raw = Math.Max(box.Size.Z, 1e-4);
        _scale = Rules.Size / raw;
        /* Les bornes sont données DANS LE SENS OÙ LE SHADER TRAVAILLE : s'il doit
           faire faire demi-tour au modèle, la queue et le nez échangent leur
           place, et tout ce qui suit l'ignore. */
        float tail = Rules.Face < 0 ? -(box.Position.Z + box.Size.Z) : box.Position.Z;
        float nose = Rules.Face < 0 ? -box.Position.Z : box.Position.Z + box.Size.Z;

        Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/fish.gdshader") };
        Material.SetShaderParameter("u_fish_face", Rules.Face < 0 ? -1f : 1f);
        Material.SetShaderParameter("u_fish_tail", tail);
        Material.SetShaderParameter("u_fish_nose", nose);
        Material.SetShaderParameter("u_fish_half", Math.Max(box.Size.Y * 0.5f, 1e-4f));
        Material.SetShaderParameter("u_fish_speed", (float)Rules.Speed);
        Material.SetShaderParameter("u_fish_beat", (float)Rules.Beat);
        Material.SetShaderParameter("u_fish_sway", (float)Rules.Sway);
        Material.SetShaderParameter("u_fish_vary", (float)Rules.Vary);
        var tint = Color.FromString(Rules.Tint, new Color(0.86f, 0.62f, 0.26f)).SrgbToLinear();
        Material.SetShaderParameter("u_fish_tint", new Vector3(tint.R, tint.G, tint.B));

        for (int i = 0; i < Rules.Shoals; i++)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = Rules.PerShoal,
            };
            for (int k = 0; k < Rules.PerShoal; k++) mm.SetInstanceTransform(k, Transform3D.Identity);
            var node = new MultiMeshInstance3D
            {
                Multimesh = mm,
                MaterialOverride = Material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                /* SA BOÎTE À LA MAIN : le moteur ne peut pas la deviner, puisque
                   les instances sont toutes à l'origine et que c'est le shader qui
                   les disperse. Sans elle, le banc disparaît dès que son centre
                   sort du champ. */
                CustomAabb = new Aabb(
                    new Vector3(-(float)Rules.Spread - 1, -1.5f, -(float)Rules.Spread - 1),
                    new Vector3(2 * (float)Rules.Spread + 2, 3f, 2 * (float)Rules.Spread + 2)),
            };
            AddChild(node);
            _shoals.Add(new Shoal { Node = node, Mm = mm });
        }
        return true;
    }

    double _scale = 1;

    /// <summary>
    /// Les poser, les retirer, et les tenir contre l'origine du moment.
    /// <paramref name="here"/> et <paramref name="origin"/> sont en mètres VRAIS.
    /// </summary>
    public void Update(World world, Vec3d here, Vec3d origin, double dt, double seaY)
    {
        if (_shoals.Count == 0) return;

        _look -= dt;
        foreach (var b in _shoals)
        {
            if (b.Alive)
            {
                double dx = b.Centre.X - here.X, dz = b.Centre.Z - here.Z;
                // on les oublie de loin, avec une marge pour ne pas clignoter à la limite
                if (dx * dx + dz * dz > Rules.Range * Rules.Range * 2.9)
                {
                    b.Alive = false;
                    b.Node.Visible = false;
                    continue;
                }
                b.Node.Position = new Vector3(
                    (float)(b.Centre.X - origin.X), (float)b.Centre.Y, (float)(b.Centre.Z - origin.Z));
            }
            else if (_look <= 0)
            {
                _look = 1.0;
                if (Seed(world, here, seaY) is Vec3d c)
                {
                    b.Centre = c;
                    b.Alive = true;
                    Fill(b);
                    b.Node.Position = new Vector3(
                        (float)(c.X - origin.X), (float)c.Y, (float)(c.Z - origin.Z));
                    b.Node.Visible = true;
                }
            }
        }
    }

    /// <summary>
    /// UN FOND QUI LEUR AILLE : assez d'eau pour nager, assez peu pour qu'on les
    /// voie. Seize essais au hasard dans une couronne autour du navire, et tant
    /// pis s'il n'y en a pas — en haute mer il ne doit RIEN se passer, et c'est
    /// le cas le plus fréquent : seize lectures du relief par seconde, qui ne
    /// coûtent rien, contre un banc qui pousserait au large.
    /// </summary>
    Vec3d? Seed(World world, Vec3d here, double seaY)
    {
        for (int t = 0; t < 16; t++)
        {
            double a = _rng.NextDouble() * Math.Tau;
            double r = 35 + _rng.NextDouble() * (Rules.Range - 35);
            double x = here.X + Math.Cos(a) * r, z = here.Z + Math.Sin(a) * r;
            double bed = world.HeightAt(x, z);
            double depth = seaY - bed;
            if (depth < Rules.MinDepth || depth > Rules.MaxDepth) continue;
            /* À MI-EAU, un peu plus près du fond : c'est là que se tient un banc de
               récif, et cela laisse la garde nécessaire sous la quille de tout ce
               qui passe. */
            double y = bed + depth * 0.42;
            _water = depth;
            return new Vec3d(x, y, z);
        }
        return null;
    }

    /// <summary>La hauteur d'eau du dernier endroit retenu, en mètres.</summary>
    double _water = 3;

    /// <summary>Peupler un banc : quatre nombres par poisson, et plus rien après.</summary>
    void Fill(Shoal b)
    {
        /* L'ÉPAISSEUR DU BANC EST CELLE DE L'EAU QUI RESTE. Dans un mètre vingt,
           un banc étalé sur trois mètres de haut sortirait par le dessus à la
           première vague et rentrerait dans le sable par le dessous. */
        double thick = Math.Clamp(_water * 0.25, 0.12, 1.6);
        int n = Rules.PerShoal;
        for (int k = 0; k < n; k++)
        {
            /* LES RAYONS SONT TIRÉS EN RACINE pour que les poissons se répartissent
               dans le DISQUE et non vers son centre — un tirage uniforme du rayon
               en entasse la moitié dans le quart intérieur. */
            double r = Rules.Spread * (0.18 + 0.82 * Math.Sqrt(_rng.NextDouble()));
            double lift = (_rng.NextDouble() - 0.5) * 2 * thick;
            double size = _scale * (0.78 + 0.44 * _rng.NextDouble());
            b.Mm.SetInstanceCustomData(k, new Color(
                (float)_rng.NextDouble(), (float)r, (float)lift, (float)size));
        }
        b.Mm.VisibleInstanceCount = n;
    }
}
