using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE TRÉSOR QUI S'EN VA — demandé : des pièces d'or qui descendent doucement et
/// disparaissent dans le fond.
///
/// Une pièce ne tombe pas comme une pierre : elle est plate, l'eau la porte par
/// le travers, et elle descend en VOLTIGEANT — elle glisse d'un bord, bascule,
/// glisse de l'autre, à quelques dizaines de centimètres par seconde. C'est ce
/// balancement qui la fait lire comme de l'or dans l'eau plutôt que comme une
/// étincelle qui tombe : à chaque bascule, sa face passe à plat dans la lumière
/// et jette un éclat, puis se perd de profil.
///
/// ELLE A UNE TRANCHE. Un panneau plat, vu de profil, disparaît d'un coup : on
/// ne lit plus une pièce mais un confetti. Le maillage de repli est donc un
/// cylindre très bas — un vrai disque épais, dont la tranche reste visible et ne
/// prend pas la lumière comme les faces.
///
/// Elle s'éteint en descendant — l'eau lui mange sa couleur bien avant qu'elle
/// touche un fond, qu'il n'y a d'ailleurs pas encore.
/// </summary>
public partial class CoinNode : Node3D
{
    const int Max = 600;

    /// <summary>Son épaisseur, en fraction du diamètre. Un écu est plus mince ; celle-ci se voit.</summary>
    const float Thick = 0.14f;

    /// <summary>Le modèle que le joueur peut fournir, à défaut de quoi le disque procédural.</summary>
    public const string Glb = "props/ecu.glb";

    struct Coin
    {
        public Vector3 From;
        public float Sink, Age, Life, Size, Seed, Flutter;
        public bool On;
    }

    readonly Coin[] _c = new Coin[Max];
    int _next;
    MultiMesh _mm = null!;
    readonly RandomNumberGenerator _rng = new();
    public ShaderMaterial Material { get; private set; } = null!;

    public override void _Ready()
    {
        Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/coins.gdshader") };
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = LoadCoin() ?? Disc(),
            InstanceCount = Max,
            VisibleInstanceCount = 0
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _mm, MaterialOverride = Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 200
        });
    }

    /// <summary>Le disque de repli : diamètre 1, axe +y, assez de facettes pour que la tranche tourne rond.</summary>
    static Mesh Disc() => new CylinderMesh
    {
        TopRadius = 0.5f, BottomRadius = 0.5f, Height = Thick,
        RadialSegments = 14, Rings = 0
    };

    /// <summary>
    /// L'ÉCU EN .glb, s'il existe — même convention que les coques : chargé à
    /// l'exécution depuis le dossier du projet, pas importé par l'éditeur. Il doit
    /// être posé À PLAT, sa face vers +y, et il est ramené au diamètre 1 : c'est
    /// le tirage qui décide ensuite de sa taille. Un MultiMesh ne porte qu'un
    /// maillage, donc on prend le premier qu'on trouve.
    /// </summary>
    Mesh? LoadCoin()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            Assets.Root, Glb));
        if (!System.IO.File.Exists(path)) return null;
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D obj)
        {
            GD.PushWarning($"{Glb} illisible — les pièces gardent leur disque.");
            return null;
        }
        var mi = FirstMesh(obj);
        if (mi?.Mesh == null) { obj.QueueFree(); return null; }

        /* Ramené au diamètre 1 et centré, sa texture reprise s'il en a une : le
           shader s'en sert alors à la place de l'or uni. L'échelle est cuite dans
           le maillage plutôt que posée sur chaque instance, dont la
           transformation porte déjà la voltige. */
        var box = mi.Mesh.GetAabb();
        float d = Mathf.Max(0.001f, Mathf.Max(box.Size.X, box.Size.Z));
        var arr = mi.Mesh.SurfaceGetArrays(0);
        var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var mid = box.Position + box.Size * 0.5f;
        for (int i = 0; i < verts.Length; i++) verts[i] = (verts[i] - mid) / d;
        arr[(int)Mesh.ArrayType.Vertex] = verts;
        var flat = new ArrayMesh();
        flat.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        if (mi.Mesh.SurfaceGetMaterial(0) is BaseMaterial3D bm && bm.AlbedoTexture != null)
        {
            Material.SetShaderParameter("u_tex", bm.AlbedoTexture);
            Material.SetShaderParameter("u_textured", true);
        }
        obj.QueueFree();
        GD.Print($"pièces : {Glb} chargé ({verts.Length} sommets)");
        return flat;
    }

    static MeshInstance3D? FirstMesh(Node n)
    {
        if (n is MeshInstance3D m) return m;
        foreach (var c in n.GetChildren())
            if (FirstMesh(c) is MeshInstance3D f) return f;
        return null;
    }

    /// <summary>
    /// Ce qu'une coque perd en sombrant : <paramref name="n"/> pièces semées le
    /// long d'elle, sous sa flottaison.
    /// </summary>
    public void Spill(Vector3 at, double length, double beam, int n)
    {
        for (int i = 0; i < n; i++)
        {
            ref Coin c = ref _c[_next];
            _next = (_next + 1) % Max;
            c.On = true;
            // elles ne partent pas toutes ensemble : la cale s'ouvre par à-coups
            c.Age = -_rng.Randf() * 6f;
            c.Life = 26 + _rng.Randf() * 22;
            // vingt à quarante-cinq centimètres par seconde : une pièce plate tombe lentement
            c.Sink = 0.20f + _rng.Randf() * 0.25f;
            c.Size = 0.12f + _rng.Randf() * 0.10f;
            c.Seed = _rng.Randf() * 100;
            c.Flutter = 0.5f + _rng.Randf() * 1.2f;
            /* Semées SERRÉ, vers le milieu : l'or est dans la cale, pas réparti
               d'un bout à l'autre du navire, et une nuée dense se lit — une
               poignée de points épars sur trente mètres ne se lit pas. Le tirage
               au carré ramène vers le centre. */
            float r = _rng.Randf(); r *= r;
            float ang = _rng.Randf() * Mathf.Tau;
            c.From = at + new Vector3(
                Mathf.Cos(ang) * r * (float)beam * 0.5f,
                -(float)(0.4 + _rng.Randf() * 1.6),
                Mathf.Sin(ang) * r * (float)length * 0.28f);
        }
    }

    /// <summary>Une image : elles descendent, voltigent, et s'éteignent.</summary>
    public void Step(double dt)
    {
        int live = 0;
        for (int i = 0; i < Max; i++)
        {
            ref Coin c = ref _c[i];
            if (!c.On) continue;
            c.Age += (float)dt;
            if (c.Age > c.Life) { c.On = false; continue; }
            if (c.Age < 0) continue;

            float a = c.Age;
            float phase = a * c.Flutter + c.Seed;
            // la voltige : elle glisse d'un bord puis de l'autre en basculant
            var p = new Vector3(
                c.From.X + Mathf.Sin(phase) * 0.45f,
                c.From.Y - c.Sink * a,
                c.From.Z + Mathf.Sin(phase * 0.7f + 1.3f) * 0.35f);
            /* Et elle bascule autour de son diamètre — l'axe de la pièce est +y,
               celui du cylindre : à plat elle montre sa face, en travers sa
               tranche. C'est ce passage qui fait l'éclat. */
            var basis = new Basis(Vector3.Up, phase * 1.6f) * new Basis(Vector3.Right, Mathf.Sin(phase) * 1.4f);
            _mm.SetInstanceTransform(live, new Transform3D(basis.Scaled(Vector3.One * c.Size), p));
            float fade = Mathf.Min(1, (c.Life - a) / 6f) * Mathf.Min(1, a / 0.5f);
            _mm.SetInstanceCustomData(live, new Color(c.Seed, fade, 0, 1));
            live++;
            if (live >= Max) break;
        }
        _mm.VisibleInstanceCount = live;
    }

    /// <summary>L'origine flottante.</summary>
    public void Rebase(double dx, double dz)
    {
        for (int i = 0; i < Max; i++)
            if (_c[i].On) _c[i].From += new Vector3((float)dx, 0, (float)dz);
    }
}
