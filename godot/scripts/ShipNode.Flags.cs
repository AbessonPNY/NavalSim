using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// SES COULEURS — _buildFlags, _staffFlag, _flagAt, setFlag, setEnsignMap de
/// ship-model.js. La grille et l'onde sont dans le noyau (FlagCloth, tenu en
/// parité) ; ici, où ils flottent, de quoi ils sont faits, et le vent qui les
/// fait flotter.
///
/// C'est la seule chose à bord qui montre le vent lui-même. Les voiles montrent
/// où on les a BRASSÉES ; le pavillon montre où est le vent — le vent APPARENT,
/// comme tout ce qui flotte d'un pont qui bouge —, et c'est pourquoi il pivote
/// avant les voiles quand elle lofe.
/// </summary>
public partial class ShipNode
{
    sealed class Flag
    {
        public FlagCloth Cloth = null!;
        public Node3D Mount = null!, Pivot = null!;
        public MeshInstance3D Node = null!;
        public ArrayMesh Mesh = null!;
        public Material Mat = null!;
        public Vector3[] V = null!, N = null!;
        public Vector2[] Uv = null!;
        public int[] Idx = null!;
        public bool ByNation;
        public string? NationKey;
        public Node3D? Staff;                  // la hampe, à son pied
        public readonly Godot.Collections.Array Arrays = NewArrays();
    }

    readonly List<Flag> _flags = new();
    /// <summary>La matière du pavillon du navire : il n'a qu'une nationalité, hisser le noir les change tous.</summary>
    ShaderMaterial _flagMat = null!;
    readonly Dictionary<string, ShaderMaterial> _flagMats = new();
    Nation? _nation;
    (Texture2D? Map, Color Color, bool Painted)? _ensign0;
    static readonly StringName UEmissiveK = "u_emissive_k";
    internal static readonly StringName UCanvasFloor = "u_canvas_floor";

    /// <summary>
    /// CE QUI RESTE DE LA TOILE SOUS LES ÉTOILES, de 0 à 1 — settings.json →
    /// night.canvas. La part de sa lumière de jour que la toile et l'étoffe
    /// gardent quand le ciel est au plus noir. Elle était CONSTANTE, si bien
    /// qu'un navire feux couverts dans le noir montrait ses voiles comme des
    /// draps pendus (signalé).
    /// </summary>
    public static double CanvasFloor = 0.05;

    /* AMENER LES COULEURS PREND LE TEMPS QUE ÇA PREND : le pavillon descend le
       long de sa drisse — il rapetisse à mesure qu'il coule vers la poulie, puis
       disparaît ; hissé, il refait le chemin en sens inverse. Une seconde et
       demie, le temps qu'un homme hale. <paramref name="now"/> pour les cas où
       l'étoffe doit être là ou pas là tout de suite (un spectre qui paraît). */
    double _colourWant = 1, _colourNow = 1;
    ulong _colourT;
    const double ColourRate = 1 / 1.5;

    /// <summary>Hisser ou amener : tous ses pavillons ensemble.</summary>
    public void ShowColours(bool on, bool now = false)
    {
        _colourWant = on ? 1 : 0;
        if (now) { _colourNow = _colourWant; ApplyColours(); }
    }

    /// <summary>Sont-elles hissées (l'ordre donné, non l'étoffe en chemin) ?</summary>
    public bool ColoursUp => _colourWant > 0.5;

    /* L'HORLOGE DU MOTEUR, et non celle de la houle : cette dernière est réduite
       modulo 2π (syncPhase) et sauterait en arrière au milieu de la manœuvre. */
    void ColourTick()
    {
        ulong now = Time.GetTicksMsec();
        double dt = _colourT == 0 ? 0 : Math.Clamp((now - _colourT) / 1000.0, 0, 0.1);
        _colourT = now;
        if (Math.Abs(_colourWant - _colourNow) < 1e-4) return;
        double step = ColourRate * dt;
        _colourNow += Math.Max(-step, Math.Min(step, _colourWant - _colourNow));
        ApplyColours();
    }

    void ApplyColours()
    {
        float k = (float)Math.Max(0.001, _colourNow);
        foreach (var f in _flags)
        {
            f.Pivot.Scale = new Vector3(k, k, k);
            f.Pivot.Visible = _colourNow > 0.02;
        }
    }

    /// <summary>A-t-elle un pavillon où hisser des couleurs ?</summary>
    public bool HasFlag => _flags.Count > 0;

    /// <summary>
    /// SOUS QUELLES COULEURS ELLE NAVIGUE — l'entrée de flags.json qu'on lui a
    /// hissée, ou nulle si elle garde celles de sa fiche. C'est le seul endroit
    /// où son camp se lit : une escarmouche oppose des pavillons, pas des coques.
    /// </summary>
    public Nation? Ensign => _nation;

    /* ------------------------------------------------------------------ */
    /*  LA MATIÈRE                                                          */
    /* ------------------------------------------------------------------ */

    /* L'étamine est plus légère et plus fine que la toile à voile, et un pavillon
       blanc doit rester blanc contre un ciel clair plutôt que tourner au gris —
       d'où l'émission. Un pavillon NOIR veut le contraire : relevé de même, il
       sort charbon, ce qui est un pavillon sale et non un pavillon sinistre. Une
       image apporte donc la sienne, bien plus faible. La toile du navire, pour le
       reste : la lumière qui la traverse est la même. */
    ShaderMaterial NewFlagMat() =>
        new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sail.gdshader") };

    /// <summary>La repousser à toutes ses toiles — pour juger le réglage sans rebâtir.</summary>
    public void RefreshCanvasFloor()
    {
        foreach (var c in _canvases) if (c.Mat is ShaderMaterial cm) cm.SetShaderParameter(UCanvasFloor, (float)CanvasFloor);
        _flagMat?.SetShaderParameter(UCanvasFloor, (float)CanvasFloor);
        foreach (var m in _flagMats.Values) m.SetShaderParameter(UCanvasFloor, (float)CanvasFloor);
    }

    void Dress(ShaderMaterial m, Texture2D? map, Color colour, bool painted)
    {
        m.SetShaderParameter(U.Canvas, colour);
        m.SetShaderParameter(U.Emissive, Hex(painted ? "0x2a2a2e" : "0x7c7a72"));
        m.SetShaderParameter(UEmissiveK, painted ? 0.10f : 0.14f);
        m.SetShaderParameter(UCanvasFloor, (float)CanvasFloor);
        m.SetShaderParameter(U.Map, map!);
        m.SetShaderParameter(U.HasMap, map != null ? 1f : 0f);
    }

    ShaderMaterial Registered(ShaderMaterial m) { Hazed.Add(m); AddSnowed(m); return m; }

    /* Un pavillon qui porte ses propres armes, partagé par image. */
    ShaderMaterial FlagMat(string src)
    {
        if (_flagMats.TryGetValue(src, out var m)) return m;
        m = Registered(NewFlagMat());
        Dress(m, FlagImage(src), Colors.White, true);
        _flagMats[src] = m;
        return m;
    }

    /* Le pavillon que la fiche lui donne : une image si elle en nomme une, la tête
       de mort dessinée si elle arbore le noir sans image, sinon sa couleur. Le camp
       se lit toujours sur `ensign` seul. */
    void MakeEnsign()
    {
        var A = Spec.Appearance;
        _flagMat = Registered(NewFlagMat());
        if (A.Ensign == "jolly" || A.EnsignMap != null)
            Dress(_flagMat, A.EnsignMap != null ? FlagImage(A.EnsignMap) : Jolly(), Colors.White, true);
        else
        {
            Color c = Hex("0xf6f4ef");
            if (A.Ensign != null) try { c = Hex(A.Ensign); } catch (FormatException) { }
            Dress(_flagMat, null, c, false);
        }
    }

    /// <summary>
    /// Hisser d'autres couleurs en mer : une image, ou rien pour la tête de mort
    /// dessinée. <paramref name="nation"/>, l'entrée de flags.json d'où elle vient,
    /// donne aux pavillons qui suivent la nation leurs propres images. L'image
    /// seulement : le camp reste celui d'<c>appearance.ensign</c>.
    /// </summary>
    public void SetEnsign(string? src, Nation? nation)
    {
        _nation = nation;
        ApplyNation();
        /* LE CAMP D ABORD, L ETOFFE ENSUITE. Une coque qui n a nulle part ou hisser
           un pavillon (pas de tete de mat, pas de baton) porte quand meme des
           couleurs : sans cela elle n est d aucun bord, donc l ennemie de tous —
           ce qu une escarmouche a tout de suite montre. */
        if (_flagMat == null) return;
        _ensign0 ??= (_flagMat.GetShaderParameter(U.HasMap).AsSingle() > 0.5f ? _flagMat.GetShaderParameter(U.Map).As<Texture2D>() : null,
                      _flagMat.GetShaderParameter(U.Canvas).AsColor(),
                      _flagMat.GetShaderParameter(UEmissiveK).AsSingle() < 0.12f);
        Dress(_flagMat, src != null ? FlagImage(src) : Jolly(), Colors.White, true);
    }

    /// <summary>Les couleurs que sa fiche lui donne.</summary>
    public void ResetEnsign()
    {
        _nation = null;
        ApplyNation();
        if (_ensign0 is not { } o) return;
        Dress(_flagMat, o.Map, o.Color, o.Painted);
    }

    /* « nation » cherche l'image au nom de la coupe, « nation:poupe » sous une clé
       choisie par la fiche — si bien que le pavillon de poupe peut porter les
       armes d'une nation pendant que ses têtes de mât portent ses couleurs
       unies —, et les couleurs du navire quand la nation n'en a pas. */
    Material NationMat(string key) =>
        _nation != null && _nation.Images.TryGetValue(key, out var src) ? FlagMat(src) : _flagMat;

    void ApplyNation()
    {
        foreach (var f in _flags)
            if (f.ByNation) f.Mat = NationMat(f.NationKey!);
    }

    /* ------------------------------------------------------------------ */
    /*  OÙ ILS FLOTTENT                                                     */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// Plusieurs couleurs si la fiche les déclare — un galion porte son grand
    /// pavillon sur une hampe au couronnement, une flamme en tête d'artimon et une
    /// autre au bout du beaupré. Rien de déclaré : un seul, en tête du plus grand mât.
    /// </summary>
    void BuildFlags()
    {
        foreach (var f in _flags)
        {
            f.Mount.GetParent()?.RemoveChild(f.Mount);
            f.Mount.QueueFree();
            if (f.Staff != null) { f.Staff.GetParent()?.RemoveChild(f.Staff); f.Staff.QueueFree(); }
        }
        _flags.Clear();
        if (_flagMat == null) MakeEnsign();

        var list = Spec.Flags;
        if (list == null)
        {
            if (MastHead(null) is { } h) _flags.Add(FlagAt(h.Parent, 0, h.TopY, h.Z, new FlagSpec(), 0));
            return;
        }
        (double ZAft, double ZFore, Func<double, double> DeckNear)? st = null;
        foreach (var l in list)
        {
            if (l.At == "stern" || l.At == "bow")
            {
                st ??= DeckStations();
                _flags.Add(StaffFlag(l, st.Value));
            }
            else if (l.Mast is int mi && MastHead(mi) is { } h)
                _flags.Add(FlagAt(h.Parent, 0, h.TopY + l.Above, h.Z, l, 0));
        }
    }

    /// <summary>
    /// La tête d'un mât, dans le repère de ce qui le porte. L'indice compte de
    /// l'avant — 0 la misaine — et un négatif de l'arrière, si bien que −1 est
    /// toujours l'artimon ; null demande le plus haut, où un pavillon seul a
    /// toujours flotté. Sur un modèle, pendu DANS la chute de ce mât quand le
    /// gréement en a trouvé une : il passe par-dessus bord avec lui.
    /// </summary>
    (Node3D Parent, double TopY, double Z)? MastHead(int? index)
    {
        var spec = Spec;
        if (index is int ix)
        {
            if (ModelRoot != null)
            {
                var tops = new List<MastTop>(MastTops());
                tops.Sort((a, b) => b.Z.CompareTo(a.Z));            // l'avant d'abord, +z étant l'étrave
                int k = ix < 0 ? tops.Count + ix : ix;
                if (k < 0 || k >= tops.Count) return null;
                var top = tops[k];
                Node3D? fall = null;
                double near = 0.06 * spec.L;
                foreach (var m in _masts)
                {
                    if (m.Parent == _rig) continue;
                    double d = Math.Abs(m.Parent.Position.Z - top.Z);
                    if (d < near) { near = d; fall = m.Parent; }
                }
                return fall != null
                    ? (fall, top.Y - fall.Position.Y, top.Z - fall.Position.Z)
                    : (this, top.Y, top.Z);
            }
            var masts = new List<MastSpec>(spec.Masts);
            masts.Sort((a, b) => b.Z.CompareTo(a.Z));
            int j = ix < 0 ? masts.Count + ix : ix;
            if (j < 0 || j >= masts.Count) return null;
            return (this, spec.DeckMid + masts[j].Height, masts[j].Z);
        }

        if (ModelRoot != null)
        {
            /* On demande aux mâts DÉJÀ TROUVÉS : le gréement a sorti le fût du
               modèle pour le pendre dans sa chute, et il est le seul à savoir où. */
            MastAt? best = null;
            foreach (var m in _masts)
            {
                if (m.Parent == _rig) continue;
                var dmg = _damage.Find(d => d.Fall == m.Parent);
                if (dmg == null || !dmg.HasPole) continue;
                if (best == null || m.Height > best.Value.Height) best = m;
            }
            if (best is { } b) return (b.Parent, b.Foot + b.Height, b.Z);
            // en repli, la règle de forme : la plus haute pièce fine debout sur l'axe
            Part? p0 = null;
            foreach (var p in ModelParts())
            {
                double thick = Math.Max(p.Size.X, p.Size.Z);
                if (p.Size.Y < 3 * thick || p.Size.Y < 0.15 * spec.L) continue;
                if (Math.Abs(p.Mid.X) > 0.08 * spec.B) continue;
                if (p0 == null || p.Max.Y > p0.Max.Y) p0 = p;
            }
            return p0 != null ? (this, p0.Max.Y, p0.Mid.Z) : null;
        }
        MastSpec? tall = null;
        foreach (var m in spec.Masts) if (tall == null || m.Height > tall.Height) tall = m;
        return tall != null ? (this, spec.DeckMid + tall.Height, tall.Z) : null;      // à la machine seule, aucun
    }

    /// <summary>
    /// Un pavillon sur sa propre hampe — au couronnement, ou au bout du beaupré.
    /// La hampe est dessinée et le guindant court LE LONG d'elle : pendu droit à la
    /// pomme d'une hampe quêtée, il flottait dans l'air à côté de son mât. Ni l'un
    /// ni l'autre n'appartient à un mât : un démâtage les laisse flotter.
    /// </summary>
    Flag StaffFlag(FlagSpec l, (double ZAft, double ZFore, Func<double, double> DeckNear) st)
    {
        var spec = Spec;
        double sc = spec.L / 24;
        bool stern = l.At == "stern";
        double hoist = 0.045 * spec.L * (l.Size ?? 1);
        double z = l.Z ?? (l.ZFrac is double zf ? zf * spec.L : stern ? st.ZAft : st.ZFore);
        double y = l.Y ?? st.DeckNear(z);
        double x = l.X ?? (l.XFrac ?? 0) * spec.B;
        /* Le pavillon de beaupré est planté sur l'espar, pas sur le pont : au bout
           du beaupré tel que le modèle le dessine, sauf si la fiche dit où. */
        if (!stern && ModelRoot != null && l.Z == null && l.ZFrac == null && SpritTip() is { } sp)
        {
            z = sp.Z;
            if (l.Y == null) y = sp.Y;
        }
        y += l.Above;
        double h = l.Staff ?? Math.Max((stern ? 0.12 : 0.08) * spec.L, hoist * 1.5);
        double rake = l.Rake ?? (stern ? 0.3 : 0.1);
        double lean = stern ? -1 : 1;               // la tête penche vers le dehors
        var staff = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = (float)(0.035 * sc), BottomRadius = (float)(0.06 * sc), Height = (float)h, RadialSegments = 6
            },
            MaterialOverride = MakeHullMaterial(Hex(spec.Appearance.Spar), 0.62f)
        };
        // le pied à l'origine du nœud, comme la géométrie translatée de la page
        var foot = new Node3D { Position = new Vector3((float)x, (float)y, (float)z), Rotation = new Vector3((float)(lean * rake), 0, 0) };
        staff.Position = new Vector3(0, (float)(h / 2), 0);
        foot.AddChild(staff);
        AddChild(foot);
        var f = FlagAt(this, x, y + h * Math.Cos(rake), z + lean * h * Math.Sin(rake), l, -lean * rake);
        f.Staff = foot;
        return f;
    }

    /* Le bout du beaupré tel qu'il est dessiné : la pièce fine près de l'axe qui va
       le plus loin vers l'avant — le relevé de _sparScan. */
    (double Z, double Y)? _sprit;
    bool _spritDone;
    (double Z, double Y)? SpritTip()
    {
        if (_spritDone) return _sprit;
        _spritDone = true;
        foreach (var p in ModelParts())
        {
            if (p.Size.X > 0.08 * Spec.B || Math.Abs(p.Mid.X) > 0.08 * Spec.B) continue;
            foreach (var v in p.Verts)
                if (_sprit == null || v.Z > _sprit.Value.Z) _sprit = (v.Z, v.Y);
        }
        return _sprit;
    }

    /// <summary>
    /// Un pavillon, son guindant en (x, topY, z) dans <paramref name="parent"/>. Il
    /// pend dans une MONTURE inclinée comme ce à quoi il est frappé — <paramref name="tilt"/>,
    /// positif la tête vers l'arrière — et tourne au vent autour de cet axe.
    /// </summary>
    Flag FlagAt(Node3D parent, double x, double topY, double z, FlagSpec l, double tilt)
    {
        var cloth = new FlagCloth(Spec.L, l, Rng.Randf() * 6.28);
        string shapeName = cloth.ShapeName;
        bool byNation = l.Image != null && (l.Image == "nation" || l.Image.StartsWith("nation:"));
        string? key = byNation ? (l.Image!.Length > 7 ? l.Image[7..] : shapeName) : null;
        int n = cloth.U.Length;
        var f = new Flag
        {
            Cloth = cloth, Mesh = new ArrayMesh(), ByNation = byNation, NationKey = key,
            V = new Vector3[n], N = new Vector3[n], Uv = new Vector2[n], Idx = new int[cloth.Indices.Length]
        };
        f.Mat = byNation ? NationMat(key!) : l.Image != null ? FlagMat(l.Image) : _flagMat;
        for (int k = 0; k < n; k++) f.Uv[k] = new Vector2(cloth.Uvs[k * 2], 1 - cloth.Uvs[k * 2 + 1]);
        // le sens des triangles retourné, comme la toile : face avant horaire chez Godot
        for (int t = 0; t < f.Idx.Length; t += 3)
        {
            f.Idx[t] = cloth.Indices[t];
            f.Idx[t + 1] = cloth.Indices[t + 2];
            f.Idx[t + 2] = cloth.Indices[t + 1];
        }
        double lean = l.Tilt ?? tilt;
        f.Mount = new Node3D
        {
            Position = new Vector3((float)x, (float)topY, (float)z),
            Rotation = new Vector3((float)-lean, 0, 0)          // un tour +x porte la tête vers +z, l'étrave
        };
        f.Pivot = new Node3D { Position = new Vector3(0, (float)(-cloth.Hoist * 0.18), 0) };
        f.Node = new MeshInstance3D { Mesh = f.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        f.Pivot.AddChild(f.Node);
        f.Mount.AddChild(f.Pivot);
        parent.AddChild(f.Mount);
        UploadFlag(f);
        return f;
    }

    static void UploadFlag(Flag f)
    {
        var p = f.Cloth.Positions;
        var nr = f.Cloth.Normals;
        for (int k = 0; k < f.V.Length; k++)
        {
            f.V[k] = new Vector3(p[k * 3], p[k * 3 + 1], p[k * 3 + 2]);
            f.N[k] = new Vector3(nr[k * 3], nr[k * 3 + 1], nr[k * 3 + 2]);
        }
        var a = f.Arrays;
        a.SetNow((int)Mesh.ArrayType.Vertex, f.V);
        a.SetNow((int)Mesh.ArrayType.Normal, f.N);
        a.SetNow((int)Mesh.ArrayType.TexUV, f.Uv);
        a.SetNow((int)Mesh.ArrayType.Index, f.Idx);
        f.Mesh.ClearSurfaces();
        f.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, a);
        f.Mesh.SurfaceSetMaterial(0, f.Mat);
    }

    /// <summary>
    /// Les faire flotter, une fois par image, au vent apparent que le solveur
    /// connaît déjà. Chacun garde sa phase, sans quoi trois pavillons onduleraient
    /// comme un seul ; une longue flamme porte plus d'ondes.
    /// </summary>
    /// <summary>La mer, pour savoir si un pavillon est encore dans le vent ou déjà dans l'eau.</summary>
    public NavalSim.Core.Ocean? Sea;

    public void StreamFlags(double t)
    {
        var p = Physics;
        ColourTick();
        foreach (var f in _flags)
        {
            if (!f.Pivot.Visible || !f.Node.IsVisibleInTree()) continue;
            /* UN PAVILLON NOYÉ NE DANSE PLUS. Sous l'eau il n'y a pas de vent :
               l'étamine se colle à elle-même et pend le long de sa drisse — elle
               se met en torche. Le même calcul le fait, avec un vent nul : il perd
               sa longueur en retombant. Lu sur SA hauteur à lui, pas sur celle du
               navire : le grand pavillon de poupe touche l'eau bien avant les
               têtes de mât. */
            double vApp = p.AppWindSpeed, lift = 0;
            if (Sea != null)
            {
                var w = f.Mount.GlobalPosition;
                double over = w.Y - Sea.Sample(w.X, w.Z, t);
                if (over < 1.5)
                {
                    double air = Math.Clamp(over / 1.5, 0, 1);
                    vApp *= air;
                    lift = 1 - air;          // ce qui est dans l'eau y est PORTÉ
                }
            }
            double yaw = f.Cloth.Stream(p.AppWindAngle, p.Tack, vApp, t, lift);
            f.Pivot.Rotation = new Vector3(0, (float)yaw, 0);
            UploadFlag(f);
        }
    }

    /* ------------------------------------------------------------------ */
    /*  LES IMAGES                                                          */
    /* ------------------------------------------------------------------ */

    static readonly Dictionary<string, Texture2D?> FlagImages = new();

    /* Une image de pavillon, partagée par tous ceux qui la nomment. */
    static Texture2D? FlagImage(string src)
    {
        if (FlagImages.TryGetValue(src, out var t)) return t;
        var img = Image.LoadFromFile(System.IO.Path.Combine(RepoRoot, src));
        if (img == null || img.IsEmpty())
        {
            GD.PushWarning($"[pavillon] image introuvable : {src}");
            t = null;
        }
        else
        {
            img.GenerateMipmaps();
            t = ImageTexture.CreateFromImage(img);
        }
        FlagImages[src] = t;
        return t;
    }

    static ImageTexture? _jolly;

    /// <summary>
    /// LE PAVILLON NOIR, DESSINÉ — Naval.jollyTexture. Dessiné GRAS exprès : un
    /// motif lisible à bout de bras sur un écran est une tache grise sur un
    /// pavillon à un demi-mille ; à la taille où on le voit vraiment, seules les
    /// grandes formes survivent. Les formes de la page, tracées pixel par pixel
    /// et suréchantillonnées quatre fois dans chaque sens pour leurs bords.
    /// </summary>
    static ImageTexture Jolly()
    {
        if (_jolly != null) return _jolly;
        const int W = 256, H = 160, SS = 4;
        float cx = W * 0.5f, cy = H * 0.47f, s = H / 160f;
        var ground = new Color(0x0a / 255f, 0x0a / 255f, 0x0c / 255f);
        var bone = new Color(0xea / 255f, 0xe7 / 255f, 0xdf / 255f);

        static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1);
            float ex = px - ax - t * dx, ey = py - ay - t * dy;
            return MathF.Sqrt(ex * ex + ey * ey);
        }
        static bool Ell(float px, float py, float x, float y, float rx, float ry)
        {
            float u = (px - x) / rx, v = (py - y) / ry;
            return u * u + v * v <= 1;
        }
        static bool Tri(float px, float py, float ax, float ay, float bx, float by, float qx, float qy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - qx) * (by - qy) - (bx - qx) * (py - qy);
            float d3 = (px - ax) * (qy - ay) - (qx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        // blanc ? — l'ordre de la page : les os derrière, le crâne devant, puis les creux repris au fond
        bool White(float px, float py)
        {
            bool w = false;
            foreach (int d in new[] { 1, -1 })
            {
                if (Seg(px, py, cx - 62 * s, cy - d * 40 * s, cx + 62 * s, cy + d * 40 * s) <= 6.5f * s) w = true;
                foreach (int e in new[] { -1, 1 })
                    foreach (int o in new[] { -1, 1 })
                        if (Ell(px, py, cx + e * 62 * s, cy + e * d * 40 * s + o * 9 * s, 8.5f * s, 8.5f * s)) w = true;
            }
            if (Ell(px, py, cx, cy - 6 * s, 40 * s, 34 * s)) w = true;
            if (Tri(px, py, cx - 22 * s, cy + 18 * s, cx + 22 * s, cy + 18 * s, cx + 17 * s, cy + 40 * s)
                || Tri(px, py, cx - 22 * s, cy + 18 * s, cx + 17 * s, cy + 40 * s, cx - 17 * s, cy + 40 * s)) w = true;
            if (!w) return false;
            foreach (int e in new[] { -1, 1 }) if (Ell(px, py, cx + e * 15 * s, cy - 8 * s, 11 * s, 12.5f * s)) return false;
            if (Tri(px, py, cx, cy + 4 * s, cx + 7 * s, cy + 17 * s, cx - 7 * s, cy + 17 * s)) return false;
            foreach (int g in new[] { -11, 0, 11 })
                if (Seg(px, py, cx + g * s, cy + 20 * s, cx + g * s, cy + 38 * s) <= 1.5f * s) return false;
            return true;
        }

        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int hits = 0;
                for (int j = 0; j < SS; j++)
                    for (int i = 0; i < SS; i++)
                        if (White(x + (i + 0.5f) / SS, y + (j + 0.5f) / SS)) hits++;
                img.SetPixel(x, y, ground.Lerp(bone, hits / (float)(SS * SS)));
            }
        img.GenerateMipmaps();
        return _jolly = ImageTexture.CreateFromImage(img);
    }
}
