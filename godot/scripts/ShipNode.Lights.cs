using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Les feux du bord et les fenêtres de nuit — _buildLantern, _lanternAt,
/// _findNightGlow et setLantern de ship-model.js — et la lanterne du grand mât,
/// qui est un AJOUT : la page n'en porte pas.
///
/// Commutés par l'INTENSITÉ et l'opacité, jamais en les cachant ni en les
/// retirant. La raison d'origine — three recompilait toute la scène quand le
/// nombre de lumières visibles changeait, un à-coup de 106 ms à chaque
/// crépuscule — n'existe pas sous Godot, dont le rendu en grappes ne recompile
/// rien ; la règle reste, parce qu'elle ne coûte rien et qu'elle garde la lampe,
/// sa lueur et son repère d'accord.
/// </summary>
public partial class ShipNode
{
    /// <summary>Allumés au crépuscule, soufflés à l'aube — Naval.NIGHT.</summary>
    const double NightGlowGain = 2.6, LightAt = 0.35, SnuffAt = 0.25;
    const double FarFrom = 1500, FarFade = 1.5, FarMinSize = 0.35;

    sealed class Lantern
    {
        public Node3D Group = null!;
        public ShaderMaterial Halo = null!, Mark = null!;
        public OmniLight3D Light = null!;
        public bool Candle;
        public double Seed;
        public Swing? Swing;
    }

    readonly List<Lantern> _lanterns = new();
    readonly List<(BaseMaterial3D Mat, double Base)> _nightMats = new();
    bool _lit;
    /// <summary>Ses feux sont-ils allumés : la nuit, par la règle des fanaux.</summary>
    public bool Lit => _lit;

    /// <summary>Les réglages qui touchent aux feux : posés AVANT Build, relus par RebuildLanterns.</summary>
    public bool LanternShadows = true, WithMastLantern = true;
    static readonly RandomNumberGenerator Rng = new();

    /// <summary>
    /// Où est le pont à cette station, lu sur le modèle quand il y en a un, et pris
    /// au MAXIMUM sur une tranche plutôt qu'en un point : un couronnement sculpté
    /// se lit en dents de scie — 18,4 puis 12,4 puis 5,3 m d'une station à l'autre
    /// sur la Roter Löwe —, et une station seule avait déjà fait tomber le feu
    /// cinq mètres sous sa lisse, à l'intérieur de son propre château.
    /// </summary>
    (double ZAft, double ZFore, Func<double, double> DeckNear) DeckStations()
    {
        if (ModelRoot != null)
        {
            var parts = ModelParts();
            if (parts.Count > 0)
            {
                var deckAt = DeckProfile(parts);
                Part hull = parts[0];
                double best = -1;
                foreach (var p in parts)
                {
                    double v = (double)p.Size.X * p.Size.Y * p.Size.Z;
                    if (v > best) { best = v; hull = p; }
                }
                double z0 = hull.Min.Z, z1 = hull.Max.Z, span = z1 - z0;
                return (z0 + span * 0.02, z1 - span * 0.02, z =>   // tout à l'arrière, +z étant l'étrave
                {
                    double y = double.NegativeInfinity;
                    for (double f = -0.03; f <= 0.031; f += 0.01)
                        y = Math.Max(y, deckAt(Math.Min(z1, Math.Max(z0, z + span * f))));
                    return y;
                });
            }
        }
        return (-Spec.L * 0.45, Spec.L * 0.48, z => Lines.DeckY(Math.Min(1, Math.Max(0, z / Spec.L + 0.5))));
    }

    /// <summary>
    /// Les feux de la fiche, puis la lanterne du grand mât. Une liste VIDE veut
    /// dire aucun feu de fiche, et pas le feu par défaut : une fiche qui déclare
    /// ses lanternes dit tout ce qu'elle porte, y compris rien.
    /// </summary>
    void BuildLanterns()
    {
        foreach (var L in _lanterns)
        {
            /* la lampe retourne où le modèle l'avait, sans quoi une reconstruction
               des feux la perdrait avec son pivot */
            if (L.Swing is { } S)
            {
                S.Pivot.Quaternion = Quaternion.Identity;
                if (S.Home != null && S.Mesh.GetParent() == S.Pivot) S.Mesh.Reparent(S.Home, true);
                S.Pivot.QueueFree();
            }
            L.Group.QueueFree();
        }
        _lanterns.Clear();
        var spec = Spec;
        var (zAft, _, deckNear) = DeckStations();
        var list = spec.Lanterns ?? new List<LanternSpec> { new() { Z = zAft } };
        foreach (var l in list)
        {
            double x = l.X ?? (l.XFrac ?? 0) * spec.B;
            double z = l.Z ?? (l.ZFrac.HasValue ? l.ZFrac.Value * spec.L : zAft);
            double y = l.Y ?? deckNear(z) + 0.10 * spec.L / 6 + l.Above;
            var L = LanternAt(this, new Vector3((float)x, (float)y, (float)z), l);
            if (l.Hang != null) HangLantern(L, l.Hang);
            _lanterns.Add(L);
        }
        if (WithMastLantern) MastLantern();
        foreach (var L in _lanterns)
        {
            var g = L.Group.GlobalPosition - GlobalPosition;
            RigLog.Add(FormattableString.Invariant($"feu {(L.Candle ? "bougie" : "fanal")} x {g.X:F2} y {g.Y:F2} z {g.Z:F2}"));
        }
    }

    /// <summary>
    /// UNE LANTERNE À MI-HAUTEUR DU GRAND MÂT — demandée à l'usage, absente de la
    /// page. Le grand mât est le plus haut ; la lanterne est pendue sur sa face
    /// AVANT, à un empan du bois, pour ne pas brûler dans l'espar. Elle est
    /// l'enfant du nœud qui porte le mât, donc elle suivrait un mât qui tombe.
    /// </summary>
    void MastLantern()
    {
        if (_masts.Count == 0) return;
        var main = _masts[0];
        foreach (var m in _masts) if (m.Height > main.Height) main = m;
        /* SUR UN MODÈLE, LA FICHE DIT LEQUEL. Seul un mât d'un seul tenant a une
           hauteur mesurée ; les autres n'ont que celle de leur plus haute vergue,
           qui les raccourcit, et « le plus haut » tomberait au hasard. Le grand mât
           est le plus haut de la fiche : on prend le mât du modèle le plus proche
           de sa station. */
        if (ModelRoot != null && Spec.Masts.Count > 0)
        {
            double want = Spec.Masts[0].Z, hMax = Spec.Masts[0].Height;
            foreach (var m in Spec.Masts) if (m.Height > hMax) { hMax = m.Height; want = m.Z; }
            double best = double.PositiveInfinity;
            foreach (var m in _masts)
            {
                double d = Math.Abs(m.Parent.Position.Z + m.Z - want);
                if (d < best) { best = d; main = m; }
            }
        }
        double k = Spec.L / 24;
        var at = new Vector3(0, (float)(main.Foot + main.Height * 0.5),
                             (float)(main.Z + main.Radius + 0.35 * k));
        _lanterns.Add(LanternAt(main.Parent, at, null));
    }

    static double ParseColor(JsonElement? c, double fallback)
    {
        if (c == null) return fallback;
        var e = c.Value;
        if (e.ValueKind == JsonValueKind.Number) return e.GetDouble();
        if (e.ValueKind == JsonValueKind.String)
        {
            string s = e.GetString() ?? "";
            return Convert.ToInt32(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, 16);
        }
        return fallback;
    }

    // ------------------------------------------------------------------
    //  LA LANTERNE PENDUE AU BARROT — _hangLantern et swingLanterns
    // ------------------------------------------------------------------

    /// <summary>Ce qui fait d'un feu une lampe pendue : son crochet, sa ligne, son pendule.</summary>
    sealed class Swing
    {
        public Node3D Pivot = null!;
        public Node3D Mesh = null!;
        public Node? Home;
        public Pendulum Pend = null!;
    }

    /// <summary>
    /// UNE LANTERNE PENDUE AU BARROT. La fiche nomme un objet du modèle (<c>hang</c>) ;
    /// il est ôté d'où il était et pendu à un crochet du barrot — trouvé par un
    /// rayon tiré droit en haut depuis le haut de sa boîte —, au bout d'une ligne
    /// tirée ici du crochet à la lampe. La flamme est mise dedans, et le tout se
    /// balance en vrai pendule. Si la corde du modèle atteint déjà le barrot, il n'y
    /// a rien à tirer et le crochet est le haut de la boîte. Sans l'objet dans le
    /// modèle, la flamme reste où la fiche l'a mise.
    /// </summary>
    void HangLantern(Lantern L, string name)
    {
        if (ModelRoot == null) return;
        string want = name.ToLowerInvariant();
        Node3D? o = null;
        var stack = new Stack<Node>();
        stack.Push(ModelRoot);
        while (stack.Count > 0 && o == null)
        {
            var n = stack.Pop();
            if (n is Node3D n3 && n.Name.ToString().ToLowerInvariant() == want) { o = n3; break; }
            var kids = n.GetChildren();
            for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
        }
        if (o == null)
        {
            GD.PushWarning($"[{Spec.Id}] lanterne « {name} » absente du .glb — la flamme reste à sa place");
            return;
        }

        // ses pièces à elle, et toutes les autres : le barrot est dans les autres
        var own = new HashSet<MeshInstance3D>();
        foreach (var (mi, _) in Meshes(o)) own.Add(mi);
        var ownRel = new List<(MeshInstance3D, Transform3D)>();
        var solid = new List<(MeshInstance3D, Transform3D)>();
        foreach (var (mi, rel) in Meshes(ModelRoot))
            (own.Contains(mi) ? ownRel : solid).Add((mi, rel));

        // sa boîte, dans le repère du navire
        Aabb box = default;
        bool first = true;
        foreach (var (mi, rel) in ownRel)
        {
            var b = rel * mi.Mesh.GetAabb();
            box = first ? b : box.Merge(b);
            first = false;
        }
        if (first) return;
        Vector3 mid = box.GetCenter();
        float top = box.End.Y;

        // le barrot au-dessus de la lampe : ses propres pièces exclues, et pas plus loin que 2 m
        float? hit = RayHit(solid, new Vector3(mid.X, top - 0.02f, mid.Z), Vector3.Up, 2f);
        float line = hit.HasValue ? Math.Max(0, hit.Value - 0.02f) : 0;
        float hookY = top + line;

        var pivot = new Node3D { Position = new Vector3(mid.X, hookY, mid.Z) };   // le crochet
        AddChild(pivot);
        var home = o.GetParent();
        o.Reparent(pivot, true);                          // garde l'endroit où elle pend
        if (line > 0.03f)
        {
            // du chanvre goudronné, du crochet à l'anneau de la lampe
            pivot.AddChild(new MeshInstance3D
            {
                Mesh = Cylinder(0.007, 0.007, line, 5),
                MaterialOverride = MakeHullMaterial(Hex("0x3a2e22"), 0.9f),
                Position = new Vector3(0, -line / 2, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }

        /* LA BOUGIE EST POSÉE AU FOND DE LA LANTERNE, pas au milieu de sa boîte :
           la boîte monte jusqu'à l'anneau et ce qui la pend, et son milieu mettait
           la flamme sous le chapeau. Un rayon vers le bas depuis le milieu trouve
           le fond parmi ses propres faces ; la mèche est une bougie plus haut. */
        float flameY = mid.Y;
        float? floor = RayHit(ownRel, mid, Vector3.Down, box.Size.Y);
        if (floor.HasValue) flameY = Math.Min(mid.Y, mid.Y - floor.Value + 0.16f);   // 14 cm de cire, et la mèche
        L.Group.Reparent(pivot, false);
        L.Group.Position = new Vector3(0, flameY - hookY, 0);

        /* ET LA LANTERNE NE JETTE PAS D'OMBRE À ELLE. Ses vitres sont des faces
           comme les autres pour la carte d'ombre, qui ne sait rien du verre : la
           flamme enfermée dans une boîte ne sortait que par quatre fentes du
           chapeau — quatre taches claires au barrot, et une chambre noire. */
        foreach (var mi in own) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

        L.Swing = new Swing { Pivot = pivot, Mesh = o, Home = home, Pend = new Pendulum(hookY - flameY) };
        RigLog.Add(FormattableString.Invariant($"pendue {name} : crochet x {mid.X:F2} y {hookY:F2} z {mid.Z:F2}, ligne {line:F3}, flamme y {flameY:F2}, longueur {L.Swing.Pend.Len:F3}"));
    }

    /// <summary>
    /// Le rayon le plus proche touché, en mètres, dans le repère du navire, sur des
    /// maillages SANS collision — les modèles n'en ont pas. Comme le Raycaster de
    /// three : seules les faces tournées vers le rayon comptent, sauf pour une
    /// matière double face. Godot tient pour face avant le sens horaire, d'où le
    /// signe du test.
    /// </summary>
    static float? RayHit(List<(MeshInstance3D Mi, Transform3D Rel)> meshes, Vector3 o, Vector3 dir, float far)
    {
        float best = far;
        bool any = false;
        foreach (var (mi, rel) in meshes)
        {
            var mesh = mi.Mesh;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s);
                bool both = mat is BaseMaterial3D bm && bm.CullMode == BaseMaterial3D.CullModeEnum.Disabled;
                var arr = mesh.SurfaceGetArrays(s);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idxV = arr[(int)Mesh.ArrayType.Index];
                int[] idx = idxV.VariantType == Variant.Type.Nil
                    ? Iota(v.Length) : idxV.AsInt32Array();
                for (int t = 0; t + 2 < idx.Length; t += 3)
                {
                    Vector3 a = rel * v[idx[t]], b = rel * v[idx[t + 1]], c = rel * v[idx[t + 2]];
                    Vector3 e1 = b - a, e2 = c - a;
                    Vector3 n = e1.Cross(e2);
                    if (!both && dir.Dot(n) <= 0) continue;        // face tournée ailleurs
                    // Möller–Trumbore
                    Vector3 p = dir.Cross(e2);
                    float det = e1.Dot(p);
                    if (Math.Abs(det) < 1e-9f) continue;
                    float inv = 1 / det;
                    Vector3 tv = o - a;
                    float u = tv.Dot(p) * inv;
                    if (u < 0 || u > 1) continue;
                    Vector3 q = tv.Cross(e1);
                    float w = dir.Dot(q) * inv;
                    if (w < 0 || u + w > 1) continue;
                    float d = e2.Dot(q) * inv;
                    if (d > 0 && d < best) { best = d; any = true; }
                }
            }
        }
        return any ? best : null;
    }

    // 0, 1, 2… : les indices d'une surface qui n'en a pas
    static int[] Iota(int n)
    {
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = i;
        return r;
    }

    /// <summary>
    /// Chaque image, pour les lanternes pendues : le pendule avance avec la vitesse
    /// du crochet, et le pivot prend sa direction, ramenée dans le repère du navire.
    /// </summary>
    public void SwingLanterns(double dt)
    {
        if (!(dt > 0)) return;
        var b = Physics.Body;
        foreach (var L in _lanterns)
        {
            var S = L.Swing;
            if (S == null) continue;
            var pp = S.Pivot.Position;
            Vec3d r = b.Quat.Rotate(new Vec3d(pp.X, pp.Y, pp.Z));        // le crochet, depuis son origine
            var w = b.AngVel;
            Vec3d hookVel = new Vec3d(w.Y * r.Z - w.Z * r.Y, w.Z * r.X - w.X * r.Z, w.X * r.Y - w.Y * r.X) + b.Vel;
            Vec3d d = S.Pend.Step(hookVel, dt);
            Vec3d dl = b.Quat.Inverted().Rotate(d);                       // dans son repère
            var to = new Vector3((float)dl.X, (float)dl.Y, (float)dl.Z).Normalized();
            S.Pivot.Quaternion = new Quaternion(Vector3.Down, to);
        }
    }

    /// <summary>Allumer ou couper les ombres des feux, sans rien reconstruire.</summary>
    public void SetLanternShadows(bool on)
    {
        LanternShadows = on;
        foreach (var L in _lanterns) L.Light.ShadowEnabled = on;
    }

    /// <summary>Refaire les feux : la lanterne du mât a été ajoutée ou retirée.</summary>
    public void RebuildLanterns() => BuildLanterns();

    /// <summary>
    /// Ses feux tels que la mer doit les voir : position monde et énergie de
    /// l'instant (flamme comprise), portée à part. Rend le nombre écrit.
    /// </summary>
    public int FillLamps(Vector4[] lamp, float[] range, int start)
    {
        int n = start;
        foreach (var L in _lanterns)
        {
            if (n >= lamp.Length) break;
            if (L.Light.LightEnergy <= 0) continue;
            var p = L.Light.GlobalPosition;
            lamp[n] = new Vector4(p.X, p.Y, p.Z, L.Light.LightEnergy);
            range[n] = L.Light.OmniRange;
            n++;
        }
        return n - start;
    }

    /// <summary>Un feu : sa lueur de près, sa marque de loin, et sa lampe.</summary>
    Lantern LanternAt(Node3D parent, Vector3 at, LanternSpec? l)
    {
        var group = new Node3D { Position = at };
        double k = Spec.L / 24 * (l?.Size ?? 1);
        int col = (int)ParseColor(l?.Color, 0xffcf7a);
        // en sRGB, comme la page affiche : voir lantern_glow.gdshader
        var srgb = new Vector3(((col >> 16) & 255) / 255f, ((col >> 8) & 255) / 255f, (col & 255) / 255f);
        var shader = GD.Load<Shader>("res://shaders/lantern_glow.gdshader");
        ShaderMaterial Sprite(double size, bool fixedSize)
        {
            var m = new ShaderMaterial { Shader = shader };
            m.SetShaderParameter(U.Color, srgb);
            m.SetShaderParameter(U.Size, (float)size);
            m.SetShaderParameter(U.Fixed, fixedSize ? 1f : 0f);
            m.SetShaderParameter(U.Opacity, 0f);
            group.AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = Vector2.One },
                MaterialOverride = m,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                // le panneau est déplacé dans le shader : la boîte au repos ne vaut rien
                ExtraCullMargin = (float)(fixedSize ? 1e4 : size)
            });
            return m;
        }
        var L = new Lantern
        {
            Group = group,
            Halo = Sprite(3.4 * k, false),       // la lampe, en mètres
            Mark = Sprite(0.030, true),          // le repère de position, à taille d'écran
            Seed = Rng.Randf() * 100
        };

        /* UNE BOUGIE n'est pas un feu de position : on ne la voit pas à deux
           milles, donc pas de repère de loin. Et une flamme seule en l'air ne se
           lit pas : il lui faut son bâton de cire, posé SOUS la flamme. */
        L.Candle = l?.Kind == "candle";
        if (L.Candle)
            group.AddChild(new MeshInstance3D
            {
                Mesh = Cylinder(0.022, 0.025, 0.14, 10),
                MaterialOverride = MakeHullMaterial(Hex("0xefe6cf"), 0.7f),
                Position = new Vector3(0, -0.085f, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });

        /* Une vraie flamme, pas une ampoule : elle est éclairée par une mèche dans
           une lanterne de corne, donc elle respire. Même loi de distance que la
           PointLight de la page (décroissance en carré, coupée à 26 m pour 24 m de
           coque), et la même convention d'énergie que le soleil. */
        L.Light = new OmniLight3D
        {
            LightColor = Hex("0xffb765"),
            LightEnergy = 0,
            OmniRange = (float)(26 * k),
            OmniAttenuation = 1.0f,
            /* SES OMBRES, en cube : une lanterne au pied d'un mât jette l'ombre du
               mât sur le pont, et c'est ce qui la fait lire comme une flamme posée là
               plutôt que comme un halo collé à l'image. Ni son halo ni sa bougie ne
               portent d'ombre, ni le verre des modèles (voir FindNightGlow) : une
               flamme enfermée dans sa propre lanterne ne sortait que par quatre
               fentes, ce que la page avait déjà rencontré. */
            ShadowEnabled = LanternShadows,
            OmniShadowMode = OmniLight3D.ShadowMode.Cube
        };
        group.AddChild(L.Light);
        parent.AddChild(group);
        return L;
    }

    /// <summary>
    /// LES FENÊTRES S'ALLUMENT AVEC LES FEUX, et rien n'est peint deux fois pour
    /// ça : une matière du .glb qui porte une carte ÉMISSIVE — ce qu'un nœud
    /// Émission de Blender exporte en glTF — est une matière qui a une part
    /// lumineuse. Le repli est un mot dans un nom de matière : « fenetre »,
    /// « window », « lampe »… reçoit une émissive chaude sans image. Le jour son
    /// intensité est à zéro : une vitre au soleil ne luit pas, elle reflète.
    /// </summary>
    void FindNightGlow()
    {
        _nightMats.Clear();
        if (ModelRoot == null) return;
        double gain = Spec.Model?.NightGlow ?? 1;
        if (!(gain > 0)) return;
        var seen = new HashSet<Material>();
        foreach (var (mi, _) in Meshes(this))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                if ((mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s)) is not BaseMaterial3D mat) continue;
                bool byName = GlowNames.IsMatch(mat.ResourceName ?? "");
                /* le verre et les fanaux laissent passer la lumière : pas d'ombre — et
                   CHAQUE objet qui en porte, pas le premier seulement. Le test « déjà
                   vue » passait avant : deux vitres sur quatre du Roter Löwe, qui
                   partagent la matière « glass », faisaient encore ombre, et le soleil
                   n'entrait plus dans la chambre du capitaine (signalé). */
                if (byName) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                if (!seen.Add(mat)) continue;
                bool hasMap = mat.EmissionTexture != null;
                if (!hasMap && !byName) continue;
                /* Une carte émissive est MULTIPLIÉE par la couleur émissive, que
                   l'exportateur laisse noire quand le facteur est nul : sans ce
                   blanc, la carte est là et ne donne rien. */
                mat.EmissionEnabled = true;
                if (mat.Emission.R == 0 && mat.Emission.G == 0 && mat.Emission.B == 0)
                    mat.Emission = hasMap ? Colors.White : Hex("0xffb765");
                double b = mat.EmissionEnergyMultiplier > 0 ? mat.EmissionEnergyMultiplier : 1;
                _nightMats.Add((mat, b * gain));
                mat.EmissionEnergyMultiplier = 0;
            }
        foreach (var (m, b) in _nightMats) RigLog.Add(FormattableString.Invariant($"fenêtre de nuit {m.ResourceName} force {b:F2}"));
    }

    /// <summary>
    /// Allumés seulement quand il fait assez sombre pour les vouloir. <paramref name="night"/>
    /// vient du ciel, si bien que les feux, le ciel et la couleur du soleil
    /// tournent ensemble.
    /// </summary>
    public void SetLantern(double night, double t, Vector3 camPos, NavalSim.Core.Sky sky)
    {
        double on = Math.Max(0, Math.Min(1, night));
        // ALLUMÉ OU ÉTEINT, jamais à mi-feu ; deux seuils, sinon un soleil qui
        // hésite à la limite ferait battre tout le bord
        if (on >= LightAt) _lit = true;
        else if (on <= SnuffAt) _lit = false;
        foreach (var (mat, b) in _nightMats) mat.EmissionEnergyMultiplier = (float)(_lit ? b * NightGlowGain : 0);

        /* AU LOIN, LE FEU S'EFFACE. Le repère est à taille d'ÉCRAN fixe — sans lui
           un fanal disparaît à deux milles —, mais avec un éclat fixe une voile au
           loin se lisait comme un réverbère. Passé FarFrom l'éclat tombe en
           (FarFrom/d)^FarFade et la taille comme sa racine ; la brume l'éteint à
           son tour, en racine de ce qu'elle laisse passer, une lumière perçant
           mieux la brume qu'une coque. */
        double far = 1, farSize = 1;
        double d = GlobalPosition.DistanceTo(camPos);
        if (d > FarFrom)
        {
            far = Math.Pow(FarFrom / d, FarFade);
            farSize = Math.Max(FarMinSize, Math.Sqrt(far));
        }
        var gp = GlobalPosition;
        far *= Math.Sqrt(sky.HazeTransmit(new Vec3d(camPos.X, camPos.Y, camPos.Z), new Vec3d(gp.X, gp.Y, gp.Z)));

        foreach (var L in _lanterns)
        {
            if (on <= 0.01)
            {
                L.Light.LightEnergy = 0;
                L.Halo.SetShaderParameter(U.Opacity, 0f);
                L.Mark.SetShaderParameter(U.Opacity, 0f);
                continue;
            }
            // deux battements lents déphasés se lisent comme une flamme ; un seul, comme un pouls
            // une mèche nue dans les courants d'air d'une chambre : plus vive et moins régulière ;
            // une bougie enfermée dans une lanterne pendue brûle comme une lanterne
            double flick = L.Candle && L.Swing == null
                ? 0.80 + 0.12 * Math.Sin(t * 9.7 + L.Seed) + 0.08 * Math.Sin(t * 23.3 + L.Seed * 1.3)
                : 0.86 + 0.14 * Math.Sin(t * 7.3 + L.Seed) + 0.06 * Math.Sin(t * 17.1 + L.Seed * 1.7);
            L.Halo.SetShaderParameter(U.Opacity, (float)(0.85 * on * flick * far));
            L.Mark.SetShaderParameter(U.Opacity, (float)(L.Candle ? 0 : 0.95 * on * flick * far));
            L.Mark.SetShaderParameter(U.Size, (float)(0.030 * farSize));
            L.Light.LightEnergy = (float)(2.6 * on * flick);
        }
    }
}

