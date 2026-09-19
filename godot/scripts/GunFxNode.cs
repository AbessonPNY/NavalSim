using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// CE QUE LES PIÈCES ET LA SOUTE MONTRENT — le dessin de guns.js et d'explosion.js.
/// Le noyau (<see cref="Gunnery"/>) décide et fait voler ; ceci dessine.
///
/// UN COUP DE CANON N'EST PAS UNE PETITE EXPLOSION. Une explosion est une boule
/// qui grandit dans tous les sens et MONTE ; un canon est un JET : les gaz
/// quittent la bouche de côté à une vitesse énorme, sont arrêtés net par l'air en
/// quelques mètres, puis s'enroulent en un gros nuage qui ne va nulle part — sauf
/// sous le vent. La fumée de poudre, lente, épaisse et durable, fait que le navire
/// SORT DE SOUS ELLE en laissant une ligne de nuages là où chaque pièce a parlé.
/// Elle ne décroît donc pas vers l'arrêt : elle se relâche vers une PART de la
/// vitesse de l'air, une ligne qui donne la dérive, le banc sous le vent et le
/// navire qui s'en dégage, sans aucun cas pour aucun d'eux.
///
/// Tout en MultiMesh : les flammes (additives), la fumée de poudre, la fumée
/// noire de la soute, les boulets — quatre appels de rendu pour toute la flotte.
/// Les lampes sont une RÉSERVE créée au départ et commutée par l'intensité : une
/// lumière ajoutée ou retirée en jeu ferait tout recompiler.
/// </summary>
public partial class GunFxNode : Node3D
{
    const int MaxAdd = 512, MaxPowder = 1536, MaxSoot = 256, MaxBalls = 96;
    const double Lag = 0.30;          // la part du vent qu'un nuage de poudre prend

    enum Kind { Flash, Smoke, Fire, Soot, Spark }

    struct Puff
    {
        public Kind K;
        public Vector3 P, V;
        public double T, Life, Drag, Lift, Spin, Rot, Flash, Floor, S0, S1;
        public bool Gravity, HasFloor;
        public Vector3 Col;               // la couleur de base, linéaire
    }

    readonly List<Puff> _add = new(), _powder = new(), _soot = new();
    MultiMesh _mmAdd = null!, _mmPowder = null!, _mmSoot = null!, _mmBall = null!;
    readonly OmniLight3D[] _gunLamps = new OmniLight3D[4], _blastLamps = new OmniLight3D[3];
    readonly (double T, double Life, double Peak)[] _gunLamp = new (double, double, double)[4], _blastLamp = new (double, double, double)[3];
    int _gunLampI, _blastLampI;
    readonly Random _rng = new();
    readonly List<(double T, Vector3 At, double Size)> _queue = new();
    Vector3 _lit = Vector3.One;

    public readonly List<ShaderMaterial> Hazed = new();
    /// <summary>Les planches de la soute : les éclats, coupés autrement.</summary>
    public SplinterNode? Timber;

    float R() => (float)_rng.NextDouble();

    public override void _Ready()
    {
        var quad = new QuadMesh { Size = Vector2.One };
        _mmAdd = Pool(quad, "res://shaders/puff_add.gdshader", GlowTexture(), MaxAdd);
        _mmPowder = Pool(quad, "res://shaders/puff_mix.gdshader", PowderTexture(), MaxPowder);
        _mmSoot = Pool(quad, "res://shaders/puff_mix.gdshader", SmokeTexture(), MaxSoot);

        /* UN BOULET, bien plus gros que nature, et exprès : un douze livres fait onze
           centimètres, un tiers de pixel à une encablure — il n'existerait pas. Son
           VOL est exact ; seul son diamètre ment, et c'est le seul mensonge qui vaille. */
        var haze = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
        Hazed.Add(haze);
        var ballMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0x14 / 255f, 0x12 / 255f, 0x0f / 255f), Roughness = 0.62f, Metallic = 0.35f,
            NextPass = haze
        };
        _mmBall = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = MaxBalls, VisibleInstanceCount = 0,
            Mesh = new SphereMesh { Radius = 0.37f, Height = 0.74f, RadialSegments = 10, Rings = 7, Material = ballMat }
        };
        AddChild(new MultiMeshInstance3D { Multimesh = _mmBall, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = Big });
        // le boulet aussi, pour la même raison, loin sous l'eau
        _mmBall.SetInstanceTransform(0, new Transform3D(Basis.Identity, new Vector3(0, -1000, 0)));
        _mmBall.VisibleInstanceCount = 1;

        for (int i = 0; i < _gunLamps.Length; i++)
        {
            _gunLamps[i] = new OmniLight3D { LightColor = new Color(1, 0xb4 / 255f, 0x54 / 255f), LightEnergy = 0, ShadowEnabled = false };
            AddChild(_gunLamps[i]);
        }
        for (int i = 0; i < _blastLamps.Length; i++)
        {
            _blastLamps[i] = new OmniLight3D { LightColor = new Color(1, 0xc2 / 255f, 0x5a / 255f), LightEnergy = 0, ShadowEnabled = false };
            AddChild(_blastLamps[i]);
        }
    }

    static readonly Aabb Big = new(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f));

    MultiMesh Pool(Mesh quad, string shader, Texture2D tex, int max)
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>(shader) };
        mat.SetShaderParameter("u_tex", tex);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
            InstanceCount = max, VisibleInstanceCount = 0, Mesh = quad
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, CustomAabb = Big
        });
        /* COMPILÉ À L'ARMEMENT, PAS AU PREMIER COUP : une bouffée invisible (opacité
           nulle) dessinée à la première image, sans quoi le shader se compile à
           l'instant précis où la pièce parle — l'à-coup qu'on voit le mieux. */
        mm.SetInstanceTransform(0, new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.01f), new Vector3(0, -1000, 0)));
        mm.SetInstanceCustomData(0, new Color(0, 0, 0, 0));
        mm.VisibleInstanceCount = 1;
        return mm;
    }

    // une couleur « 0xrrggbb » de la page, décodée en linéaire comme le fait three
    static Vector3 Lin(int hex)
    {
        var c = Color.Color8((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex).SrgbToLinear();
        return new Vector3(c.R, c.G, c.B);
    }

    /* ------------------------------------------------------------------ */
    /*  UNE PIÈCE PART                                                     */
    /* ------------------------------------------------------------------ */

    /// <summary>La flamme, le jet, le banc et la lueur d'une pièce — fire() de guns.js, sans le boulet.</summary>
    public void Gun(Vector3 at, Vector3 outDir, double k, double floor)
    {
        float fk = (float)k;
        var sideV = outDir.Cross(Vector3.Up).Normalized();

        /* LA FLAMME — une LANGUE de feu, pas une étincelle, tirée strictement vers
           l'extérieur le long du tube et grandissant en s'éloignant : un cône, pas une
           boule. C'est elle qui fait croire que la fumée a été JETÉE. */
        for (int i = 0; i < 6; i++)
            Add(_add, new Puff
            {
                K = Kind.Flash, T = -i * 0.010, Life = 0.10 + i * 0.032,
                P = at + outDir * ((0.5f + i * 0.85f) * fk), V = outDir * ((14 - i) * fk), Drag = 5.0,
                S0 = (1.5 + i * 0.75) * k, S1 = (3.0 + i * 1.5) * k,
                Col = Lin(i < 2 ? 0xfff4d2 : 0xffc766), Rot = R() * 6.2832
            }, MaxAdd);

        /* LE JET : vite hors de la bouche et arrêté presque aussitôt — le nez à une
           dizaine de mètres au large, ce qu'un canon jette vraiment. */
        for (int i = 0; i < 7; i++)
        {
            float spread = R() - 0.5f;
            Add(_powder, new Puff
            {
                K = Kind.Smoke, T = -i * 0.018, Life = 5.5 + R() * 3.0,
                P = at + outDir * (0.4f * fk),
                V = outDir * ((16 + R() * 14) * fk) + sideV * (spread * 4 * fk) + new Vector3(0, (R() - 0.2f) * 2.5f * fk, 0),
                Drag = 3.4, Lift = 0.10 * k, Spin = (R() - 0.5) * 1.1, Flash = 1.0, Floor = floor, HasFloor = true,
                S0 = 2.4 * k, S1 = (10 + R() * 5) * k, Rot = R() * 6.2832
            }, MaxPowder);
        }

        /* LE BANC : lent, large, et vivant quatre fois plus que le jet — ce qui reste
           suspendu sur l'eau après son passage, et l'essentiel de ce qu'on regarde. */
        for (int i = 0; i < 10; i++)
        {
            float a = R() * Mathf.Tau;
            Add(_powder, new Puff
            {
                K = Kind.Smoke, T = 0.06 + R() * 0.5, Life = 17 + R() * 11,
                P = at + outDir * ((2 + R() * 7) * fk) + new Vector3((R() - 0.5f) * 4 * fk, (R() - 0.4f) * 3 * fk, (R() - 0.5f) * 6 * fk),
                V = outDir * ((1.5f + R() * 3) * fk) + new Vector3(Mathf.Cos(a) * 0.9f * fk, 0.7f + R() * 1.0f, Mathf.Sin(a) * 0.9f * fk),
                Drag = 0.7, Lift = -0.12 * k, Spin = (R() - 0.5) * 0.5, Flash = 0.5, Floor = floor, HasFloor = true,
                S0 = (3.4 + R() * 2.6) * k, S1 = (15 + R() * 12) * k, Rot = R() * 6.2832
            }, MaxPowder);
        }

        /* LA LUEUR SUR SON PROPRE BORDÉ, une vraie lumière, un peu AU LARGE de la
           bouche — dedans, elle éclairerait sa batterie à travers la coque. Bornée en
           portée : le bordé autour du sabord, le livet, la mer juste dessous. */
        int li = _gunLampI; _gunLampI = (_gunLampI + 1) % _gunLamps.Length;
        var L = _gunLamps[li];
        L.Position = at + outDir * (1.1f * fk);
        L.OmniRange = 18 * fk;
        // la page règle 1 600·k² candelas sur three ; l'énergie de Godot est une autre échelle
        _gunLamp[li] = (0, 0.10, 16 * k * k);
        L.LightEnergy = (float)_gunLamp[li].Peak;
    }

    /* ------------------------------------------------------------------ */
    /*  LA SOUTE                                                           */
    /* ------------------------------------------------------------------ */

    /// <summary>Faire partir une charge plus tard : une soute ne saute pas d'un coup net.</summary>
    public void BlastIn(double delay, Vector3 at, double size) => _queue.Add((-Math.Max(0, delay), at, size));

    /* LA SOUTE SAUTE — fire() d'explosion.js. Ce qui fait lire une explosion n'est
       pas la boule de feu, c'est l'ORDRE des choses et leurs durées : un éclat parti
       en un dixième de seconde, une boule qui grandit vite et meurt en une, une fumée
       qui monte et s'étale une demi-minute, du bois en arcs balistiques. */
    void Blast(Vector3 at, double size)
    {
        double k = Math.Max(0.4, size / 24);
        float fk = (float)k;
        int li = _blastLampI; _blastLampI = (_blastLampI + 1) % _blastLamps.Length;
        _blastLamps[li].Position = at;
        _blastLamps[li].OmniRange = 55 * fk;
        _blastLamp[li] = (0, 0.42, 90 * k * k);
        _blastLamps[li].LightEnergy = (float)_blastLamp[li].Peak;

        // LE FEU : vif, rapide, parti — chaque bouffée à son retard, la boule bout
        for (int i = 0; i < 14; i++)
        {
            float a = R() * Mathf.Tau, e = (R() - 0.3f) * 1.4f;
            Add(_add, new Puff
            {
                K = Kind.Fire, T = -R() * 0.10, Life = 0.55 + R() * 0.45, P = at,
                V = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e) + 0.5f, Mathf.Sin(a) * Mathf.Cos(e)) * ((7 + R() * 13) * fk),
                S0 = 2.5 * k, S1 = (11 + R() * 9) * k, Col = Lin(0xffd9a0), Rot = R() * 6.2832
            }, MaxAdd);
        }
        // LA FUMÉE : elle survit vingt fois au feu, monte et grandit — c'est elle qui donne l'échelle
        for (int i = 0; i < 16; i++)
        {
            float a = R() * Mathf.Tau;
            Add(_soot, new Puff
            {
                K = Kind.Soot, T = -R() * 0.8, Life = 9 + R() * 11,
                P = at + new Vector3((R() - 0.5f) * 6 * fk, R() * 3 * fk, (R() - 0.5f) * 6 * fk),
                V = new Vector3(Mathf.Cos(a) * (1.0f + R() * 1.8f), 7.0f + R() * 6.0f, Mathf.Sin(a) * (1.0f + R() * 1.8f)) * fk,
                S0 = 3 * k, S1 = (9 + R() * 8) * k, Col = Lin(0x3a3630), Rot = R() * 6.2832
            }, MaxSoot);
        }
        // et quelques vraies étincelles, qui SONT des points de lumière
        for (int i = 0; i < 14; i++)
        {
            float a = R() * Mathf.Tau, e = 0.4f + R() * 1.0f;
            Add(_add, new Puff
            {
                K = Kind.Spark, T = 0, Life = 2.2 + R() * 2.0, P = at, Gravity = true,
                V = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e)) * ((20 + R() * 30) * fk),
                S0 = 0.7 * k, S1 = 0.3 * k, Col = Lin(0xffb066), Rot = R() * 6.2832
            }, MaxAdd);
        }
        // DU BOIS en arcs balistiques : ce qu'on lit, c'est la hauteur qu'il atteint et le temps qu'il met à retomber
        Timber?.Planks(at, k);
    }

    static void Add(List<Puff> list, Puff p, int max)
    {
        if (list.Count >= max) list.RemoveAt(0);         // la plus vieille cède sa place
        list.Add(p);
    }

    /// <summary>
    /// Une image : les charges en attente, les lampes, les bouffées, les boulets.
    /// <paramref name="horizon"/> est la couleur de l'horizon : la fumée RÉFLÉCHIT.
    /// </summary>
    public void Step(double dt, Vec3d wind, Vec3d horizon, IReadOnlyList<Gunnery.Shot> shots)
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            var q = _queue[i];
            q.T += dt;
            _queue[i] = q;
            if (q.T >= 0) { Blast(q.At, q.Size); _queue.RemoveAt(i); }
        }
        // l'éclat meurt vite et inégalement : une charge ne s'éteint pas, elle s'en va
        StepLamps(_gunLamps, _gunLamp, dt);
        StepLamps(_blastLamps, _blastLamp, dt);

        /* CE QU'IL Y A DE LUMIÈRE À RENVOYER. La fumée de poudre ne fait pas de
           lumière, elle la RENVOIE : jamais plus claire que ce qui tombe dessus. La
           couleur de l'horizon porte déjà l'heure, la saison et le temps. */
        double l = Math.Max(1e-3, 0.2126 * horizon.X + 0.7152 * horizon.Y + 0.0722 * horizon.Z);
        double kk = Math.Max(0.18, l / 0.85);
        _lit = new Vector3((float)(horizon.X / l * kk), (float)(horizon.Y / l * kk), (float)(horizon.Z / l * kk));

        StepPuffs(_add, _mmAdd, dt, wind);
        StepPuffs(_powder, _mmPowder, dt, wind);
        StepPuffs(_soot, _mmSoot, dt, wind);

        int n = Math.Min(shots.Count, MaxBalls);
        for (int i = 0; i < n; i++)
            _mmBall.SetInstanceTransform(i, new Transform3D(Basis.Identity,
                new Vector3((float)shots[i].P.X, (float)shots[i].P.Y, (float)shots[i].P.Z)));
        _mmBall.VisibleInstanceCount = n;
    }

    static void StepLamps(OmniLight3D[] lamps, (double T, double Life, double Peak)[] st, double dt)
    {
        for (int i = 0; i < lamps.Length; i++)
        {
            if (st[i].Life <= 0) continue;
            st[i].T += dt;
            double u = st[i].T / st[i].Life;
            lamps[i].LightEnergy = u >= 1 ? 0 : (float)(st[i].Peak * (1 - u) * (1 - u));
            if (u >= 1) st[i].Life = 0;
        }
    }

    void StepPuffs(List<Puff> list, MultiMesh mm, double dt, Vec3d wind)
    {
        int keep = 0, drawn = 0;
        float fdt = (float)dt;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            p.T += dt;
            double u = p.T / p.Life;
            if (u >= 1) continue;
            if (p.T >= 0)
            {
                if (p.Gravity) p.V.Y -= 9.81f * fdt;
                else if (p.K == Kind.Smoke || p.K == Kind.Flash)
                {
                    /* Elle se relâche vers l'AIR, pas vers zéro — et vers une PART du
                       vent seulement : froide, dense, pleine de grains non brûlés, elle
                       reste là où les pièces ont parlé. La stagnation d'abord, la
                       dérive ensuite. */
                    float kd = (float)Math.Min(1, p.Drag * dt);
                    p.V.X += ((float)(wind.X * Lag) - p.V.X) * kd;
                    p.V.Z += ((float)(wind.Z * Lag) - p.V.Z) * kd;
                    p.V.Y += ((float)p.Lift - p.V.Y) * kd;
                }
                else p.V *= (float)(1 - 1.3 * dt);         // le feu et la fumée noire, freinés par l'air
                p.P += p.V * fdt;
                /* ELLE SE COUCHE SUR L'EAU : froide et chargée, elle s'affaisse par petit
                   temps et reste sur la mer en nappe — il lui faut un plancher. */
                if (p.HasFloor && p.P.Y < p.Floor) { p.P.Y = (float)p.Floor; if (p.V.Y < 0) p.V.Y = 0; }
                p.Rot += p.Spin * dt;

                // dessinée : pas encore née, gardée mais pas montrée
                if (drawn < mm.InstanceCount)
                {
                    double size = p.S0 + (p.S1 - p.S0) * Math.Pow(u, p.K == Kind.Smoke || p.K == Kind.Flash ? 0.5 : 0.55);
                    var (col, a) = Look(p, u);
                    float s = (float)size, c = (float)Math.Cos(p.Rot), sn = (float)Math.Sin(p.Rot);
                    // la taille et l'angle dans le premier axe : voir puff.gdshaderinc
                    // la taille et l'angle dans le premier axe ; le plancher dans le troisième (voir puff.gdshaderinc)
                    var basis = new Basis(new Vector3(c * s, sn * s, 0), new Vector3(-sn * s, c * s, 0),
                                          new Vector3(p.HasFloor ? 1 : 0, (float)p.Floor, s));
                    mm.SetInstanceTransform(drawn, new Transform3D(basis, p.P));
                    mm.SetInstanceCustomData(drawn, new Color(col.X, col.Y, col.Z, (float)a));
                    drawn++;
                }
            }
            list[keep++] = p;
        }
        list.RemoveRange(keep, list.Count - keep);
        mm.VisibleInstanceCount = drawn;
    }
    /// <summary>La couleur (linéaire) et l'opacité d'une bouffée à la part u de sa vie.</summary>
    (Vector3 Col, double A) Look(in Puff p, double u)
    {
        switch (p.K)
        {
            case Kind.Flash: return (p.Col, Math.Pow(1 - u, 1.4));
            case Kind.Fire:
                // blanc → jaune → orange → éteint, bien avant la fumée
                return (new Vector3(1.0f, (float)(0.86 - 0.5 * u), (float)(0.62 - 0.55 * u)), Math.Min(1, u * 8) * Math.Pow(1 - u, 1.6));
            case Kind.Soot:
            {
                // elle s'éclaircit en s'étalant : de la suie qui refroidit, pas une tache noire
                float g = (float)(0.10 + 0.20 * u);
                return (new Vector3(g, g * 0.97f, g * 0.92f), Math.Min(1, u * 4) * (1 - u) * 0.42);
            }
            case Kind.Spark: return (p.Col, Math.Pow(1 - u, 0.8) * 0.9);
            default:
            {
                /* MINCE PAR BOUFFÉE, épaisse par accumulation : une bordée seule est un
                   banc qu'on traverse du regard, trois bordées le mur. La montée se
                   compte en SECONDES, la descente en part de vie. Grise et sale : le
                   blanc se lit comme de la vapeur. */
                double a = Math.Min(1, p.T / 0.55) * Math.Pow(1 - u, 1.15) * 0.20;
                double g = 0.62 - 0.10 * u;
                var L = _lit;
                float gr = (float)(g * L.X), gg = (float)(g * 0.99 * L.Y), gb = (float)(g * 0.95 * L.Z);
                /* LA LUMIÈRE ENTRE DANS LE NUAGE : la flamme est DEDANS, si bien qu'une
                   fraction de seconde le nuage naissant luit de l'intérieur, orange à la
                   bouche. Seul le gris est atténué par le ciel ; le feu est le sien. */
                double hot = p.Flash > 0 ? p.Flash * Math.Max(0, 1 - p.T / 0.30) : 0;
                if (hot > 0.002)
                {
                    float q = (float)(hot * hot);
                    return (new Vector3(gr + (1.00f - gr) * q, gg + (0.66f - gg) * q, gb + (0.22f - gb) * q), a * (1 + 0.75 * q));
                }
                return (new Vector3(gr, gg, gb), a);
            }
        }
    }

    public void Rebase(double dx, double dz)
    {
        var d = new Vector3((float)dx, 0, (float)dz);
        for (int i = 0; i < _add.Count; i++) { var p = _add[i]; p.P -= d; _add[i] = p; }
        for (int i = 0; i < _powder.Count; i++) { var p = _powder[i]; p.P -= d; _powder[i] = p; }
        for (int i = 0; i < _soot.Count; i++) { var p = _soot[i]; p.P -= d; _soot[i] = p; }
        for (int i = 0; i < _queue.Count; i++) { var q = _queue[i]; q.At -= d; _queue[i] = q; }
        foreach (var L in _gunLamps) L.Position -= d;
        foreach (var L in _blastLamps) L.Position -= d;
    }

    /* ------------------------------------------------------------------ */
    /*  LES TEXTURES, tirées comme la page les peint sur un canvas         */
    /* ------------------------------------------------------------------ */

    // une image blanche dont l'alpha est rempli par `a(x, y)`, en canvas « source-over »
    static ImageTexture White(int s, float[] alpha)
    {
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                img.SetPixel(x, y, new Color(1, 1, 1, Math.Clamp(alpha[y * s + x], 0, 1)));
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    static float Grad(float r, (float At, float A)[] stops)
    {
        if (r <= stops[0].At) return stops[0].A;
        for (int i = 1; i < stops.Length; i++)
            if (r <= stops[i].At)
            {
                float u = (r - stops[i - 1].At) / (stops[i].At - stops[i - 1].At);
                return stops[i - 1].A + (stops[i].A - stops[i - 1].A) * u;
            }
        return stops[^1].A;
    }

    // un disque radial posé « par-dessus » : A = a + A(1 − a)
    static void Blob(float[] a, int s, float cx, float cy, float r, (float At, float A)[] stops)
    {
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / r;
                if (d > 1) continue;
                float v = Grad(d, stops);
                a[y * s + x] = v + a[y * s + x] * (1 - v);
            }
    }

    // un disque qu'on MORD dans l'image : « destination-out »
    static void Bite(float[] a, int s, float cx, float cy, float r)
    {
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float cov = Math.Clamp(r - d + 0.5f, 0, 1);
                a[y * s + x] *= 1 - cov;
            }
    }

    /* LA LUEUR (Naval.glowTexture) : blanc au cœur, puis chaud, puis ambre, puis
       rien — en couleur, celle-ci. */
    static ImageTexture GlowTexture()
    {
        const int s = 64;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var st = new (float At, Color C)[]
        {
            (0, new Color(1, 1, 1, 1)), (0.16f, new Color(1, 228 / 255f, 170 / 255f, 0.92f)),
            (0.42f, new Color(1, 170 / 255f, 70 / 255f, 0.32f)), (1, new Color(1, 140 / 255f, 40 / 255f, 0))
        };
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float r = MathF.Sqrt((x + 0.5f - s / 2f) * (x + 0.5f - s / 2f) + (y + 0.5f - s / 2f) * (y + 0.5f - s / 2f)) / (s / 2f);
                Color c = st[^1].C;
                for (int i = 1; i < st.Length; i++)
                    if (r <= st[i].At) { c = st[i - 1].C.Lerp(st[i].C, (r - st[i - 1].At) / (st[i].At - st[i - 1].At)); break; }
                img.SetPixel(x, y, c);
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    /* LA FUMÉE DE LA SOUTE (Naval.smokeTexture) : un dégradé radial dont on mord le
       bord, pour qu'une bouffée ne soit pas un disque parfait. */
    ImageTexture SmokeTexture()
    {
        const int s = 128;
        var a = new float[s * s];
        Blob(a, s, s / 2f, s / 2f, s / 2f, new[] { (0f, 0.95f), (0.45f, 0.42f), (1f, 0f) });
        for (int i = 0; i < 26; i++)
        {
            float an = R() * 6.2832f, r = s * 0.34f + R() * s * 0.16f;
            Bite(a, s, s / 2f + MathF.Cos(an) * r, s / 2f + MathF.Sin(an) * r, s * (0.05f + R() * 0.09f));
        }
        return White(s, a);
    }

    /* LA FUMÉE DE POUDRE a sa PROPRE texture, et elle vaut ses vingt lignes : quarante
       dégradés radiaux l'un sur l'autre s'additionnent en un losange blanc bien lisse
       — une savonnette le long de son bord. Le remède, ce sont des BOSSES dans la
       texture : un corps, sept bosses de tailles différentes autour, et quelques
       encoches mordues. Tournées chacune à son angle, elles ne s'accordent jamais. */
    ImageTexture PowderTexture()
    {
        const int s = 160;
        var a = new float[s * s];
        void B(float x, float y, float r, float al) => Blob(a, s, x, y, r, new[] { (0f, al), (0.55f, al * 0.55f), (1f, 0f) });
        B(s * 0.5f, s * 0.5f, s * 0.40f, 0.72f);
        for (int i = 0; i < 7; i++)
        {
            float an = i / 7f * 6.2832f + R() * 0.7f, d = s * (0.13f + R() * 0.15f);
            B(s * 0.5f + MathF.Cos(an) * d, s * 0.5f + MathF.Sin(an) * d, s * (0.13f + R() * 0.12f), 0.42f + R() * 0.34f);
        }
        for (int i = 0; i < 9; i++)
        {
            float an = R() * 6.2832f, d = s * (0.30f + R() * 0.20f);
            Bite(a, s, s * 0.5f + MathF.Cos(an) * d, s * 0.5f + MathF.Sin(an) * d, s * (0.06f + R() * 0.10f));
        }
        return White(s, a);
    }
}
