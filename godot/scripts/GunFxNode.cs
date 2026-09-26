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
    /// <summary>Les LANGUES de feu, qui ont leur propre forme et leur propre réserve.</summary>
    const int MaxFlame = 384;
    const double Lag = 0.30;          // la part du vent qu'un nuage de poudre prend

    enum Kind { Flash, Smoke, Fire, Soot, Spark, Mist }

    struct Puff
    {
        public Kind K;
        public Vector3 P, V;
        public double T, Life, Drag, Lift, Spin, Rot, Flash, Floor, S0, S1;
        public bool Gravity, HasFloor;
        public Vector3 Col;               // la couleur de base, linéaire
    }

    readonly List<Puff> _add = new(), _powder = new(), _soot = new(), _flame = new();
    MultiMesh _mmAdd = null!, _mmPowder = null!, _mmSoot = null!, _mmBall = null!, _mmFlame = null!;
    readonly OmniLight3D[] _gunLamps = new OmniLight3D[4], _blastLamps = new OmniLight3D[3];
    /// <summary>Une par coque qui peut brûler — autant que la flotte en porte.</summary>
    readonly OmniLight3D[] _fireLamps = new OmniLight3D[NavalSim.Core.Config.MaxShips];
    readonly (double T, double Life, double Peak)[] _gunLamp = new (double, double, double)[4], _blastLamp = new (double, double, double)[3];
    int _gunLampI, _blastLampI;
    readonly Random _rng = new();
    readonly List<(double T, Vector3 At, double Size)> _queue = new();
    Vector3 _lit = Vector3.One;

    public readonly List<ShaderMaterial> Hazed = new();
    /// <summary>La fumée de la soute, que la brume des spectres emprunte.</summary>
    public ImageTexture Smoke { get; private set; } = null!;
    /// <summary>Les planches de la soute : les éclats, coupés autrement.</summary>
    public SplinterNode? Timber;

    float R() => (float)_rng.NextDouble();

    public override void _Ready()
    {
        var quad = new QuadMesh { Size = Vector2.One };
        _mmAdd = Pool(quad, "res://shaders/puff_add.gdshader", GlowTexture(), MaxAdd);
        _mmPowder = Pool(quad, "res://shaders/puff_mix.gdshader", PowderTexture(), MaxPowder);
        _mmSoot = Pool(quad, "res://shaders/puff_mix.gdshader", Smoke = SmokeTexture(), MaxSoot);
        /* LE FEU A SA PROPRE FORME. Les bouffées additives partageaient une seule
           texture — un disque —, ce qui va pour une boule de feu et pour une
           étincelle, et pas du tout pour ce qui BRÛLE : une flamme est une langue,
           haute, pointue, et l'œil la reconnaît à sa silhouette bien avant sa
           couleur. Un second bassin, donc, avec sa texture à lui. */
        _mmFlame = Pool(quad, "res://shaders/puff_add.gdshader", FlameTexture(), MaxFlame);

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
        /* UNE LAMPE D'INCENDIE PAR COQUE POSSIBLE, créée ici et jamais ajoutée ni
           retirée ensuite : un feu dure des minutes, et la scène ne peut pas voir
           son nombre de lumières changer en jeu. */
        for (int i = 0; i < _fireLamps.Length; i++)
        {
            _fireLamps[i] = new OmniLight3D
            {
                LightColor = new Color(1, 0x8e / 255f, 0x3a / 255f),
                LightEnergy = 0, OmniAttenuation = 1.0f, ShadowEnabled = false, Visible = false
            };
            AddChild(_fireLamps[i]);
        }
    }

    /// <summary>
    /// CE QUE LE COUP ÉCLAIRE DE SON PROPRE BORD : énergie, portée en mètres,
    /// durée en secondes. Réglé dans settings.json → gunnery.
    /// </summary>
    public double FlashEnergy = 34, FlashRange = 26, FlashLife = 0.22;

    readonly List<ShaderMaterial> _puffMats = new();

    /// <summary>
    /// L'ŒIL EST PASSÉ SOUS L'EAU, OU EN EST SORTI.
    ///
    /// De dessous, le feu des canons avait disparu, et c'était une affaire
    /// d'ORDRE et non d'opacité. La mer est opaque (ALPHA = 1) et montre ce qui
    /// est au-delà en LISANT L'ÉCRAN déjà dessiné : tout ce qui vient après elle
    /// n'y est pas, donc rien de ce qui est transparent ne traverse la fenêtre de
    /// Snell. Les bouffées, qui sont transparentes, se dessinaient après.
    ///
    /// On ne peut pas les mettre devant une fois pour toutes : au-dessus de l'eau
    /// la mer, dessinée ensuite, les recouvrirait — elles n'écrivent pas de
    /// profondeur, et la mer n'a donc rien contre quoi se faire refuser. On les
    /// fait donc passer AVANT la mer (priorité −3, la mer est à −1) seulement
    /// quand l'œil est dessous, où c'est exactement ce qu'il faut : la mer les
    /// recouvre bien, mais après les avoir lues dans l'écran et teintées de son
    /// épaisseur d'eau — ce qui est ce que l'on voit d'une flamme depuis le fond.
    /// </summary>
    public void Submerged(bool under)
    {
        if (under == _under) return;
        _under = under;
        foreach (var m in _puffMats) m.RenderPriority = under ? -3 : 0;
    }
    bool _under;

    static readonly Aabb Big = new(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f));

    MultiMesh Pool(Mesh quad, string shader, Texture2D tex, int max)
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>(shader) };
        mat.SetShaderParameter("u_tex", tex);
        _puffMats.Add(mat);
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
        L.OmniRange = (float)FlashRange * fk;
        /* La page règle 1 600·k² candelas sur three ; l'énergie de Godot est une
           autre échelle, et le tout se règle à l'œil dans settings.json. */
        _gunLamp[li] = (0, FlashLife, FlashEnergy * k * k);
        L.LightEnergy = (float)_gunLamp[li].Peak;
    }

    /// <summary>
    /// LE SOUFFLE D'UNE BALEINE : une gerbe de brume de quatre à cinq mètres,
    /// jetée vite par l'évent puis suspendue, que le vent emporte en entier —
    /// de la vapeur tiède et de l'eau pulvérisée, pas de la fumée froide. Les
    /// mêmes bouffées que la poudre, blanches, et dans le vent tout entier.
    /// </summary>
    public void Spout(Vector3 at, Vector3 dir, double k = 1)
    {
        float fk = (float)k;
        for (int i = 0; i < 14; i++)
        {
            float up = 0.35f + 0.65f * R();
            Add(_powder, new Puff
            {
                K = Kind.Mist, T = -i * 0.03, Life = 3.5 + R() * 2.5,
                P = at + dir * (0.3f * fk),
                V = dir * ((7 + R() * 5) * up * fk) + new Vector3((R() - 0.5f) * 1.6f, (R() - 0.3f) * 1.2f, (R() - 0.5f) * 1.6f) * fk,
                Drag = 1.6, Lift = 0.25, Spin = (R() - 0.5) * 0.8,
                S0 = (0.5 + R() * 0.4) * k, S1 = (2.4 + R() * 1.6) * k, Rot = R() * 6.2832
            }, MaxPowder);
        }
    }

    /* ------------------------------------------------------------------ */
    /*  LA SOUTE                                                           */
    /* ------------------------------------------------------------------ */

    /// <summary>Faire partir une charge plus tard : une soute ne saute pas d'un coup net.</summary>
    public void BlastIn(double delay, Vector3 at, double size) => _queue.Add((-Math.Max(0, delay), at, size));

    /* ------------------------------------------------------------------ */
    /*  UN FOYER D'INCENDIE                                                */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// CE QUI BRÛLE À BORD — une flamme qui monte, sa fumée, et la lumière qu'elle
    /// jette. <paramref name="heat"/> de 0 à 1 mène tout : le nombre de bouffées,
    /// leur taille, leur vitesse et l'éclat de la lampe.
    ///
    /// Ce n'est PAS la boule de feu d'une explosion, qui part en tous sens et meurt
    /// en une seconde : un incendie MONTE, lentement, et sa fumée est noire et
    /// grasse — du goudron, du chanvre et de la toile, pas de la poudre. Les
    /// bouffées sont donc lentes, hautes, et il y en a peu par image : c'est leur
    /// PERSISTANCE qui fait la colonne, pas leur nombre.
    ///
    /// <paramref name="dt"/> sert à en semer un compte juste quelle que soit la
    /// cadence d'images — un feu qui fume deux fois plus sur une machine deux fois
    /// plus rapide serait une faute de la même famille que les vitesses par image.
    /// </summary>
    public void Burn(Vector3 at, double heat, double dt, double scale = 1)
    {
        if (heat <= 0.01) return;
        double k = Math.Max(0.35, scale);
        float fk = (float)k;
        // combien de bouffées cette image mérite : un feu bien pris en sème une dizaine par seconde
        _burnDue += (3 + 9 * heat) * dt;
        int n = (int)_burnDue;
        _burnDue -= n;
        for (int i = 0; i < n && i < 6; i++)
        {
            float a2 = R() * Mathf.Tau, r = R() * 1.2f * fk;
            var p0 = at + new Vector3(Mathf.Cos(a2) * r, 0, Mathf.Sin(a2) * r);
            /* LA FLAMME : une LANGUE, courte, qui monte droit et s'éteint vite.
               Elle se tient DEBOUT — un angle au hasard ferait des flammes
               couchées et même à l'envers, ce qui est la seule chose qu'une
               flamme ne fait jamais. Un huitième de radian de tremblement suffit
               à ce qu'elles ne soient pas toutes parallèles. */
            Add(_flame, new Puff
            {
                K = Kind.Fire, T = 0, Life = 0.45 + R() * 0.5,
                P = p0,
                V = new Vector3((R() - 0.5f) * 0.9f, (1.6f + R() * 2.2f) * (0.5f + (float)heat), (R() - 0.5f) * 0.9f) * fk,
                Drag = 1.4,
                S0 = (0.5 + 1.4 * heat) * k, S1 = (1.6 + 3.2 * heat) * k,
                Col = Lin(0xffc46a), Rot = (R() - 0.5) * 0.25
            }, MaxFlame);
            // LA FUMÉE : noire, lente, et elle vit vingt fois plus — c'est elle qu'on voit d'un mille
            if (R() < 0.7f)
                Add(_soot, new Puff
                {
                    K = Kind.Soot, T = 0, Life = 7 + R() * 9,
                    P = p0 + new Vector3(0, 0.6f * fk, 0),
                    V = new Vector3((R() - 0.5f) * 0.8f, 2.2f + R() * 2.4f, (R() - 0.5f) * 0.8f) * fk,
                    Lift = 0.35 * k, Drag = 0.5,
                    S0 = (0.8 + 1.6 * heat) * k, S1 = (5 + 7 * heat) * k,
                    Col = Lin(0x2b2724), Rot = R() * 6.2832
                }, MaxSoot);
        }
    }
    double _burnDue;

    /// <summary>
    /// LA BRAISE QUI MONTE D'UNE VOILE QUI SE MANGE — des flammèches, et non un
    /// foyer.
    ///
    /// Une toile qui brûle ne fait pas la colonne d'un incendie de pont : elle
    /// lâche des ESCARBILLES, de la braise légère que le tirage emporte vers le
    /// haut et que le vent couche. C'est ce qu'on voit d'une voilure en feu, et
    /// c'est joli parce que ça monte vite et que ça s'éteint en l'air.
    ///
    /// Semées LE LONG de la lisière qui ronge — <paramref name="span"/> est la
    /// demi-largeur de la voile —, et non en un point : le feu mange la toile sur
    /// toute sa laize à la fois, et une gerbe unique se lirait comme une torche
    /// plantée derrière elle.
    /// </summary>
    public void Embers(Vector3 at, double span, double heat, double dt, double scale = 1)
    {
        if (heat <= 0.01) return;
        double k = Math.Max(0.35, scale);
        float fk = (float)k;
        _emberDue += (6 + 22 * heat) * dt;
        int n = (int)_emberDue;
        _emberDue -= n;
        for (int i = 0; i < n && i < 8; i++)
        {
            var p0 = at + new Vector3((float)((R() - 0.5) * 2 * span), (R() - 0.5f) * 0.4f * fk,
                                      (R() - 0.5f) * 0.3f * fk);
            /* ELLE MONTE ET ELLE PÈSE. La gravité la reprend au bout de sa course,
               donc elle décrit l'arc court d'une braise et non la ligne droite
               d'une fusée — c'est l'arc qui la rend légère à l'œil. */
            Add(_add, new Puff
            {
                K = Kind.Spark, T = 0, Life = 1.1 + R() * 1.4, P = p0, Gravity = true,
                V = new Vector3((R() - 0.5f) * 1.6f, 3.0f + R() * 4.5f, (R() - 0.5f) * 1.2f) * fk,
                S0 = 0.30 * k, S1 = 0.10 * k, Col = Lin(0xffa24a), Rot = R() * 6.2832
            }, MaxAdd);
        }
    }
    double _emberDue;

    /// <summary>
    /// LA TOILE QUI LÈCHE — les flammes d'une voile en feu.
    ///
    /// Trois choses les séparent d'un foyer de pont, et les trois comptent.
    ///
    /// Elles sont semées SUR TOUTE LA LAIZE, le long de l'axe qu'on leur donne —
    /// celui de la vergue, dans le monde. Un foyer répand ses flammes dans un
    /// disque horizontal, ce qui est juste pour un tas de bois qui brûle sur un
    /// pont ; une voile brûle sur une LIGNE, celle de son front, et des flammes
    /// en rond derrière elle se lisaient comme une torche accrochée au mât.
    ///
    /// Elles sont NOMBREUSES et COURTES. Une toile de lin n'a pas de quoi
    /// nourrir une colonne : elle s'enflamme d'un coup, lèche et s'éteint. Ce
    /// qu'on veut voir est un rideau qui frémit sur toute la largeur, pas six
    /// langues isolées — d'où quatre fois le débit d'un foyer et la moitié de la
    /// vie.
    ///
    /// Elles montent DROIT et collent au tissu : le hasard qu'on leur laisse est
    /// dans la laize, à peine dans l'épaisseur. Une flamme qui s'écarte de la
    /// toile ne la lèche plus, elle flotte à côté.
    /// </summary>
    public void SailFire(Vector3 at, Vector3 axis, double span, double heat, double dt, double scale = 1)
    {
        if (heat <= 0.01) return;
        double k = Math.Max(0.35, scale);
        float fk = (float)k;
        _sailFlameDue += (10 + 26 * heat) * dt;
        int n = (int)_sailFlameDue;
        _sailFlameDue -= n;
        var ax = axis.LengthSquared() > 1e-4f ? axis.Normalized() : Vector3.Right;
        for (int i = 0; i < n && i < 9; i++)
        {
            var p0 = at + ax * ((R() - 0.5f) * 2f * (float)span)
                        + new Vector3((R() - 0.5f) * 0.22f, (R() - 0.6f) * 0.5f, (R() - 0.5f) * 0.22f) * fk;
            Add(_flame, new Puff
            {
                K = Kind.Fire, T = 0, Life = 0.30 + R() * 0.40,
                P = p0,
                V = ax * ((R() - 0.5f) * 0.8f * fk)
                  + new Vector3(0, (2.0f + R() * 2.4f) * (0.55f + (float)heat), 0) * fk,
                Drag = 1.7,
                S0 = (0.32 + 0.85 * heat) * k, S1 = (1.05 + 2.1 * heat) * k,
                Col = Lin(0xffb055), Rot = (R() - 0.5) * 0.22
            }, MaxFlame);
        }
    }
    double _sailFlameDue;

    /// <summary>
    /// LA LUMIÈRE D'UN INCENDIE — une par navire, posée sur son pire foyer.
    ///
    /// Elle est créée à l'armement et ne fait que changer d'énergie et de place,
    /// comme celles des canons : un feu qui dure des minutes ne peut pas se
    /// permettre d'ajouter une lumière à la scène. Orange et BASSE, parce qu'un
    /// incendie éclaire d'en dessous — c'est ce qui rend les visages et la voilure
    /// si étranges sur les peintures de combat nocturne.
    /// </summary>
    public void BurnLight(int slot, Vector3 at, double heat, double scale = 1)
    {
        if (slot < 0 || slot >= _fireLamps.Length) return;
        var L = _fireLamps[slot];
        if (heat <= 0.01) { L.LightEnergy = 0; L.Visible = false; return; }
        L.Visible = true;
        L.Position = at;
        L.OmniRange = (float)(14 * Math.Max(0.35, scale) * (0.5 + heat));
        // elle respire : un brasier n'est pas une lampe
        double flick = 0.82 + 0.18 * Math.Sin(_burnT * 8.3 + slot) + 0.10 * Math.Sin(_burnT * 19.7 + slot * 2.1);
        L.LightEnergy = (float)(FireLight * heat * heat * flick);
    }
    double _burnT;

    /// <summary>L'éclat d'un incendie bien pris — settings.json → fire.light.</summary>
    public double FireLight = 26;

    /// <summary>Éteindre la lampe d'un navire qui ne brûle plus.</summary>
    public void BurnOut(int slot)
    {
        if (slot < 0 || slot >= _fireLamps.Length) return;
        _fireLamps[slot].LightEnergy = 0;
        _fireLamps[slot].Visible = false;
    }

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
        _burnT += dt;
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
        StepPuffs(_flame, _mmFlame, dt, wind);
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
                else if (p.K == Kind.Mist)
                {
                    float kd = (float)Math.Min(1, p.Drag * dt);
                    p.V.X += ((float)wind.X - p.V.X) * kd;
                    p.V.Z += ((float)wind.Z - p.V.Z) * kd;
                    p.V.Y += ((float)p.Lift - p.V.Y) * kd;
                }
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
            case Kind.Mist:
            {
                // blanche, qui réfléchit le ciel ; dense à la sortie, puis elle se défait
                double a = Math.Min(1, p.T / 0.15) * Math.Pow(1 - u, 1.3) * 0.42;
                var L = _lit;
                return (new Vector3(0.95f * L.X, 0.96f * L.Y, 0.98f * L.Z), a);
            }
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
        for (int i = 0; i < _flame.Count; i++) { var p = _flame[i]; p.P -= d; _flame[i] = p; }
        for (int i = 0; i < _queue.Count; i++) { var q = _queue[i]; q.At -= d; _queue[i] = q; }
        foreach (var L in _gunLamps) L.Position -= d;
        foreach (var L in _blastLamps) L.Position -= d;
        foreach (var L in _fireLamps) L.Position -= d;
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

    /* UN DISQUE QU'ON MORD dans l'image — « destination-out » de la page, mais
       ESTOMPÉ. Découpée net comme sur le canvas, chaque encoche était un cercle à
       bord dur : au bord d'un banc, là où peu de bouffées se recouvrent, on voyait la
       mer à travers une couronne de trous ronds tous de la même taille, et ils se
       lisaient comme des disques sombres posés sur l'eau — signalé à l'usage. Estompée
       sur la moitié de son rayon, l'encoche garde la silhouette déchirée et perd son
       bord : il n'y a plus de cercle à lire. */
    static void Bite(float[] a, int s, float cx, float cy, float r)
    {
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float u = Math.Clamp((r - d) / (0.5f * r), 0, 1);
                float cov = u * u * (3 - 2 * u);
                a[y * s + x] *= 1 - cov;
            }
    }

    /* LA LUEUR (Naval.glowTexture) : blanc au cœur, puis chaud, puis ambre, puis
       rien — en couleur, celle-ci. */
    /// <summary>
    /// UNE LANGUE DE FEU, peinte plutôt que chargée — comme tout ce que le code
    /// peut dessiner.
    ///
    /// Large et ronde au pied, effilée en pointe au sommet, les bords ondulés :
    /// c'est la SILHOUETTE qu'on reconnaît, bien avant la couleur. Le cœur est
    /// blanc-jaune et la lisière orange sombre, parce qu'une flamme est le plus
    /// chaude au milieu de sa base et se refroidit en montant.
    ///
    /// Dessinée haute dans un carré, le pied en BAS : la bouffée reste un sprite
    /// carré, et c'est le vide de part et d'autre qui lui donne son élancement,
    /// sans qu'on ait à porter une seconde dimension jusqu'au shader.
    /// </summary>
    static ImageTexture FlameTexture()
    {
        const int s = 64;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                // u : 0 au pied, 1 à la pointe ; v : de −1 à 1 en travers
                float u = 1f - (y + 0.5f) / s;
                float v = ((x + 0.5f) / s - 0.5f) * 2f;
                /* SA LARGEUR : pleine au tiers de sa hauteur, nulle à la pointe et
                   resserrée au pied — une flamme n'est pas un triangle, elle a un
                   ventre. Et deux ondulations en travers, pour que la lisière ne
                   soit pas un arc de cercle. */
                float ventre = MathF.Sin(MathF.Pow(u, 0.62f) * MathF.PI);
                float onde = 1f + 0.10f * MathF.Sin(u * 11f) + 0.06f * MathF.Sin(u * 23f + 1.7f);
                float w = ventre * onde * 0.92f;
                float d = w > 0.001f ? MathF.Abs(v) / w : 9f;
                if (d >= 1f) { img.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
                /* LE CŒUR EST BLANC, la lisière orange, et tout s'éteint vers la
                   pointe : le haut d'une flamme est déjà de la fumée chaude. */
                float coeur = MathF.Pow(1f - d, 2.2f) * (1f - u * 0.72f);
                float a2 = MathF.Pow(1f - d, 1.35f) * (1f - MathF.Pow(u, 1.8f));
                var c = new Color(1f,
                                  Math.Clamp(0.42f + 0.58f * coeur, 0f, 1f),
                                  Math.Clamp(0.08f + 0.80f * coeur * coeur, 0f, 1f),
                                  Math.Clamp(a2, 0f, 1f));
                img.SetPixel(x, y, c);
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

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
