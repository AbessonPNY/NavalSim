using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Le gréement et la toile — _buildRig, _gaffRig, _squareRig, _jibRig,
/// _rigModel et setTrim de ship-model.js.
///
/// La toile elle-même (tissage, forme, normales, brassage) vit dans le noyau,
/// dans <see cref="SailCloth"/> et <see cref="BraceTrim"/>, que le banc de
/// parité compare à l'original. Ce qui reste ici est ce que seul le moteur sait
/// faire : les pivots, les espars, et reconnaître ceux d'un modèle importé.
/// </summary>
public partial class ShipNode
{
    /// <summary>Une voile à l'écran : sa toile (noyau) et son maillage (moteur).</summary>
    sealed class Canvas
    {
        public SailCloth Cloth = null!;
        public MeshInstance3D Node = null!;
        public ArrayMesh Mesh = null!;
        public Material Mat = null!;
        public Vector3[] V = null!, N = null!;
        public Vector2[] Uv = null!;
        public int[] Idx = null!;
        // le mât qui la porte (−1 : aucun qui tombe), et si elle a éclaté hors de ses ralingues
        public int Mast = -1;
        public bool Split;
        /// <summary>Ce que le feu lui a mangé, de 0 (intacte) à 1 (plus rien).</summary>
        public double Burn;
        /// <summary>Où les boulets l'ont percée, en coordonnées de sa toile (u, v).</summary>
        public readonly List<Vector2> Holes = new();
        // un seul tableau de surface par voile, rempli à chaque image : en créer un
        // neuf soixante fois par seconde et par voile, c'est de la mémoire à ramasser
        public readonly Godot.Collections.Array Arrays = NewArrays();
        LiveCloth? _live;
        public LiveCloth Live => _live ??= new LiveCloth(Arrays);
    }

    static Godot.Collections.Array NewArrays()
    {
        var a = new Godot.Collections.Array();
        a.Resize((int)Mesh.ArrayType.Max);
        return a;
    }

    static readonly StringName UBurnSeed = "u_burn_seed";

    /* UN VRAI BRASSAGE, et non un pas régulier. Le premier essai prenait le reste
       d un produit par un premier — ce qui, tant qu on ne déborde pas du modulo,
       revient à AJOUTER le même nombre à chaque voile (relevé : 0 · 2,16 · 4,33 ·
       6,49…). Le dessin changeait quand même, le bruit ne se répétant pas d une
       case à l autre, mais une progression n est pas un hasard et finirait par se
       voir sur une voilure nombreuse. */
    static float BurnSeed(int i)
    {
        double x = Math.Sin((i + 1) * 12.9898) * 43758.5453;
        return (float)(Math.Abs(x - Math.Floor(x)) * 40.0);
    }
    readonly List<Canvas> _canvases = new();
    /// <summary>La dernière heure où la toile a été formée : le creux passe à une vitesse, pas d'un coup.</summary>
    double? _lastTrimT;
    // les pivots que le brassage fait tourner ; le foc, à part, aux trois quarts
    readonly List<Node3D> _rigs = new();
    Node3D? _jibRig;
    readonly BraceTrim _trim = new();
    /* LA LATINE D'ARTIMON dessinée : son pivot autour du mât, son propre suivi
       d'angle (l'équipage la borde seul, voir ShipPhysics.Lateen), et l'entrée
       de dommage de son mât — elle tombe avec lui. */
    Node3D? _latPivot;
    readonly BraceTrim _latTrim = new();
    int _latMast = -1;
    ShaderMaterial? _canvasMat;
    readonly Dictionary<string, ShaderMaterial> _painted = new();

    /// <summary>
    /// Un mât tel qu'il a été dressé, dans le repère du nœud qui le porte : de quoi
    /// y pendre un feu. Pour un modèle, ce nœud est le groupe qui tomberait avec lui.
    /// </summary>
    readonly record struct MastAt(Node3D Parent, double Z, double Foot, double Height, double Radius);
    readonly List<MastAt> _masts = new();

    static string RepoRoot =>
        System.IO.Path.GetDirectoryName(ShipLibrary.Folder.TrimEnd('/', '\\')) ?? "";

    /// <summary>
    /// CE QUE LE NOM DIT. Deux pièces d'un modèle appartiennent à un mât sans
    /// qu'aucune mesure ne puisse le deviner : la VIGIE, qui en fait partie et
    /// tombe avec lui, et le CORDAGE, qui n'accompagne rien — il casse et s'en
    /// va. Le nom du maillage les désigne (« vigie… », « hune… », « cordage… »,
    /// « hauban… ») et le mât le plus proche en station les reçoit.
    /// </summary>
    readonly List<(MeshInstance3D Mi, int Mast)> _snapped = new();

    void NameParts(List<Part> parts)
    {
        foreach (var p in parts)
        {
            if (p.Taken) continue;
            string n = p.Mi.Name.ToString().ToLowerInvariant();
            bool lookout = n.Contains("vigie") || n.Contains("hune");
            bool line = n.Contains("cordage") || n.Contains("hauban");
            if (!lookout && !line) continue;
            int m = NearestMast(p.Mid.Z);
            if (m < 0) continue;
            p.Taken = true;
            if (lookout) p.Mi.Reparent(_damage[m].Fall, true);
            else _snapped.Add((p.Mi, m));
            RigLog.Add(FormattableString.Invariant(
                $"{(lookout ? "vigie" : "cordage")} « {p.Mi.Name} » au mat {m} (z {p.Mid.Z:F1})"));
        }
    }

    /// <summary>Le mât dont le pied est le plus proche de cette station, ou −1.</summary>
    int NearestMast(double z)
    {
        int best = -1; double near = double.MaxValue;
        for (int i = 0; i < _damage.Count; i++)
        {
            /* PAS LE BEAUPRÉ. Il est rangé à son TALON, qui est sous le mât de
               misaine — c est là qu il est étalingué —, si bien qu il devenait le
               plus proche de tout ce qui pend à la misaine : le cordage de misaine
               est passé du mât 1 au mât 4 et restait en place quand le mât tombait
               (signalé, régression). Un espar COUCHÉ n a pas de station : il en
               traverse une dizaine, et ce qui pend à la sienne pend à ce qui est
               DEBOUT là. */
            if (!_damage[i].HasPole || _damage[i].Pitch) continue;
            double d = Math.Abs(_damage[i].Fall.Position.Z - z);
            if (d < near) { near = d; best = i; }
        }
        return best;
    }

    /// <summary>Les cordages d'un mât qui s'en va : ils cassent, ils ne tombent pas.</summary>
    void SnapLines(int mast, bool gone)
    {
        foreach (var (mi, m) in _snapped)
            if (m == mast && IsInstanceValid(mi)) mi.Visible = !gone;
        /* ET CE QUI DÉPEND DE LUI PAR UN AUTRE BOUT (ShipNode.Ropes.cs) : une corde
           tient tant que TOUS les mâts qui la tiennent sont debout. */
        foreach (var (mi, masts) in _ropeDeps)
        {
            if (!masts.Contains(mast) || !IsInstanceValid(mi)) continue;
            bool standing = true;
            foreach (int k in masts) if (k >= 0 && k < _damage.Count && _damage[k].Down != null) standing = false;
            mi.Visible = standing;
        }
    }

    void ClearRig()
    {
        _snapped.Clear();
        _ropeDeps.Clear();
        _movers.Clear();
        _latPivot = null;
        _latYard = null;
        _latMast = -1;
        _canvases.Clear();
        _rigs.Clear();
        _masts.Clear();
        _damage.Clear();
        _tops = null;
        _jibRig = null;
    }

    // ------------------------------------------------------------------
    //  LE GRÉEMENT PROCÉDURAL — _buildRig
    // ------------------------------------------------------------------

    /// <summary>
    /// Les mâts de la fiche et leur gréement. Ils ne sont pas décoratifs : une
    /// coque seule ne montre pas son roulis, l'œil n'ayant aucune verticale à quoi
    /// comparer l'inclinaison.
    /// </summary>
    /* ------------------------------------------------------------------ */
    /*  LES AVIRONS D'UNE EMBARCATION                                      */
    /* ------------------------------------------------------------------ */

    sealed class Oar
    {
        public Node3D Pivot = null!;
        public Node3D Blade = null!;
        public int Side;                 // 0 bâbord, 1 tribord
        public float Sg;                 // +1 bâbord (+x), −1 tribord (−x)
        public float Th, Dip = 0.14f, Feather;
    }
    readonly List<Oar> _oars = new();

    /// <summary>
    /// UN AVIRON PAR TOLET : un fût et une pelle, qui pivotent sur le plat-bord.
    /// Rien ici ne tire — le solveur le fait, aux PELLES —, et <see cref="SetOars"/>
    /// ne lit que la phase de son coup : ce que l'œil voit nager est exactement ce
    /// qui la fait avancer.
    /// </summary>
    void BuildOars()
    {
        _oars.Clear();
        if (Spec.Oars is not NavalSim.Core.OarsSpec O) return;
        var L = Lines;
        double len = O.Length;
        var spar = MakeHullMaterial(ColorX.Hex(Spec.Appearance.Spar), 0.62f);
        var loomMesh = Cylinder(0.03, 0.04, len, 8);
        var bladeMesh = new BoxMesh { Size = new Vector3(0.8f, 0.022f, 0.16f) };
        for (int i = 0; i < O.Pairs; i++)
        {
            double z = Spec.L * (O.Pairs > 1 ? 0.12 - 0.26 * i / (O.Pairs - 1.0) : 0);
            double t = z / Spec.L + 0.5;
            for (int side = 0; side < 2; side++)
            {
                float sg = side == 0 ? 1 : -1;          // bâbord +x, tribord −x
                var pivot = new Node3D
                {
                    Position = new Vector3(sg * (float)(L.HalfB(t) * 0.97),
                                           (float)(L.DeckY(t) + 0.30 * (Spec.L / 24)), (float)z)
                };
                /* LE FÛT EST COUCHÉ LE LONG DE X, comme il est armé : un tiers en
                   dedans du tolet, deux au dehors. Le cylindre de Godot est debout
                   sur Y ; on le couche par la rotation du nœud qui le porte. */
                var loom = new MeshInstance3D
                {
                    Mesh = loomMesh, MaterialOverride = spar,
                    Position = new Vector3(sg * (float)(0.2 * len), 0, 0),
                    Rotation = new Vector3(0, 0, Mathf.Pi / 2)
                };
                var blade = new MeshInstance3D
                {
                    Mesh = bladeMesh, MaterialOverride = spar,
                    Position = new Vector3(sg * (float)(0.7 * len - 0.4), 0, 0)
                };
                pivot.AddChild(loom);
                pivot.AddChild(blade);
                _rig.AddChild(pivot);
                _oars.Add(new Oar { Pivot = pivot, Blade = blade, Side = side, Sg = sg });
            }
        }
    }

    /// <summary>
    /// BALANCER LES AVIRONS EN MESURE avec le coup du solveur : la pelle en avant
    /// et CARRÉE à l'attaque, tirée vers l'arrière dans l'eau, puis plumée et
    /// portée en avant au-dessus. Scier nage le même coup à l'envers. Laissés à
    /// eux-mêmes, ils sont tenus à plat, hors de l'eau.
    /// </summary>
    public void SetOars(double dt)
    {
        if (_oars.Count == 0) return;
        float k = (float)Math.Min(1, dt * 14);
        foreach (var o in _oars)
        {
            double inp = Physics.OarInput[o.Side], u = Physics.OarPhase[o.Side];
            float th = 0, dip = 0.14f, feather = 0;
            if (inp != 0)
            {
                float dir = inp > 0 ? 1 : -1;
                if (u < 0.45)
                {
                    float e = (float)((1 - Math.Cos(Math.PI * u / 0.45)) / 2);
                    th = dir * (-0.55f + 1.1f * e); dip = -0.17f; feather = Mathf.Pi / 2;
                }
                else
                {
                    float e = (float)((1 - Math.Cos(Math.PI * (u - 0.45) / 0.55)) / 2);
                    th = dir * (0.55f - 1.1f * e);
                    dip = 0.08f + 0.07f * (float)Math.Sin(Math.PI * (u - 0.45) / 0.55);
                }
            }
            o.Th += (th - o.Th) * k; o.Dip += (dip - o.Dip) * k; o.Feather += (feather - o.Feather) * k;
            o.Pivot.Rotation = new Vector3(0, o.Sg * o.Th, o.Sg * o.Dip);
            o.Blade.Rotation = new Vector3(o.Feather, 0, 0);
        }
    }

    void BuildRig()
    {
        _rig = new Node3D();
        AddChild(_rig);
        var spec = Spec;
        BuildOars();                                    // une chaloupe n'a que cela
        if (spec.Masts.Count == 0) return;              // un bâtiment à la seule machine
        double sc = spec.L / 24;                          // les espars grossissent avec elle
        var spar = MakeHullMaterial(ColorX.Hex(spec.Appearance.Spar), 0.62f);
        foreach (var m in spec.Masts)
        {
            /* CHAQUE MÂT DANS SON GROUPE, articulé à son pied — celui qui tombe.
               Dessinés d'abord à même le gréement, les mâts d'une coque sans
               modèle ne pouvaient pas tomber du tout : ni boulet, ni foudre, ni
               soute n'en abattaient un sur le cotre ou la goélette (relevé). Le
               mât, ses vergues ou sa bôme et sa toile passent dedans, et le
               dommage est le même que sur un modèle. */
            var fall = new Node3D { Position = new Vector3(0, (float)spec.DeckMid, (float)m.Z) };
            _rig.AddChild(fall);
            fall.AddChild(new MeshInstance3D
            {
                Mesh = Cylinder(0.10 * sc, 0.17 * sc, m.Height, 10),
                MaterialOverride = spar,
                Position = new Vector3(0, (float)(m.Height / 2), 0)
            });
            int index = _masts.Count, sails = _canvases.Count;
            _masts.Add(new MastAt(fall, 0, 0, m.Height, 0.17 * sc));
            var rig = spec.Rig.Type == "square" ? SquareRig(m, sc, spar) : GaffRig(m, sc, spar);
            rig.Reparent(fall, true);
            _rigs.Add(rig);
            double share = 0;
            for (int i = sails; i < _canvases.Count; i++) { _canvases[i].Mast = index; share += 1; }
            var cords = new List<CordAnchor>();
            foreach (int sx in new[] { -1, 1 })
                cords.Add(new CordAnchor(fall, new Vector3((float)(sx * Math.Min(1.4, 0.05 * m.Height)), (float)(m.Height * 0.78), 0),
                    Math.Max(4, Math.Min(10, 0.26 * m.Height))));
            _damage.Add(new MastDamage
            {
                // sa part de la toile : comme le carré de sa hauteur, faute de mieux sans modèle
                Fall = fall, Heel = spec.DeckMid, Share = share > 0 ? m.Height * m.Height : 0, HasPole = true, Height = m.Height, Cords = cords
            });
        }
        if (spec.Rig.Jib != null)
        {
            _jibRig = JibRig();
            _rigs.Add(_jibRig);
        }
    }

    static CylinderMesh Cylinder(double top, double bottom, double h, int seg) => new()
    {
        TopRadius = (float)top, BottomRadius = (float)bottom, Height = (float)h, RadialSegments = seg
    };

    /// <summary>
    /// Voile aurique sur une bôme qui pivote. Le groupe tourne autour du mât :
    /// border ou choquer fait tourner la bôme et sa toile ensemble.
    /// </summary>
    Node3D GaffRig(MastSpec m, double sc, Material spar)
    {
        var spec = Spec;
        double tackY = spec.DeckMid + m.TackAbove;
        var rig = new Node3D { Position = new Vector3(0, 0, (float)m.Z) };
        rig.AddChild(new MeshInstance3D
        {
            Mesh = Cylinder(0.07 * sc, 0.09 * sc, m.Boom, 8),
            MaterialOverride = spar,
            Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
            Position = new Vector3(0, (float)tackY, (float)(-m.Boom * 0.45))
        });
        // taillée plate, dans l'axe ; le creux vient de la forme, sous le vent
        rig.AddChild(SailSurface(new[]
        {
            new Vec3d(0, tackY + 0.05, -0.2 * sc),
            new Vec3d(0, tackY, -m.Boom * 0.93),
            new Vec3d(0, spec.DeckMid + m.Height - 1.0 * sc, -m.Boom * 0.70),
            new Vec3d(0, spec.DeckMid + m.Height - 0.6 * sc, -0.2 * sc)
            // lacée sur trois côtés ; le plus creux aux quatre dixièmes derrière le guindant
        }, new Vec3d(1, 0, 0), new SailCut { Kind = "gaff", UPeak = 0.42, Crown = 0.80 }));
        _rig.AddChild(rig);
        return rig;
    }

    /// <summary>
    /// Gréement carré : des vergues croisées sur le mât, chacune portant sa
    /// voile. Le groupe entier brasse d'un bloc, comme on règle un carré — on
    /// brasse les vergues, on ne choque pas une bôme.
    /// </summary>
    Node3D SquareRig(MastSpec m, double sc, Material spar)
    {
        var spec = Spec;
        var rig = new Node3D { Position = new Vector3(0, 0, (float)m.Z) };
        double halfSpan = spec.B * m.YardSpan * 0.5;
        for (int i = 0; i < m.Yards.Count; i++)
        {
            double frac = m.Yards[i];
            double y = spec.DeckMid + m.Height * frac;
            double span = halfSpan * (1 - i * 0.16);            // plus étroites en montant
            double drop = m.Height * (i + 1 < m.Yards.Count ? (m.Yards[i + 1] - frac) * 0.80 : 0.16);
            rig.AddChild(new MeshInstance3D
            {
                Mesh = Cylinder(0.07 * sc, 0.05 * sc, span * 2, 8),
                MaterialOverride = spar,
                Rotation = new Vector3(0, 0, Mathf.Pi / 2),    // en travers
                Position = new Vector3(0, (float)y, 0)
            });
            /* La plus creuse EN HAUT, juste sous sa vergue, et presque plate à la
               bordure : ce sont les écoutes qui en décident, tirant ses points
               d'écoute vers les bouts de la vergue du dessous. */
            rig.AddChild(SailSurface(new[]
            {
                new Vec3d(-span, y, 0), new Vec3d(span, y, 0),
                new Vec3d(span * 0.86, y - drop, 0), new Vec3d(-span * 0.86, y - drop, 0)
            }, new Vec3d(0, 0, 1), SailCut.Square()));
        }
        _rig.AddChild(rig);
        return rig;
    }

    /// <summary>Le foc, établi volant : il pivote sur l'étai, à l'étrave.</summary>
    Node3D JibRig()
    {
        var spec = Spec;
        var j = spec.Rig.Jib!;
        var rig = new Node3D { Position = new Vector3(0, 0, (float)(spec.L / 2)) };
        var fore = spec.Masts[0];
        double back = fore.Z - spec.L / 2;               // le mât le plus avant, dans ce repère
        rig.AddChild(SailSurface(new[]
        {
            new Vec3d(0, Lines.DeckY(1) + j.TackAbove, spec.JibFootZ),
            new Vec3d(0, spec.DeckMid + j.ClewAbove, back + 0.6),
            new Vec3d(0, spec.DeckMid + fore.Height - j.HeadDrop, back + 0.15)
            // mailloché sur son étai le long du guindant, mais la bordure vole libre
        }, new Vec3d(1, 0, 0), new SailCut { Kind = "jib", UPeak = 0.40, VPeak = 0.34, VPin0 = false, Crown = 0.80 }));
        _rig.AddChild(rig);
        return rig;
    }

    // ------------------------------------------------------------------
    //  LA TOILE À L'ÉCRAN
    // ------------------------------------------------------------------

    /// <summary>
    /// UNE MATIÈRE PAR SORTE de voile et non par voile : un motif appartient aux
    /// basses voiles et aux huniers, pas au foc ; et un galion porte neuf carrés,
    /// qui seraient neuf programmes là où un suffit.
    /// </summary>
    Material CanvasMat(string kind)
    {
        var a = Spec.Appearance;
        string? src = null;
        if (a.CanvasMap != null && a.CanvasMap.TryGetValue(kind, out var s)) src = s;
        if (src == null)
        {
            _canvasMat ??= NewCanvas(null);
            return Inscrire(_canvasMat);
        }
        if (!_painted.TryGetValue(kind, out var m))
        {
            m = NewCanvas(src);
            _painted[kind] = m;
        }
        return Inscrire(m);
    }

    /// <summary>
    /// LA MATIERE DE TOILE, REINSCRITE A CHAQUE FOIS QU ON LA DONNE.
    ///
    /// BuildRig l inscrit dans Hazed — la liste de ce qui recoit le ciel, le
    /// soleil et l heure — puis LoadModel VIDE cette liste pour la refaire sur
    /// le modele. Or la matiere, elle, est en CACHE : les voiles rebaties sur
    /// les vergues du .glb reprenaient la meme et personne ne la reinscrivait.
    /// Elles gardaient donc les valeurs par defaut du shader — un horizon de
    /// plein midi — et restaient blanches a minuit, quoi qu on fit du reste
    /// (signale quatre fois, trouve en peignant la toile en ROUGE la nuit : les
    /// pavillons devenaient ecarlates, les voiles non, alors que les deux
    /// emploient le meme shader).
    ///
    /// Un objet qu on garde en cache survit a la liste qui le tenait : c est la
    /// SORTIE qui doit inscrire, pas la construction.
    /// </summary>
    ShaderMaterial Inscrire(ShaderMaterial m)
    {
        if (!Hazed.Contains(m)) { Hazed.Add(m); AddSnowed(m); }
        return m;
    }

    ShaderMaterial NewCanvas(string? mapSrc)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sail.gdshader") };
        m.SetShaderParameter(UCanvasFloor, (float)CanvasFloor);
        m.SetShaderParameter(U.Canvas, ColorX.Hex(Spec.Appearance.Canvas));
        m.SetShaderParameter(U.Emissive, ColorX.Hex("0x8d866f"));
        if (mapSrc != null)
        {
            var img = Image.LoadFromFile(System.IO.Path.Combine(RepoRoot, mapSrc));
            if (img != null && !img.IsEmpty())
            {
                img.GenerateMipmaps();
                m.SetShaderParameter(U.Map, ImageTexture.CreateFromImage(img));
                m.SetShaderParameter(U.HasMap, 1.0f);
            }
            else GD.PushWarning($"[voile] texture introuvable : {mapSrc} — la toile reste unie");
        }
        Hazed.Add(m);
        AddSnowed(m);
        return m;
    }

    MeshInstance3D SailSurface(Vec3d[] corners, Vec3d dir, SailCut cut)
    {
        var cloth = new SailCloth(corners, dir, cut);
        int n = cloth.U.Length;
        var c = new Canvas
        {
            Cloth = cloth,
            Mesh = new ArrayMesh(),
            Mat = CanvasMat(cut.Kind),
            V = new Vector3[n], N = new Vector3[n], Uv = new Vector2[n],
            Idx = new int[cloth.Indices.Length]
        };
        for (int k = 0; k < n; k++) c.Uv[k] = new Vector2(cloth.Uvs[k * 2], cloth.Uvs[k * 2 + 1]);
        /* LE SENS DES TRIANGLES EST RETOURNÉ. three.js tient pour face avant les
           triangles tournant en sens direct, Godot en sens horaire ; les normales
           venant du noyau, garder l'ordre d'origine ferait éclairer la toile par
           la face qui n'est pas au soleil. */
        for (int t = 0; t < c.Idx.Length; t += 3)
        {
            c.Idx[t] = cloth.Indices[t];
            c.Idx[t + 1] = cloth.Indices[t + 2];
            c.Idx[t + 2] = cloth.Indices[t + 1];
        }
        c.Node = new MeshInstance3D { Mesh = c.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        /* SA GRAINE DE COMBUSTION : deux voiles ne brûlent pas du même dessin.
           Un hachage et non le rang, sinon les voisines se ressemblent encore —
           ce qu'on veut n'est pas un décalage, c'est un autre feu. */
        c.Node.SetInstanceShaderParameter(UBurnSeed, BurnSeed(_canvases.Count));
        // the cut never changes: UVs and indices go across once, not every frame
        c.Arrays.SetNow((int)Mesh.ArrayType.TexUV, c.Uv);
        c.Arrays.SetNow((int)Mesh.ArrayType.Index, c.Idx);
        Upload(c);
        _canvases.Add(c);
        return c.Node;
    }

    static void Upload(Canvas c) => c.Live.Upload(c.Mesh, c.Mat, c.Cloth.Positions, c.Cloth.Normals, c.V, c.N);

    /// <summary>
    /// Brasser et former — <c>setTrim</c>. L'écoute, le bord, la toile établie, le
    /// faseyement et la charge viennent du solveur à chaque image ; aucun n'est
    /// retenu ici, sinon l'angle des vergues qui, lui, vient de loin.
    /// </summary>
    public void SetTrim(double sheet, int tack, double set, bool luffing, double t, double load)
    {
        if (_rigs.Count == 0) return;                   // pas de toile à régler
        double angle = _trim.Update(sheet, tack, luffing, t);
        /* Ferler rentre la toile, pas les espars — un bâtiment à sec de toile a
           toujours ses vergues croisées. Et pour un modèle importé, dont les
           vergues pendent dans ces pivots, cacher le groupe dépouillerait ses
           mâts. */
        foreach (var rig in _rigs)
            rig.Rotation = new Vector3(0, (float)(rig == _jibRig ? angle * 0.75 : angle), 0);
        if (_latPivot != null)
        {
            // le signe des vergues (voir BraceTrim) : l'angle voulu, moins celui du modèle
            _latPivot.Rotation = new Vector3(0, (float)(_latTrim.Update(Physics.LateenAngle, tack, false, t) - _latModelled), 0);
            // son mât tombé, elle n'est plus gréée : le solveur ne la compte plus
            Physics.LateenUp = _latMast < 0 || _latMast >= _damage.Count || _damage[_latMast].Down == null;
        }
        // les cordes accrochées aux espars qui viennent de tourner (ShipNode.Ropes.cs)
        if (_movers.Count > 0) MoveRopes();
        /* ET LE CREUX PASSE AVEC LA BÔME.
         *
         * Le signe de l'angle des bras dit de quel bord la toile porte : l'angle
         * est un lacet, la bôme montre l'arrière, donc un lacet positif l'envoie
         * à tribord (−x) et la toile doit s'y creuser. Sa normale de taille est
         * +x, d'où le signe contraire.
         *
         * Il PASSE, il ne saute pas : deux secondes et demie d'un bord à l'autre,
         * le temps que la bôme traverse. Ce qu'on voit alors est une voile qui
         * tombe à plat et se remplit de l'autre main — un empannage. */
        double dtb = Math.Clamp(t - (_lastTrimT ?? t), 0, 0.25);
        _lastTrimT = t;
        double pas = dtb * 2.5;
        void Lean(Canvas c, double want)
            => c.Cloth.Side += Math.Clamp(want - c.Cloth.Side, -pas, pas);
        double lee = -Math.Sign(angle);
        double leeLat = _latPivot != null ? -Math.Sign(_latTrim.Angle) : 1;
        foreach (var c in _canvases)
        {
            if (c.Split) continue;                       // partie : plus rien à former
            /* Une carrée se creuse vers l'avant des deux bords : elle n'a pas de
               main à changer, et lui en donner une la retournerait pour rien. */
            if (c.Cloth.Kind != "square")
                Lean(c, _latMast >= 0 && c.Mast == _latMast ? leeLat : lee);
            c.Cloth.Shape(Spec.Rig.Belly, load, luffing, t, set);
            Upload(c);
        }
    }
}
