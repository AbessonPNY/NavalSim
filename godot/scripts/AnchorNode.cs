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
        public Mooring? Moor;
        public Node3D Mesh = null!;
        public MultiMesh Cable = null!;
        public MeshInstance3D CableView = null!;
        public readonly double[] Px = new double[N], Py = new double[N], Pz = new double[N];
        public readonly double[] Ox = new double[N], Oy = new double[N], Oz = new double[N];
        public bool Strung;
        public double Acc, Tick2, FloorShip, FloorAt;
    }

    readonly World _world;
    readonly OceanNode _sea;
    readonly Dictionary<ShipPhysics, Item> _items = new();

    /// <summary>La gerbe quand le fer entre dans l'eau.</summary>
    public Action<Vec3d, double, double, double>? Splash;
    /// <summary>Ce qui se dit au commandant.</summary>
    public Action<string>? OnSay;
    public readonly List<ShaderMaterial> Hazed = new();

    StandardMaterial3D _iron = null!, _wood = null!, _rope = null!;

    public AnchorNode(World world, OceanNode sea) { _world = world; _sea = sea; }

    public override void _Ready()
    {
        StandardMaterial3D Tone(uint rgb, float rough, float metal)
        {
            var m = new StandardMaterial3D
            {
                AlbedoColor = Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb),
                Roughness = rough, Metallic = metal,
                NextPass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") }
            };
            Hazed.Add((ShaderMaterial)m.NextPass);
            return m;
        }
        _iron = Tone(0x24211e, 0.55f, 0.6f);
        _wood = Tone(0x4a3827, 0.85f, 0f);
        _rope = Tone(0x6d6047, 0.95f, 0f);
    }

    /* L'ANCRE D'AMIRAUTÉ, dans ses propres proportions : la verge, l'organeau,
       un jas de bois en travers de la tête et d'équerre avec les bras, et deux
       bras courbes finissant en pattes. C'est le jas qui la fait lire comme une
       ancre de n'importe quel côté — sans lui, c'est un grappin. */
    Node3D AnchorMesh(double s)
    {
        var g = new Node3D();
        void Add(Mesh m, Material mat, Vector3 at, Vector3 rot = default)
            => g.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = at, Rotation = rot });

        Add(new CylinderMesh { TopRadius = (float)(0.055 * s), BottomRadius = (float)(0.075 * s), Height = (float)s, RadialSegments = 7 },
            _iron, new Vector3(0, (float)(s * 0.5), 0));
        Add(new TorusMesh { InnerRadius = (float)(0.10 * s), OuterRadius = (float)(0.17 * s), RingSegments = 8, Rings = 6 },
            _iron, new Vector3(0, (float)(s * 1.05), 0), new Vector3(Mathf.Pi / 2, 0, 0));
        // le jas, en travers
        Add(new BoxMesh { Size = new Vector3((float)(0.95 * s), (float)(0.09 * s), (float)(0.09 * s)) },
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
        double thick = Math.Clamp(0.006 * S.L, 0.10, 0.32);
        it.Cable = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CylinderMesh { TopRadius = (float)thick, BottomRadius = (float)thick, Height = 1, RadialSegments = 5 },
            InstanceCount = N - 1
        };
        it.CableView = new MeshInstance3D { Mesh = null };
        AddChild(new MultiMeshInstance3D { Multimesh = it.Cable, MaterialOverride = _rope, ExtraCullMargin = 300 });
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

    /// <summary>M : mouiller, ou virer au cabestan. Le même geste dans les deux sens.</summary>
    public void Toggle(ShipNode ship, double t)
    {
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
                if (it.Moor == null) { Stow(it); OnSay?.Invoke("Ancre haute et bossée"); }
                else OnSay?.Invoke("On vire au cabestan");
                break;
            case St.Weigh:
                it.State = St.Down;
                OnSay?.Invoke("On stoppe de virer");
                break;
        }
    }

    void Stow(Item it)
    {
        it.State = St.Stowed;
        it.Mesh.Visible = false;
        it.Cable.VisibleInstanceCount = 0;
        if (it.Moor != null) { it.Ph.Moorings.Remove(it.Moor); it.Moor = null; }
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
            Cable(it, dt, t);
            Pose(it);
        }
    }

    void Fall(Item it, double dt, double t)
    {
        const double G = 9.81;
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
                   pic. */
                m.Len = Math.Max(0, m.Len - 1.2 * dt);
                double upDown = hawse.Y - m.Wy;
                if (m.Len <= upDown + 1.5)
                {
                    it.Ph.Moorings.Remove(m);
                    it.Moor = null;
                    it.State = St.Up;
                    OnSay?.Invoke("L'ancre dérape");
                }
            }
            return;
        }

        if (it.State == St.Up)
        {
            var v = hawse - it.P;
            double d = v.Length;
            if (d < 0.6) { Stow(it); OnSay?.Invoke("Ancre haute et bossée"); return; }
            it.P += v * Math.Min(1, 1.2 * dt / d);
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
        double hold = it.Mass * 9.81 * 8 * Math.Clamp((scope - 1) / 4, 0.15, 1);
        var h = Hawse(it);
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
        var hawse = HawseWorld(it);
        var ring = it.P + new Vec3d(0, it.Size * 1.05, 0);
        double reach = (hawse - ring).Length;
        double len = it.Moor != null ? Math.Max(it.Moor.Len, reach) : reach + 0.5;
        double seg = Math.Max(len, reach) / (N - 1);
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
        while (it.Acc >= H)
        {
            it.Acc -= H;
            for (int j = 1; j < L; j++)
            {
                /* Dans l'eau, presque sans poids et fortement traînée ; dans
                   l'air, lourde et libre. C'est cette différence qui fait qu'un
                   câble mouillé tombe droit et qu'un câble en l'air fouette. */
                bool wet = it.Py[j] < sea;
                double g = wet ? 1.8 : 9.81, c = wet ? 4.0 : 0.3;
                double vx = (it.Px[j] - it.Ox[j]) / H, vy = (it.Py[j] - it.Oy[j]) / H, vz = (it.Pz[j] - it.Oz[j]) / H;
                double nx = it.Px[j] + (it.Px[j] - it.Ox[j]) - c * vx * H * H;
                double ny = it.Py[j] + (it.Py[j] - it.Oy[j]) + (-g - c * vy) * H * H;
                double nz = it.Pz[j] + (it.Pz[j] - it.Oz[j]) - c * vz * H * H;
                it.Ox[j] = it.Px[j]; it.Oy[j] = it.Py[j]; it.Oz[j] = it.Pz[j];
                it.Px[j] = nx; it.Py[j] = ny; it.Pz[j] = nz;
            }
            it.Px[0] = it.Ox[0] = hawse.X; it.Py[0] = it.Oy[0] = hawse.Y; it.Pz[0] = it.Oz[0] = hawse.Z;
            it.Px[L] = it.Ox[L] = ring.X; it.Py[L] = it.Oy[L] = ring.Y; it.Pz[L] = it.Oz[L] = ring.Z;

            for (int k = 0; k < 14; k++)
            {
                for (int j = 0; j < L; j++)
                {
                    int a = j, c2 = j + 1;
                    double dx = it.Px[c2] - it.Px[a], dy = it.Py[c2] - it.Py[a], dz = it.Pz[c2] - it.Pz[a];
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d < 1e-6) d = 1e-6;
                    double f = (d - seg) / d * 0.5;
                    dx *= f; dy *= f; dz *= f;
                    bool pinA = a == 0, pinC = c2 == L;
                    if (!pinA && !pinC)
                    {
                        it.Px[a] += dx; it.Py[a] += dy; it.Pz[a] += dz;
                        it.Px[c2] -= dx; it.Py[c2] -= dy; it.Pz[c2] -= dz;
                    }
                    else if (pinA && !pinC) { it.Px[c2] -= 2 * dx; it.Py[c2] -= 2 * dy; it.Pz[c2] -= 2 * dz; }
                    else if (pinC && !pinA) { it.Px[a] += 2 * dx; it.Py[a] += 2 * dy; it.Pz[a] += 2 * dz; }
                }
                // le fond, en droite de sous elle à sous l'ancre : le mou s'y COUCHE
                for (int j = 1; j < L; j++)
                {
                    double floor = it.FloorShip + (it.FloorAt - it.FloorShip) * j / L + 0.15;
                    if (it.Py[j] < floor) { it.Py[j] = floor; it.Oy[j] = floor; }
                }
            }
        }

        for (int j = 0; j < N - 1; j++)
        {
            var a = new Vector3((float)it.Px[j], (float)it.Py[j], (float)it.Pz[j]);
            var b = new Vector3((float)it.Px[j + 1], (float)it.Py[j + 1], (float)it.Pz[j + 1]);
            var mid = (a + b) * 0.5f;
            var dir = b - a;
            float l = dir.Length();
            if (l < 1e-4f) { it.Cable.SetInstanceTransform(j, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), mid)); continue; }
            var up = dir / l;
            var any = Math.Abs(up.Y) > 0.99f ? Vector3.Right : Vector3.Up;
            var x = any.Cross(up).Normalized();
            var z = up.Cross(x);
            it.Cable.SetInstanceTransform(j, new Transform3D(new Basis(x, up * l, z), mid));
        }
        it.Cable.VisibleInstanceCount = N - 1;
    }

    void Pose(Item it)
    {
        it.Mesh.Position = new Vector3((float)it.P.X, (float)it.P.Y, (float)it.P.Z);
        // couchée sur le fond, debout dans l'eau
        it.Mesh.Rotation = it.State == St.Down
            ? new Vector3(Mathf.Pi / 2, 0, 0)
            : Vector3.Zero;
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
