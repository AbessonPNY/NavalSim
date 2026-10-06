using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// MOUILLER, ET LEVER L'ANCRE — portage d'anchor.js.
///
/// UNE ANCRE SUR LE FOND EST UNE AMARRE DONT LA BITTE PEUT BOUGER. Le solveur
/// tient déjà une coque par un ressort qui tire et ne pousse jamais, pris sur un
/// point en mètres monde ; être au mouillage, c'est ce même bout pris sur un
/// point du FOND, avec deux différences qu'il porte déjà par ligne : un long
/// câble est un ressort plus doux qu'une aussière courte, et le bout d'en face
/// CÈDE quand on tire plus fort que l'ancre ne tient. Rien ici ne pousse la
/// coque — c'est <see cref="Mooring"/> qui travaille.
///
/// Ce que ce fichier possède, c'est tout ce que le solveur n'a pas à savoir :
/// l'ancre qui quitte le bossoir, la gerbe, sa chute lente dans l'eau, le câble
/// qui file derrière elle, et le cabestan qui la ramène.
///
/// LA TENUE EST ÉNONCÉE, PAS RÉGLÉE. Une ancre de bossoir pesait quelque chose
/// comme quatre millièmes du déplacement — une tonne pour un galion de 240 —, et
/// tient huit fois son poids quand elle est bien couchée. Elle ne l'est qu'avec
/// de la TOUÉE : le câble doit tirer le long du fond et non vers le haut, si
/// bien que la tenue s'effondre à mesure que le câble se rapproche de la
/// profondeur. C'est la vraie raison pour laquelle on mouillait en rade et non
/// au large, et elle sort de l'arithmétique.
/// </summary>
public partial class AnchorNode : Node3D
{
    enum St { Stowed, Air, Water, Hang, Down, Weigh, Up }

    const int N = 28;                       // points le long du câble

    sealed class Item
    {
        public ShipNode Ship = null!;
        public ShipPhysics Ph = null!;
        public St State = St.Stowed;
        public Vec3d P, V;
        public double Mass, Size, CableMax, Tick;
        /// <summary>Le câble qui reste dehors une fois l'ancre dérapée : elle pend au bout, sous l'écubier.</summary>
        public double UpLen;
        /// <summary>Dérapée, la verge suit la chaîne : la direction de l'organeau, vue du diamant.</summary>
        public Vec3d UpShank = new(0, 1, 0);
        public Mooring? Moor;
        public Node3D Mesh = null!;
        public MultiMesh Cable = null!;
        public MeshInstance3D CableView = null!;
        /// <summary>Le nœud qui dessine le câble, pour le libérer avec l'ancre.</summary>
        public MultiMeshInstance3D? CableNode;
        /// <summary>La pose à poste, dans le repère du navire : mesurée une fois sur le bordé (HalfAt lit le modèle), elle ne change pas.</summary>
        public Transform3D? Fished;
        public readonly double[] Px = new double[N], Py = new double[N], Pz = new double[N];
        public readonly double[] Ox = new double[N], Oy = new double[N], Oz = new double[N];
        public bool Strung;
        /// <summary>Le câble a-t-il déjà pris son effort ? Pour ne le dire qu une fois.</summary>
        public bool Taut;
        public double Acc, Tick2, FloorShip, FloorAt;
        /// <summary>L'écubier, en repère du navire : SUR le bordé, mesuré une fois.</summary>
        public Vec3d? Hole;
        /// <summary>Le diamètre du fer d'un maillon, et le pas d'un maillon au suivant.</summary>
        public double Bar, Pitch;
        public int Links;
    }

    readonly World _world;
    readonly OceanNode _sea;
    readonly Dictionary<ShipPhysics, Item> _items = new();

    /// <summary>La gerbe quand le fer entre dans l'eau.</summary>
    public Action<Vec3d, double, double, double>? Splash;
    /// <summary>Ce qui se dit au commandant.</summary>
    public Action<string>? OnSay;
    public readonly List<ShaderMaterial> Hazed = new();

    StandardMaterial3D _iron = null!, _wood = null!, _chain = null!;

    public AnchorNode(World world, OceanNode sea) { _world = world; _sea = sea; }

    public override void _Ready()
    {
        StandardMaterial3D Tone(uint rgb, float rough, float metal)
        {
            var m = new StandardMaterial3D
            {
                AlbedoColor = ColorX.Rgb(rgb),
                Roughness = rough, Metallic = metal,
                NextPass = HazePass.New()
            };
            Hazed.Add((ShaderMaterial)m.NextPass);
            return m;
        }
        _iron = Tone(0x24211e, 0.55f, 0.6f);
        _wood = Tone(0x4a3827, 0.85f, 0f);
        /* LA CHAÎNE, noire et brillante : du fer passé au noir de fumée et
           graissé, qui prend le reflet du ciel sur chaque maillon mouillé. */
        _chain = Tone(0x0b0b0d, 0.22f, 0.9f);
    }

    /* L'ANCRE D'AMIRAUTÉ, dans ses propres proportions : la verge, l'organeau,
       un jas de bois en travers de la tête et d'équerre avec les bras, et deux
       bras courbes finissant en pattes. C'est le jas qui la fait lire comme une
       ancre de n'importe quel côté — sans lui, c'est un grappin. */
    /* LE MODÈLE DE L'ANCRE, s'il y en a un (props/ancre.glb — tools/anchor-glb.js, ou
       celui de l'artiste) : la verge longue d'UNE unité, le diamant à l'origine, la
       verge vers +Y, les bras dans le plan X–Y, le jas le long de Z, l'organeau en haut
       (y ≈ 1,05). Lu une fois, dupliqué pour chaque navire, échelonné à sa taille ; la
       brume posée sur ses matières. Absent, l'ancre dessinée ci-dessous. */
    Node3D? _anchorProto;
    bool _anchorTried;
    ShaderMaterial? _anchorHaze;

    Node3D? AnchorModel(double s)
    {
        if (!_anchorTried)
        {
            _anchorTried = true;
            string path = Assets.Path("props/ancre.glb");
            if (System.IO.File.Exists(path) && Assets.LoadGlb(path) is { } proto)
            {
                _anchorHaze = HazePass.New();
                Hazed.Add(_anchorHaze);
                HazePass.Chain(proto, _anchorHaze);
                _anchorProto = proto;
            }
        }
        if (_anchorProto == null) return null;
        var n = (Node3D)_anchorProto.Duplicate();
        n.Scale = Vector3.One * (float)s;
        var g = new Node3D();
        g.AddChild(n);
        AddChild(g);
        return g;
    }

    Node3D AnchorMesh(double s)
    {
        if (AnchorModel(s) is { } model) return model;
        var g = new Node3D();
        void Add(Mesh m, Material mat, Vector3 at, Vector3 rot = default)
            => g.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = at, Rotation = rot });

        Add(new CylinderMesh { TopRadius = (float)(0.055 * s), BottomRadius = (float)(0.075 * s), Height = (float)s, RadialSegments = 7 },
            _iron, new Vector3(0, (float)(s * 0.5), 0));
        Add(new TorusMesh { InnerRadius = (float)(0.10 * s), OuterRadius = (float)(0.17 * s), RingSegments = 8, Rings = 6 },
            _iron, new Vector3(0, (float)(s * 1.05), 0), new Vector3(Mathf.Pi / 2, 0, 0));
        // le jas, en travers de la tête ET D'ÉQUERRE AVEC LES BRAS (le long de Z) : il était le long des bras, comme eux
        Add(new BoxMesh { Size = new Vector3((float)(0.09 * s), (float)(0.09 * s), (float)(0.95 * s)) },
            _wood, new Vector3(0, (float)(s * 0.88), 0));
        // les deux bras, et leurs pattes
        foreach (int side in new[] { -1, 1 })
        {
            Add(new CylinderMesh { TopRadius = (float)(0.05 * s), BottomRadius = (float)(0.06 * s), Height = (float)(0.62 * s), RadialSegments = 6 },
                _iron, new Vector3((float)(side * 0.22 * s), (float)(0.12 * s), 0),
                new Vector3(0, 0, (float)(side * 1.05)));
            Add(new BoxMesh { Size = new Vector3((float)(0.26 * s), (float)(0.05 * s), (float)(0.17 * s)) },
                _iron, new Vector3((float)(side * 0.46 * s), (float)(0.30 * s), 0),
                new Vector3(0, 0, (float)(side * 0.75)));
        }
        AddChild(g);
        return g;
    }

    Item Of(ShipNode ship)
    {
        var ph = ship.Physics;
        if (_items.TryGetValue(ph, out var had)) return had;
        var S = ship.Spec;
        double size = Math.Clamp(0.09 * S.L, 1.2, 5);
        var it = new Item
        {
            Ship = ship, Ph = ph,
            Mass = Math.Max(40, 0.004 * S.MassKg),
            Size = size,
            CableMax = Math.Min(220, 6 * S.L),
            Mesh = AnchorMesh(size)
        };
        /* DES MAILLONS, pas un tuyau : un fer de six centimètres sur une coque de
           vingt-sept mètres, neuf au plus, le maillon six fers de long et trois
           et demi de large, chacun tourné d'un quart sur le précédent. Un tore
           unité étiré à ces mesures, en autant d'exemplaires que la touée en
           demande — quelques centaines, une seule passe de rendu. */
        it.Bar = Math.Clamp(0.0022 * S.L, 0.03, 0.09);
        it.Pitch = 4 * it.Bar;
        it.Links = Math.Min(2400, (int)Math.Ceiling((it.CableMax * 1.25 + 10) / it.Pitch));
        it.Cable = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new TorusMesh { InnerRadius = 0.445f, OuterRadius = 1f, Rings = 10, RingSegments = 6 },
            InstanceCount = it.Links,
            VisibleInstanceCount = 0
        };
        it.CableView = new MeshInstance3D { Mesh = null };
        it.CableNode = new MultiMeshInstance3D { Multimesh = it.Cable, MaterialOverride = _chain, ExtraCullMargin = 300 };
        AddChild(it.CableNode);
        it.Mesh.Visible = false;
        _items[ph] = it;
        return it;
    }

    /// <summary>
    /// Où l'ancre pend et d'où le câble sort : le BOSSOIR, sur l'avant, à
    /// tribord, juste sous la lisse du gaillard. Lu sur le bordé même — tribord
    /// est −x local — et EN DEHORS du bordage, ce qui est toute la raison d'être
    /// d'un bossoir : porter l'ancre claire du bord. Écrit d'abord à la
    /// demi-largeur, l'ancre était larguée DANS la coque et tombait au travers
    /// sans qu'on la voie : les nombres étaient justes et rien n'était à
    /// l'écran.
    /// </summary>
    Vec3d Hawse(Item it)
    {
        var S = it.Ph.Spec;
        var shell = it.Ship.HullShell();
        if (shell != null && shell.Length > 0)
        {
            var b = shell[^1];
            return new Vec3d(-(b.Half + 0.4 + 0.02 * S.L), b.Deck - 0.8, b.Z0 + 0.45 * (b.Z1 - b.Z0));
        }
        return new Vec3d(-(0.5 * S.B + 0.4), 1.2, 0.42 * S.L);
    }

    Vec3d HawseWorld(Item it)
    {
        var b = it.Ph.Body;
        return b.Pos + b.Quat.Rotate(Hawse(it));
    }

    /// <summary>
    /// L'ÉCUBIER : le trou du bordé d'où sort la chaîne, sous le bossoir. La
    /// chaîne partait du bossoir lui-même, un mètre EN DEHORS du bordé, et
    /// flottait dans l'air à côté de la coque. Le bordé est lu sur le modèle à
    /// cette station et cette hauteur précises — l'avant s'affine, et la largeur
    /// d'une tranche entière laissait encore un jour —, sinon sur le plan de
    /// formes.
    /// </summary>
    Vec3d HoleLocal(Item it)
    {
        if (it.Hole is Vec3d had) return had;
        var cat = Hawse(it);
        var S = it.Ph.Spec;
        double half = it.Ship.HalfAt(cat.Z, cat.Y) ?? Lines(S, cat.Z, cat.Y);
        // un doigt en dedans : le fer du maillon couvre la jointure
        var h = new Vec3d(-Math.Max(0.05, half - 0.03), cat.Y, cat.Z);
        it.Hole = h;
        return h;

        static double Lines(ShipSpec S, double z, double y)
        {
            var l = new HullLines(S);
            double t = Math.Clamp(z / S.L + 0.5, 0, 1);
            double deck = l.DeckY(t), keel = l.KeelY(t);
            double s = Math.Clamp((deck - y) / Math.Max(1e-3, deck - keel), 0, 1);
            return l.HalfB(t) * l.BeamFactor(s);
        }
    }

    Vec3d HoleWorld(Item it)
    {
        var b = it.Ph.Body;
        return b.Pos + b.Quat.Rotate(HoleLocal(it));
    }

    /// <summary>
    /// La verge : debout quand l'ancre pend ou tombe, COUCHÉE sur le fond vers le
    /// navire quand elle y est — c'est la chaîne qui la tire de ce côté-là.
    /// </summary>
    Vec3d Shank(Item it)
    {
        if (it.State is St.Down or St.Weigh)
        {
            var to = HoleWorld(it) - it.P;
            var flat = new Vec3d(to.X, 0, to.Z);
            double l = flat.Length;
            if (l > 1e-3) return flat * (1 / l);
        }
        if (it.State == St.Up) return it.UpShank;
        return new Vec3d(0, 1, 0);
    }

    /// <summary>L'organeau, où la chaîne est étalinguée : au bout de la verge, où qu'elle pointe.</summary>
    Vec3d Ring(Item it) => it.P + Shank(it) * (it.Size * 1.05);

    /// <summary>M : mouiller, ou virer au cabestan. Le même geste dans les deux sens.</summary>
    public void Toggle(ShipNode ship, double t)
    {
        /* PAS D'ANCRE, PAS DE MOUILLAGE. Une chaloupe n'a ni écubier, ni
           cabestan, ni le poids qu'il faudrait pour tenir — et l'on pouvait
           pourtant mouiller avec (signalé). La fiche le dit (« anchor »: false)
           plutôt que le code de deviner : une galère en a une, et elle a aussi
           des avirons. */
        if (!ship.Spec.HasAnchor) { OnSay?.Invoke("Pas d'ancre à bord"); return; }
        var it = Of(ship);
        switch (it.State)
        {
            case St.Stowed:
                it.P = HawseWorld(it);
                it.V = it.Ph.Body.Vel * 0.3;
                it.State = St.Air;
                it.Mesh.Visible = true;
                it.Strung = false;
                OnSay?.Invoke("Mouille !");
                break;
            case St.Down:
            case St.Hang:
                it.State = St.Weigh;
                it.Taut = false;
                if (it.Moor == null) { Stow(it); OnSay?.Invoke("Ancre haute et bossée"); }
                else OnSay?.Invoke("On vire au cabestan");
                break;
            case St.Weigh:
                it.State = St.Down;
                OnSay?.Invoke("On stoppe de virer");
                break;
        }
    }

    /// <summary>
    /// Rentrée d'un coup, amarres comprises : pour un TRANSPORT, pas une
    /// manœuvre — une ancre laissée au fond tiendrait la coque à des lieues.
    /// </summary>
    public void Weigh(ShipNode ship)
    {
        if (_items.TryGetValue(ship.Physics, out var it) && it.State != St.Stowed) Stow(it);
    }

    void Stow(Item it)
    {
        it.State = St.Stowed;
        it.Mesh.Visible = false;
        it.Cable.VisibleInstanceCount = 0;
        if (it.Moor != null) { it.Ph.Moorings.Remove(it.Moor); it.Moor = null; }
    }

    /* L'ANCRE À POSTE (demandé, photographie à l'appui) : rentrée, elle n'est pas cachée,
       elle est TRAVERSÉE le long du bord comme sur un vaisseau du temps — l'organeau
       pendu au bossoir, la verge couchée vers l'arrière contre la muraille, le jas
       debout, une patte au bordé. Posée dans le repère du navire, elle le suit dans sa
       houle ; rien à simuler, elle ne bouge pas tant qu'on ne la largue pas. */
    public void PoseStowed(ShipNode ship)
    {
        if (!ship.Spec.HasAnchor || ship.IsGhost) return;
        var it = Of(ship);
        if (it.State != St.Stowed) return;
        var ph = it.Ph;
        bool show = ship.Visible && !ph.Foundered && !ph.Broken;
        it.Mesh.Visible = show;
        if (show) it.Mesh.GlobalTransform = ship.GlobalTransform * (it.Fished ??= FishedPose(it));
    }

    /// <summary>La pose de l'ancre à poste, dans le repère du navire (le diamant à l'origine du modèle).</summary>
    Transform3D FishedPose(Item it)
    {
        var S = it.Ph.Spec;
        double s = it.Size;
        var cat = Hawse(it);
        double side = cat.X < 0 ? -1 : 1;
        // l'organeau sous le bossoir
        var ring = new Vector3((float)cat.X, (float)(cat.Y - 0.15 * s), (float)cat.Z);
        // le diamant vers l'arrière, un peu plus bas, la patte intérieure au bordé
        double zc = cat.Z - 1.0 * s, yc = cat.Y - 0.35 * s;
        double half = it.Ship.HalfAt(zc, yc) ?? 0.5 * S.B;
        var want = new Vector3((float)(side * (half + 0.36 * s)), (float)yc, (float)zc);
        // la verge, de l'organeau au diamant, à sa longueur vraie (l'organeau est à 1,05 de la verge)
        var Y = (ring - want).Normalized();
        var crown = ring - Y * (float)(1.05 * s);
        // le jas debout (l'axe Z du modèle), les bras à plat (l'axe X)
        var down = new Vector3(0, -1, 0);
        var Z = (down - Y * Y.Dot(down)).Normalized();
        var X = Y.Cross(Z);
        return new Transform3D(new Basis(X, Y, Z), crown);
    }

    /// <summary>Les ancres des navires partis : leur fer et leur câble avec eux.</summary>
    public void Prune()
    {
        List<ShipPhysics>? gone = null;
        foreach (var (ph, it) in _items)
            if (!IsInstanceValid(it.Ship)) (gone ??= new()).Add(ph);
        if (gone == null) return;
        foreach (var ph in gone)
        {
            var it = _items[ph];
            if (it.Moor != null) ph.Moorings.Remove(it.Moor);
            if (IsInstanceValid(it.Mesh)) it.Mesh.QueueFree();
            if (it.CableNode != null && IsInstanceValid(it.CableNode)) it.CableNode.QueueFree();
            _items.Remove(ph);
        }
    }

    /// <summary>Pour les essais : l'état de l'ancre et sa distance à l'écubier.</summary>
    public string Probe(ShipNode ship)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (ph, it) in _items)
            sb.Append(FormattableString.Invariant($"[{(ph == ship.Physics ? "le nôtre" : it.Ship.Spec.Name)} : {it.State} à {(HawseWorld(it) - it.P).Length:F1} m, {it.Cable.VisibleInstanceCount} maillons{(it.Moor is { } m ? $", câble {m.Len:F1} m" : "")}] "));
        return sb.Length > 0 ? sb.ToString() : "aucune ancre";
    }

    /// <summary>Est-elle au mouillage ? Pour le tableau de bord.</summary>
    public bool IsDown(ShipNode ship) =>
        _items.TryGetValue(ship.Physics, out var it) && (it.State == St.Down || it.State == St.Weigh);

    public void Step(double dt, double t)
    {
        foreach (var it in _items.Values)
        {
            if (it.State == St.Stowed) continue;
            Fall(it, Math.Min(dt, 0.25), t);
            /* RANGÉE DANS CETTE IMAGE MÊME, elle n'est plus redessinée : Stow cachait la
               chaîne, et Cable la réaffichait juste après, figée à sa dernière place —
               les images suivantes sautant une ancre rangée, ces maillons restaient
               dans l'air derrière le navire (signalé deux fois, treize maillons relevés). */
            if (it.State == St.Stowed) continue;
            Cable(it, dt, t);
            Pose(it);
        }
    }

    void Fall(Item it, double dt, double t)
    {
        const double G = Config.G;
        var o = _sea.Core.Origin;
        var hawse = HawseWorld(it);
        double sea = _sea.Core.Sample(it.P.X, it.P.Z, t);

        if (it.State is St.Air or St.Water or St.Hang)
        {
            int n = Math.Max(1, (int)Math.Ceiling(dt * 60));
            double h = dt / n;
            for (int k = 0; k < n; k++)
            {
                if (it.P.Y > sea) it.V = new Vec3d(it.V.X, it.V.Y - G * h, it.V.Z);
                else
                {
                    if (it.State == St.Air)
                    {
                        it.State = St.Water;
                        /* Une tonne de fer à huit mètres par seconde tient plus
                           du boulet que d'une coque qui s'assied : elle prend la
                           colonne, pas la couronne. */
                        Splash?.Invoke(new Vec3d(it.P.X, sea, it.P.Z), 3 + it.Mass / 150, Math.Abs(it.V.Y), 1.6);
                    }
                    /* Du fer à jas de bois tombe dans l'eau à quelques mètres par
                       seconde, et le jas l'empêche de culbuter : on relaxe vers
                       cette vitesse, en perdant l'erre qu'elle tenait du navire. */
                    double vt = 2.6 + 0.4 * Math.Log10(it.Mass / 100 + 1);
                    double f = Math.Min(1, h / 0.35), g2 = Math.Max(0, 1 - h / 0.8);
                    it.V = new Vec3d(it.V.X * g2, it.V.Y + (-vt - it.V.Y) * f, it.V.Z * g2);
                }
                it.P += it.V * h;
                // le câble file librement tant qu'il en reste
                double d = (it.P - hawse).Length;
                if (d > it.CableMax)
                {
                    it.P = hawse + (it.P - hawse) * (it.CableMax / d);
                    it.V *= 0.5;
                    if (it.State != St.Hang)
                    {
                        it.State = St.Hang;
                        OnSay?.Invoke("Tout le câble est dehors — elle ne touche pas le fond");
                    }
                }
            }
            double floor = _world.HeightAt(it.P.X + o.X, it.P.Z + o.Z);
            if (it.P.Y <= floor + 0.2) Bottom(it, floor, hawse, sea);
            return;
        }

        if (it.State is St.Down or St.Weigh)
        {
            var m = it.Moor!;
            it.P = new Vec3d(m.Wx - o.X, m.Wy, m.Wz - o.Z);
            if (m.Dragging)
            {
                it.Tick += dt;
                m.Wy = _world.HeightAt(m.Wx, m.Wz) + 0.2;
                /* Pas pendant qu'on vire : le cabestan tire EXPRÈS au-delà de la
                   tenue, et « elle chasse » est le bon avertissement au mouillage
                   et le mauvais quand c'est l'équipage qui la ramène. */
                if (it.Tick > 6 && it.State == St.Down) { it.Tick = 0; OnSay?.Invoke("L'ancre chasse !"); }
            }
            if (it.State == St.Weigh)
            {
                /* LE CABESTAN rentre du câble et le navire vient à son ancre,
                   parce que c'est le même ressort : raccourcissez la ligne et
                   elle est halée au-dessus. Elle dérape quand le câble est à
                   pic.

                   ET LE MOU RENTRE VITE. Signalé : « je n'arrive pas à faire
                   remonter l'ancre, elle reste au fond ». Elle remontait — mais
                   après VINGT ET UNE SECONDES pendant lesquelles rien ne bougeait.

                   Le compte : l'équipage file trois fois et demie la profondeur,
                   soit 38,7 m de câble pour une ancre qui n'est qu'à 11,5 m de
                   l'écubier ; à 1,2 m/s il fallait rentrer vingt-six mètres de MOU
                   avant que la ligne ne prenne le moindre effort, et l'ancre ne
                   pouvait pas bouger d'un pouce pendant ce temps-là.

                   C'est faux au sens propre : du câble qui ne tire pas se hale à
                   la main, et ce n'est que lorsqu'il vient À PIC que le cabestan
                   commence à travailler — il ne s'agit plus alors de rentrer du
                   cordage mais de haler le navire au-dessus de son ancre. Deux
                   allures, donc, et la lenteur reste là où l'effort est. */
                double slack = m.Len - (hawse - it.P).Length;
                bool apic = slack <= 0.5;
                if (apic && !it.Taut) { it.Taut = true; OnSay?.Invoke("Le câble est à pic"); }
                m.Len = Math.Max(0, m.Len - (apic ? 1.2 : 4.5) * dt);
                double upDown = hawse.Y - m.Wy;
                /* ELLE NE DÉRAPE QUE LE CÂBLE À PIC — le navire AU-DESSUS d'elle, pas
                   seulement le câble rentré. Compter sur la longueur seule la faisait
                   déraper à des dizaines de mètres derrière un navire qui faisait route
                   (signalé : l'ancre restait en l'air, la chaîne étirée jusqu'à lui).
                   Tant qu'elle est loin, le câble court la hale vers lui en la faisant
                   chasser sur le fond : c'est le même ressort, elle finit sous l'étrave. */
                double reachNow = (hawse - it.P).Length;
                if (m.Len <= upDown + 1.5 && reachNow <= upDown + 4)
                {
                    // ce qui reste dehors : de l'écubier à l'organeau, où la chaîne la tient
                    double toRing = (HoleWorld(it) - Ring(it)).Length;
                    it.Ph.Moorings.Remove(m);
                    it.Moor = null;
                    it.State = St.Up;
                    it.UpLen = toRing;
                    it.UpShank = new Vec3d(0, 1, 0);
                    OnSay?.Invoke("L'ancre dérape");
                }
            }
            return;
        }

        if (it.State == St.Up)
        {
            /* DÉRAPÉE, ELLE PEND AU CÂBLE qui reste, sous l'écubier — et le suit. Elle
               était hissée VERS lui à 1,2 m/s en mètres du monde : un navire qui fait
               route la laissait derrière, en l'air, la chaîne étirée sans fin. Ici le
               cabestan raccourcit ce qui pend, et c'est la longueur qui la tient près
               de lui ; elle tombe sous l'écubier par son poids, et traîne en arrière
               quand il avance. */
            /* LA CHAÎNE LA PORTE (Cable) : elle est le dernier point de la ligne, lourde
               et libre, et c'est le câble qui raccourcit — elle monte parce qu'il la
               hale, se balance quand le navire avance, et ne glisse plus le long de
               lui (signalé : « elle remonte en parallèle de la chaîne »). */
            it.UpLen = Math.Max(0, it.UpLen - 1.2 * dt);
            if (it.UpLen < 0.6) { Stow(it); OnSay?.Invoke("Ancre haute et bossée"); return; }
        }
    }

    /// <summary>
    /// Au fond. L'équipage file de la touée avant d'étalinguer — trois fois et
    /// demie la profondeur s'il l'a —, et c'est cette longueur que l'amarre
    /// reçoit. La tenue suit de la touée qu'ils ont obtenue.
    /// </summary>
    void Bottom(Item it, double floor, Vec3d hawse, double sea)
    {
        var o = _sea.Core.Origin;
        double depth = Math.Max(1, sea - floor);
        double reach = (it.P - hawse).Length;
        double len = Math.Min(it.CableMax, Math.Max(reach * 1.02, 3.5 * depth));
        double scope = len / depth;
        double hold = it.Mass * Config.G * 8 * Math.Clamp((scope - 1) / 4, 0.15, 1);
        var h = HoleLocal(it);
        it.Moor = new Mooring
        {
            Lx = h.X, Ly = h.Y, Lz = h.Z,
            Wx = it.P.X + o.X, Wy = floor + 0.2, Wz = it.P.Z + o.Z,
            Len = len, Hold = hold,
            // un long câble prend son poids sur une longue élongation : il étarque, il ne secoue pas
            Stretch = Math.Clamp(0.06 * len, 0.8, 12)
        };
        it.Ph.Moorings.Add(it.Moor);
        it.State = St.Down;
        it.V = new Vec3d(0, 0, 0);
        /* Le mou apparaît ICI, d'un coup : la chaîne était tendue pendant la
           chute, elle doit être reposée avec la touée qu'on vient de filer. */
        it.Strung = false;
        string peu = scope < 3 ? " — trop peu pour bien tenir" : "";
        OnSay?.Invoke(FormattableString.Invariant($"Ancre au fond par {depth:F0} m · {len:F0} m de câble{peu}"));
    }

    /// <summary>
    /// Le câble : une chaîne de Verlet tenue au bossoir et à l'organeau, lourde
    /// dans l'air, presque sans poids et fortement traînée dans l'eau, et jamais
    /// au travers du fond.
    /// </summary>
    void Cable(Item it, double dt, double t)
    {
        var hawse = HoleWorld(it);
        var ring = Ring(it);
        double reach = (hawse - ring).Length;
        bool hung = it.State == St.Up;
        // dérapée, la ligne est ce qui reste dehors, et ce n'est plus l'ancre qui fixe sa longueur
        double len = it.Moor != null ? Math.Max(it.Moor.Len, reach) : hung ? Math.Max(0.2, it.UpLen) : reach + 0.5;
        double seg = (hung ? len : Math.Max(len, reach)) / (N - 1);
        double sea = _sea.Core.Sample(hawse.X, hawse.Z, t);
        var o = _sea.Core.Origin;

        if (!it.Strung)
        {
            it.Strung = true;
            /* LA FORME QU'IL A VRAIMENT, et non la corde tendue. Posé en ligne
               droite, le câble doit gagner d'un coup tout son mou — trente-huit
               mètres entre deux points qui en sont distants de quatorze — et la
               chaîne s'empile en ressort au-dessus de l'ancre, faute de savoir
               où le mettre. Vu à la capture, deux fois.

               Un câble mouillé pend en chaînette du bossoir jusqu'à ce qu'il
               TOUCHE, et tout le reste TRAÎNE sur le fond jusqu'à l'organeau :
               c'est d'ailleurs cette partie couchée qui fait la tenue. On le
               pose donc ainsi, et le Verlet n'a plus qu'à l'entretenir. */
            double depth = Math.Max(1, hawse.Y - ring.Y);
            double hang = Math.Min(len, Math.Max(depth * 1.05, Math.Min(2.2 * depth, len)));
            double drag = Math.Max(0, len - hang);
            double flat = Math.Sqrt(Math.Max(0, hang * hang - depth * depth));
            var toward = new Vec3d(ring.X - hawse.X, 0, ring.Z - hawse.Z);
            double horiz = Math.Max(1e-6, toward.Length);
            var dirv = toward * (1 / horiz);
            // le point de touche : au bout de la partie suspendue, sur le fond
            var touch = ring - dirv * Math.Min(drag, horiz);
            int nHang = Math.Max(1, (int)Math.Round((N - 1) * hang / Math.Max(1e-6, len)));
            for (int j = 0; j < N; j++)
            {
                Vec3d p;
                if (j <= nHang)
                {
                    double u = (double)j / nHang;
                    p = hawse + (touch - hawse) * u;
                    // le ventre de la chaînette, creusé vers le bas
                    p = new Vec3d(p.X, p.Y - Math.Sin(u * Math.PI) * depth * 0.22, p.Z);
                }
                else
                {
                    double u = (double)(j - nHang) / Math.Max(1, N - 1 - nHang);
                    p = touch + (ring - touch) * u;
                }
                it.Px[j] = it.Ox[j] = p.X; it.Py[j] = it.Oy[j] = p.Y; it.Pz[j] = it.Oz[j] = p.Z;
            }
            _ = flat;
        }

        /* À PAS FIXE, et c'est ce qui manquait : intégrée au pas de l'image, la
           chaîne s'entortillait en accordéon dès qu'il y avait du mou — vu à la
           capture, trente-huit mètres de câble repliés en ressort sous la
           quille. Un soixantième de seconde, quatorze passes de contraintes, et
           le fond appliqué DANS la boucle. */
        const double H = 1.0 / 60;
        it.Acc = Math.Min(it.Acc + dt, 4 * H);
        if (it.Tick2 <= 0)
        {
            it.Tick2 = 0.5;
            var bp = it.Ph.Body.Pos;
            it.FloorShip = _world.HeightAt(bp.X + o.X, bp.Z + o.Z);
            it.FloorAt = _world.HeightAt(ring.X + o.X, ring.Z + o.Z);
        }
        it.Tick2 -= dt;

        int L = N - 1;
        // le repère du navire, pour écarter la chaîne de son bordé (HullClear)
        var inv = it.Ship.GlobalTransform.AffineInverse();
        double side = Hawse(it).X < 0 ? -1 : 1;
        while (it.Acc >= H)
        {
            it.Acc -= H;
            for (int j = 1; j <= (hung ? L : L - 1); j++)
            {
                /* Dans l'eau, presque sans poids et fortement traînée ; dans
                   l'air, lourde et libre. C'est cette différence qui fait qu'un
                   câble mouillé tombe droit et qu'un câble en l'air fouette.
                   L'ANCRE, au bout quand elle pend, est du fer : lourde même
                   dans l'eau, et peu freinée. */
                bool wet = it.Py[j] < sea, anchor = hung && j == L;
                double g = anchor ? (wet ? 8.5 : Config.G) : wet ? 1.8 : Config.G;
                double c = anchor ? (wet ? 1.2 : 0.1) : wet ? 4.0 : 0.3;
                double vx = (it.Px[j] - it.Ox[j]) / H, vy = (it.Py[j] - it.Oy[j]) / H, vz = (it.Pz[j] - it.Oz[j]) / H;
                double nx = it.Px[j] + (it.Px[j] - it.Ox[j]) - c * vx * H * H;
                double ny = it.Py[j] + (it.Py[j] - it.Oy[j]) + (-g - c * vy) * H * H;
                double nz = it.Pz[j] + (it.Pz[j] - it.Oz[j]) - c * vz * H * H;
                it.Ox[j] = it.Px[j]; it.Oy[j] = it.Py[j]; it.Oz[j] = it.Pz[j];
                it.Px[j] = nx; it.Py[j] = ny; it.Pz[j] = nz;
            }
            it.Px[0] = it.Ox[0] = hawse.X; it.Py[0] = it.Oy[0] = hawse.Y; it.Pz[0] = it.Oz[0] = hawse.Z;
            if (!hung) { it.Px[L] = it.Ox[L] = ring.X; it.Py[L] = it.Oy[L] = ring.Y; it.Pz[L] = it.Oz[L] = ring.Z; }

            for (int k = 0; k < 14; k++)
            {
                for (int j = 0; j < L; j++)
                {
                    int a = j, c2 = j + 1;
                    double dx = it.Px[c2] - it.Px[a], dy = it.Py[c2] - it.Py[a], dz = it.Pz[c2] - it.Pz[a];
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d < 1e-6) d = 1e-6;
                    /* Chaque bout cède à proportion de sa légèreté : l'écubier ne cède
                       pas, l'organeau non plus tant que l'ancre est au fond ; dérapée,
                       elle cède un peu — une tonne de fer au bout d'une chaîne. */
                    double wa = a == 0 ? 0 : 1, wc = c2 == L ? (hung ? 0.08 : 0) : 1;
                    if (wa + wc <= 0) continue;
                    double f = (d - seg) / d / (wa + wc);
                    dx *= f; dy *= f; dz *= f;
                    it.Px[a] += dx * wa; it.Py[a] += dy * wa; it.Pz[a] += dz * wa;
                    it.Px[c2] -= dx * wc; it.Py[c2] -= dy * wc; it.Pz[c2] -= dz * wc;
                }
                // le fond, en droite de sous elle à sous l'ancre : le mou s'y COUCHE
                for (int j = 1; j <= (hung ? L : L - 1); j++)
                {
                    double floor = it.FloorShip + (it.FloorAt - it.FloorShip) * j / L + 0.15;
                    if (it.Py[j] < floor) { it.Py[j] = floor; it.Oy[j] = floor; }
                }
                // et le bordé : la chaîne passe le long de la coque, jamais au travers
                HullClear(it, inv, side, hung ? L : L - 1);
            }
        }

        /* DÉRAPÉE, L'ANCRE EST LÀ OÙ LA CHAÎNE L'A MENÉE : son organeau au bout de la
           ligne, sa verge dans l'axe du dernier maillon. */
        if (hung)
        {
            var ringNow = new Vec3d(it.Px[L], it.Py[L], it.Pz[L]);
            var up = new Vec3d(it.Px[L - 1] - it.Px[L], it.Py[L - 1] - it.Py[L], it.Pz[L - 1] - it.Pz[L]);
            double ul = up.Length;
            it.UpShank = ul > 1e-4 ? up * (1 / ul) : new Vec3d(0, 1, 0);
            it.P = ringNow - it.UpShank * (it.Size * 1.05);
            // le fer aussi : ses pattes s'ouvrent d'une demi-taille de chaque côté de la verge
            it.P = Clear(it, inv, side, it.P, 0.45 * it.Size);
        }
        Links(it);
    }

    /// <summary>
    /// ÉCARTER LA CHAÎNE DU BORDÉ : chaque point qui est entré dans la coque est
    /// ramené à sa surface, du côté où il est — ou du côté du bossoir s'il est près
    /// de l'axe. Le point et sa position d'avant bougent ensemble : il garde sa
    /// vitesse, au lieu d'en recevoir une qui le ferait rebondir.
    /// </summary>
    void HullClear(Item it, Transform3D inv, double side, int last)
    {
        var xf = it.Ship.GlobalTransform;
        for (int j = 1; j <= last; j++)
        {
            var q = Clear(it, inv, side, new Vec3d(it.Px[j], it.Py[j], it.Pz[j]), j == N - 1 ? 0.4 * it.Size : 0.12, xf);
            double dx = q.X - it.Px[j], dy = q.Y - it.Py[j], dz = q.Z - it.Pz[j];
            if (dx == 0 && dy == 0 && dz == 0) continue;
            it.Px[j] += dx; it.Py[j] += dy; it.Pz[j] += dz;
            it.Ox[j] += dx; it.Oy[j] += dy; it.Oz[j] += dz;
        }
    }

    /// <summary>Un point (scène) ramené hors du bordé, à <paramref name="margin"/> mètres de lui.</summary>
    Vec3d Clear(Item it, Transform3D inv, double side, Vec3d p, double margin, Transform3D? xf = null)
    {
        var lp = inv * p.ToGodot();
        double w = it.Ship.HullHalfWidth(lp.Z, lp.Y);
        if (w <= 0) return p;
        w += margin;
        if (Math.Abs(lp.X) >= w) return p;
        double sg = Math.Abs(lp.X) < 0.2 * w ? side : Math.Sign(lp.X);
        lp.X = (float)(sg * w);
        var back = (xf ?? it.Ship.GlobalTransform) * lp;
        return back.ToCore();
    }

    /* LES MAILLONS, égrenés le long de la ligne au pas d'un maillon : chacun
       centré sur sa longueur d'arc, son grand axe sur la tangente, et tourné
       d'un quart sur son voisin — c'est ce quart qui fait lire une chaîne. Le
       repère d'un maillon suit celui du précédent (on reprojette sa normale),
       sans quoi les maillons tourneraient sur eux-mêmes où la ligne passe à la
       verticale. */
    void Links(Item it)
    {
        float wide = (float)(1.8 * it.Bar), longHalf = (float)(3 * it.Bar);
        int L = N - 1;
        /* ÉGRENÉS DEPUIS L'ORGANEAU, et non depuis l'écubier. Comptés depuis le
           navire, les maillons restaient où ils étaient quand le câble
           raccourcissait : le bout côté ancre s'effaçait, et l'on voyait une chaîne
           immobile et une ancre qui montait seule (signalé). Comptés depuis
           l'ancre, ils glissent vers l'écubier à mesure qu'on vire et y RENTRENT ;
           ils en sortent quand on mouille.

           ET LA LIGNE ENTIÈRE, toujours : une ancre traînée par un navire lancé
           tend le câble au-delà de sa touée (son ressort), et la réserve de
           maillons ne couvrait plus que la moitié de la ligne — un bout de chaîne
           restait seul dans l'eau, l'autre bout nu (signalé, capture à l'appui).
           Si la ligne passe la réserve, les maillons s'espacent. */
        double total = 0;
        for (int j = L; j > 0; j--)
        {
            double dx = it.Px[j - 1] - it.Px[j], dy = it.Py[j - 1] - it.Py[j], dz = it.Pz[j - 1] - it.Pz[j];
            total += Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        double pitch = Math.Max(it.Pitch, total / Math.Max(1, it.Links)), along = pitch * 0.5, start = 0;
        int k = 0;
        Vector3 norm = Vector3.Up;
        bool first = true;
        for (int j = L; j > 0 && k < it.Links; j--)
        {
            var a = new Vector3((float)it.Px[j], (float)it.Py[j], (float)it.Pz[j]);
            var b = new Vector3((float)it.Px[j - 1], (float)it.Py[j - 1], (float)it.Pz[j - 1]);
            var dir = b - a;
            float l = dir.Length();
            if (l < 1e-5f) continue;
            var t = dir / l;
            if (first)
            {
                norm = Math.Abs(t.Y) > 0.9f ? Vector3.Right : Vector3.Up;
                first = false;
            }
            // la normale gardée d'un tronçon à l'autre, rendue perpendiculaire
            var keep = norm - t * norm.Dot(t);
            norm = keep.LengthSquared() > 1e-6f ? keep.Normalized() : t.Cross(Vector3.Right).Normalized();
            var side = t.Cross(norm);
            while (along <= start + l && k < it.Links)
            {
                var at = a + t * (float)(along - start);
                bool odd = (k & 1) == 1;
                var bx = (odd ? norm : side) * wide;
                var by = (odd ? side : norm) * wide;
                it.Cable.SetInstanceTransform(k, new Transform3D(new Basis(bx, by, t * longHalf), at));
                k++;
                along += pitch;
            }
            start += l;
        }
        it.Cable.VisibleInstanceCount = k;
    }

    void Pose(Item it)
    {
        it.Mesh.Position = it.P.ToGodot();
        // couchée sur le fond, la verge vers le navire ; debout dans l'eau
        var y = Shank(it);
        if (y.Y > 0.99) { it.Mesh.Basis = Basis.Identity; return; }
        var up = y.ToGodot();
        var x = up.Cross(Vector3.Up).Normalized();
        it.Mesh.Basis = new Basis(x, up, x.Cross(up));
    }

    /// <summary>L'origine flottante.</summary>
    public void Rebase(double dx, double dz)
    {
        foreach (var it in _items.Values)
        {
            it.P = new Vec3d(it.P.X - dx, it.P.Y, it.P.Z - dz);
            for (int j = 0; j < N; j++)
            {
                it.Px[j] -= dx; it.Ox[j] -= dx;
                it.Pz[j] -= dz; it.Oz[j] -= dz;
            }
        }
    }
}
