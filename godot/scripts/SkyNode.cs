using Godot;
using System;
using NavalSim.Core;

// `Sky` existe des deux cotes : celui du noyau est l arithmetique du ciel, celui
// de Godot est la ressource de fond. On les nomme.
using CoreSky = NavalSim.Core.Sky;

namespace NavalSim;

/// <summary>
/// LE CIEL, ET CE QU'IL ÉCLAIRE.
///
/// Il porte le dôme, le soleil, l'ambiante, la brume et le gros temps — et
/// surtout il pousse LES MÊMES uniformes au dôme et à la mer. C'est la façon
/// Godot de tenir la règle du ciel unique : côté JavaScript les deux matériaux
/// partageaient les objets uniformes eux-mêmes ; ici chaque matériau a sa copie,
/// donc c'est <see cref="PushTo"/> qui garantit qu'elles ne divergent pas, et
/// c'est le seul endroit où elles sont écrites.
///
/// L'arithmétique — position du soleil, couleurs, intensités, couplage à l'état
/// de mer — vit dans <see cref="NavalSim.Core.Sky"/>, qui ne connaît pas Godot.
/// </summary>
public partial class SkyNode : Node3D
{
    public CoreSky Core { get; } = new();

    public DirectionalLight3D Sun { get; private set; } = null!;
    public Godot.Environment Env { get; private set; } = null!;
    ShaderMaterial _domeMat = null!;

    /// <summary>La latitude du monde. 13,6° N — entre la Martinique et Sainte-Lucie.</summary>
    public double Latitude = 13.60;

    /// <summary>
    /// LE DÉFILEMENT DU JOUR, en heures de ciel par minute réelle — le curseur
    /// « Défilement du jour » de la page, qui énonce UN taux : ×1, c'est une
    /// minute réelle pour une heure, donc un jour en vingt-quatre minutes ; ×2,
    /// où elle démarre, en douze. Porté d'abord à 60, soit soixante heures par
    /// minute : personne ne l'avait vu, le jour ne tournant pas encore.
    /// Zéro arrête l'horloge.
    /// </summary>
    public double DayRate = 2.0;
    public bool DayRunning => DayRate > 0;

    /// <summary>L'heure à laquelle la page commence sa journée.</summary>
    public const double StartHour = 9.5;

    /* LA PROFONDEUR DES OMBRES SUR LES NAVIRES : ce que la lumière du ciel rend
       aux faces que le soleil ne touche pas. Le moteur fait bien porter l'ombre
       du soleil (mâts sur le pont, château sur le passavant, bordé au dos du
       soleil), mais à 3,2 le ciel éclairait l'ombre presque autant que le soleil
       la lumière, et elle ne se lisait plus (signalé). Plus bas : ombres plus
       franches, et tout un peu plus sombre à l'ombre du jour ; plus haut : plus
       doux. */
    public const float AmbientGain = 1.0f;

    /* L'ÉTUDE DE LA LUMIÈRE (menu → Lumière) : trois facteurs sur les valeurs
       de la page, réglés à l'œil et gardés dans reglages.ini.
       SunStrength multiplie le soleil ; SunWarmth le tire vers un jaune chaud
       (celui d'une fin d'après-midi) sans toucher à sa luminance, et le jour
       seulement — la lune garde sa lumière froide ; SkyShade multiplie la
       lumière du ciel dans l'ombre : plus bas, plus de contraste.

       SkyShade est l'ÉCLAIRAGE AMBIANT tout entier : l'énergie ambiante seule
       ne changeait presque rien (signalé) — l'ombre était éclairée surtout par
       les REFLETS du ciel et la lumière renvoyée (SSIL), qu'elle ne touche pas.
       Il règle donc aussi la radiance du dôme (sa passe cubemap, qui nourrit
       ambiante et reflets) et l'intensité du SSIL. */
    public float SunStrength = 1.25f, SunWarmth = 0.35f, SkyShade = 0.85f;

    /* LE SOLEIL DE LA PAGE, DANS L'UNITÉ DE GODOT. three.js (r160, éclairage
       physique) divise l'éclairement direct par π — la réflectance de Lambert —,
       Godot non : un soleil de 2,1 y éclairait π fois plus fort que dans la page.
       Le bois au soleil brûlait au blanc (relevé : rouge à 1,00 sur le bordé,
       0,71 une fois ramené) et le navire paraissait pâle, sans contraste
       (signalé). La lumière du ciel, elle, était déjà dans la bonne mesure. */
    public const float SunGain = 1f / Mathf.Pi;

    /* LE CIEL FERMÉ (Overcast, 0 à 1) : sous un couvercle noir le soleil
       n'arrive plus en faisceau, il arrive DIFFUS — tout le ciel éclaire un peu,
       plus rien n'éclaire franchement. Le noyau rabattait déjà le soleil de 62 %
       en pleine tempête, mais la Force du soleil (jusqu'à ×5) passait par-dessus,
       et l'ambiante baissait avec l'orage : la coque restait éclairée comme par
       beau temps sous un ciel d'encre (signalé). Ciel fermé, donc :
       - OvercastSun : la part du soleil qui s'en va (0,75 : il en reste le quart),
         et la Force du soleil revient vers 1 — on ne renforce pas un soleil caché ;
       - OvercastSky : ce que gagne la lumière du ciel en contrepartie (+180 %
         à l'ambiante, la moitié aux reflets du dôme).
       L'ombre des coques sur l'eau s'efface avec le soleil (u_sunlit). */
    public const float OvercastSun = 0.75f, OvercastSky = 1.8f;
    /* SEUL UN CIEL VRAIMENT FERMÉ AGIT : rien sous OvercastFrom, tout à OvercastFull,
       une marche douce entre. Linéaire, une petite pluie à 30 % ramenait déjà une
       Force du soleil de 5 vers 3,8 et ôtait 40 % du soleil (signalé). */
    public const float OvercastFrom = 0.4f, OvercastFull = 0.9f;
    /// <summary>Ce qui ferme le ciel en plus de l'orage : pluie, grain, brume, couverture. 0 à 1, posé par l'hôte.</summary>
    public double Overcast;
    /// <summary>Le ciel fermé retenu : l'orage du noyau ou ce que l'hôte a posé.</summary>
    public double Gloom => Math.Clamp(Math.Max(Core.Storm, Overcast), 0, 1);
    /// <summary>Ce qui en agit sur la lumière : 0 tant que le ciel n'est pas vraiment fermé.</summary>
    public double Closed
    {
        get
        {
            double x = Math.Clamp((Gloom - OvercastFrom) / (OvercastFull - OvercastFrom), 0, 1);
            return x * x * (3 - 2 * x);
        }
    }
    /// <summary>La part du soleil qui passe encore, pour l'ombre des coques sur l'eau.</summary>
    public double Sunlit => 1 - OvercastSun * Closed;

    // --- l'éclair proche, et le grain lointain : DEUX choses différentes ---
    double _flashT;
    (double At, double A)[]? _flashQueue;
    double _farA, _farT, _farNext2 = -1;
    readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _rng.Randomize();

        var shader = GD.Load<Shader>("res://shaders/sky_dome.gdshader");
        _domeMat = new ShaderMaterial { Shader = shader };

        Env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Godot.Sky { SkyMaterial = _domeMat },
            /* L'ambiante vient du CIEL, pas d'une couleur posée à la main : c'est
               le même ciel, donc elle porte l'heure, la saison et le gros temps
               sans qu'on ait à les lui répéter. Trois conséquences gratuites —
               l'ambiante devient DIRECTIONNELLE, le spéculaire image apparaît, et
               la nuit suit toute seule. */
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            /* PAS DE COURBE FILMIQUE, et c est une decision de portage.

               three.js n applique AUCUN tonemapping : il ecrit des valeurs
               lineaires que la sortie encode en sRGB, point. Toutes les couleurs
               de ce projet — horizon, zenith, u_deep, u_shallow, l ecume, la
               fumee — ont donc ete reglees CONTRE cette chaine-la, sur des
               annees de mesures consignees.

               ACES releve les basses lumieres et desature : appliquee par-dessus,
               elle delavait le ciel et blanchissait la mer. Elle est meilleure
               dans l absolu pour une scene HDR ; elle est fausse ici, parce que
               ce n est pas la chaine pour laquelle les chiffres ont ete trouves.
               Linear = la meme sortie que three.js. */
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,

            /* L'OCCLUSION AMBIANTE, et c'est la même que celle de ssao.js, en
               mieux placée. La page la limitait au NAVIRE pour une raison
               écrite : son passage de profondeur dessinait la mer à plat, ce qui
               faussait l'occlusion exactement là où la coque touche l'eau. Ici le
               résultat est le même par une autre voie : la mer est une matière
               TRANSPARENTE (elle relit l'image pour montrer la coque sous l'eau), et
               l'occlusion de Godot ne voit que l'opaque — donc le navire seul.

               Ses autres choix restent : 2,4 m de rayon — l'échelle de ce qui doit
               occulter, un pavois, une écoutille — et la demi-résolution
               (project.godot), l'occlusion étant basse fréquence par nature. Et
               elle ne touche QUE la lumière indirecte : une planche au fond d'une
               écoutille que le soleil atteint encore est aussi claire que le pont ;
               ce qu'elle perd, c'est le ciel. light_affect à zéro, c'est cela. */
            SsaoEnabled = true,
            SsaoRadius = 2.4f,
            SsaoIntensity = 2.0f,
            SsaoPower = 1.5f,
            SsaoLightAffect = 0.0f,
            SsaoAOChannelAffect = 0.0f,

            /* L'ILLUMINATION GLOBALE, et c'est un AJOUT : la page n'en avait pas.
               En espace écran (SSIL) plutôt que SDFGI ou VoxelGI, qui supposent une
               géométrie immobile — le navire ne l'est jamais, et la mer, déplacée
               dans son shader, n'existe pour eux que plate. Le SSIL relit l'image
               de l'image d'avant : la toile au soleil éclaire le pont, la mer
               verdit le bas du bordé, ce que l'ambiante du ciel seule ne fait pas.
               Toutes les couleurs du projet ont été réglées SANS lui : il éclaire,
               donc il se juge à l'œil et se coupe d'une touche (G). */
            SsilEnabled = true,
            SsilRadius = 5.0f,
            SsilIntensity = 1.0f,
            SsilSharpness = 0.98f,
            SsilNormalRejection = 1.0f
        };
        AddChild(new WorldEnvironment { Environment = Env });

        Sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            ShadowNormalBias = 1.4f,   // la toile est d'épaisseur nulle
            DirectionalShadowMaxDistance = 320
        };
        AddChild(Sun);

        // la journée commence où la page commence la sienne
        Core.SetTimeOfDay(StartHour, Latitude);
        Apply();
    }

    /// <summary>Recopie l'état du noyau dans le moteur et dans le dôme.</summary>
    public void Apply()
    {
        /* LA LUMIÈRE DIRECTE VIENT DE LA LUNE LA NUIT, quand elle est levée : c'est
           la seule lampe du ciel, et ses ombres doivent tomber de son côté. */
        var d = Core.LightDir;
        var dir = new Vector3((float)d.X, (float)d.Y, (float)d.Z);

        // une lumière directionnelle regarde le long de son -Z : elle est donc
        // placée du côté de l'astre et braquée sur l'origine
        Sun.Position = dir * 400f;
        // une lune au zénith rendrait « haut » colinéaire à la visée : autre repère alors
        Sun.LookAt(Vector3.Zero, Mathf.Abs(dir.Y) > 0.999f ? Vector3.Forward : Vector3.Up);

        var col = new Color((float)Core.SunColor.R, (float)Core.SunColor.G, (float)Core.SunColor.B);
        if (Core.SunElevDeg > 0)
        {
            // un jaune chaud à luminance égale : la couleur change, pas la force
            var warm = new Color(1.0f, 0.80f, 0.52f);
            float l0 = 0.2126f * col.R + 0.7152f * col.G + 0.0722f * col.B;
            var tinted = new Color(col.R * warm.R, col.G * warm.G, col.B * warm.B);
            float l1 = Math.Max(1e-4f, 0.2126f * tinted.R + 0.7152f * tinted.G + 0.0722f * tinted.B);
            tinted = new Color(tinted.R * l0 / l1, tinted.G * l0 / l1, tinted.B * l0 / l1);
            col = col.Lerp(tinted, Math.Clamp(SunWarmth, 0, 1));
        }
        Sun.LightColor = col;
        double g = Core.SunElevDeg > 0 ? Closed : 0;
        double strength = Core.SunElevDeg > 0 ? SunStrength + (1 - SunStrength) * g : 1;
        Sun.LightEnergy = (float)(Core.SunIntensity * SunGain * strength * (1 - OvercastSun * g));

        /* L'ambiante est celle du ciel, mais son ÉNERGIE porte l'éclair : c'est
           ce qui fait que le pont et la toile l'attrapent. Un éclair qui
           n'éclairerait que le ciel se lit comme un fond d'écran qui clignote. */
        Env.AmbientLightEnergy = (float)Math.Max(0.02, Core.HemiIntensity * AmbientGain * SkyShade * (1 + OvercastSky * g));
        Env.SsilIntensity = Math.Max(0, SkyShade);
        _domeMat.SetShaderParameter("u_env_gain", (float)Math.Max(0, SkyShade * (1 + 0.5 * OvercastSky * g)));

        PushTo(_domeMat);
        // le disque de la lune et sa phase n'appartiennent qu'au dôme
        _domeMat.SetShaderParameter(U.MoonDisc, (float)Core.DomeMoonLit);
        _domeMat.SetShaderParameter(U.MoonPhase, (float)Core.MoonPhaseU);
    }

    /// <summary>
    /// LE MÊME CIEL, POUSSÉ PARTOUT. Le dôme, la mer, et tout ce qui respire le
    /// même air. Ajouter un matériau à la liste des appelants est le seul geste
    /// qu'il faut ne pas oublier — et c'est pour cela que tout est écrit ici et
    /// nulle part ailleurs.
    /// </summary>
    /// <summary>Le soleil à sa vraie luminance, pour l'exposition automatique (voir sky_dome).</summary>
    public void SetDazzle(bool on) => _domeMat.SetShaderParameter(U.Dazzle, on ? 1f : 0f);

    public void PushTo(ShaderMaterial m)
    {
        if (m == null) return;
        var d = Core.SunDir;
        m.SetShaderParameter(U.Sun, new Vector3((float)d.X, (float)d.Y, (float)d.Z));
        // Vector3 et non Color : ces valeurs sont deja lineaires, et passer par
        // un Color sur un uniforme `source_color` les convertirait une fois de trop.
        m.SetShaderParameter(U.Zenith, new Vector3((float)Core.Zenith.R, (float)Core.Zenith.G, (float)Core.Zenith.B));
        m.SetShaderParameter(U.Horizon, new Vector3((float)Core.Horizon.R, (float)Core.Horizon.G, (float)Core.Horizon.B));
        m.SetShaderParameter(U.Storm, (float)Core.Storm);
        m.SetShaderParameter(U.Haze, (float)Core.Haze);
        m.SetShaderParameter(U.HazeH, (float)Core.HazeHeight);
        m.SetShaderParameter(U.Flash, (float)Core.Flash);
        // la nuit sur l'eau : sa lumière propre, et la lune avec sa route
        m.SetShaderParameter(U.WaterLight, (float)Core.WaterLight);
        var md = Core.MoonDir;
        m.SetShaderParameter(U.Moon, new Vector3((float)md.X, (float)md.Y, (float)md.Z));
        m.SetShaderParameter(U.MoonLit, (float)Core.SeaMoonLit);
        // la lumière qui traverse la toile suit celle du soleil : sa couleur,
        // à la moitié de son intensité, comme uSunCol dans la page
        double k = Core.SunIntensity * 0.5;
        m.SetShaderParameter(U.SunCol, new Vector3(
            (float)(Core.SunColor.R * k), (float)(Core.SunColor.G * k), (float)(Core.SunColor.B * k)));
        // le grain qu'on voit venir, et ses éclairs : le même quart noir sur le dôme, la mer et la brume
        m.SetShaderParameter(U.StormDir, _loomDir);
        m.SetShaderParameter(U.StormLoom, (float)_loom);
        m.SetShaderParameter(U.StormFlashDir, _farDir);
        m.SetShaderParameter(U.StormFlash, (float)_farA);
    }

    public void SetCloud(ShaderMaterial m, double amount, double skyTime)
    {
        m?.SetShaderParameter(U.Cloud, (float)amount);
        m?.SetShaderParameter(U.SkyTime, (float)skyTime);
    }

    /// <summary>
    /// Le grain qu'on voit venir : le relèvement de son centre (unitaire, x et z du
    /// monde) et la noirceur de ce quart de ciel, 0 par temps clair. Retenu ici et
    /// poussé par <see cref="PushTo"/> avec le reste du ciel ; ses éclairs
    /// lointains partent de là.
    /// </summary>
    public void SetSquall(Vector2 bearing, double loom)
    {
        if (loom > 0) _loomDir = bearing;
        _loom = loom;
    }
    Vector2 _loomDir = new(0, 1), _farDir = Vector2.Right;
    double _loom;

    /// <summary>
    /// L'éclair PROCHE : un coup au-dessus d'elle, qui éclaire le pont, la toile
    /// et tout le ciel ensemble. Quatre pointes sur une seconde, parce qu'on est
    /// dedans et que le détail se voit.
    /// </summary>
    public void Strike()
    {
        _flashT = 0;
        _flashQueue = new (double, double)[]
        {
            (0.00, 1.00), (0.06, 0.55), (0.17, 0.80), (0.33, 0.35)
        };
    }

    /// <summary>
    /// LE GRAIN LOINTAIN S'ALLUME PAR EN DEDANS, et ce n'est délibérément PAS la
    /// même chose que <see cref="Strike"/>. Un coup au-dessus d'elle éclaire le
    /// pont, la toile et tout le ciel — juste quand l'orage est sur elle, absurde
    /// à six milles, où l'on voit une tache de nuage s'allumer et rien d'autre :
    /// pas de lumière sur les voiles, pas d'ombre qui bouge. Celui-ci ne sort donc
    /// jamais du shader de ciel.
    ///
    /// Court, aussi : à cette distance un coup est un clignement, et le dessiner
    /// plus long le fait lire comme une lampe.
    /// </summary>
    void FarLightning(double dt, double loom)
    {
        _farT += dt;
        if (_farA > 0)
        {
            // décroissance rapide avec un second coup, ce à quoi ressemble un arc en retour
            _farA *= Math.Exp(-dt * 22.0);
            if (_farT > _farNext2 && _farNext2 > 0)
            {
                _farA = Math.Max(_farA, 0.55);
                _farNext2 = -1;
            }
            if (_farA < 0.004) _farA = 0;
        }

        // seulement d'un banc qu'on voit réellement, et d'autant plus souvent
        // qu'il est noir : environ un coup toutes les trois secondes d'un grain formé
        if (loom > 0.06 && _farA == 0 && _rng.Randf() < loom * dt * 0.34)
        {
            _farA = 1.0;
            _farT = 0;
            _farNext2 = _rng.Randf() < 0.55 ? 0.09 + _rng.Randf() * 0.10 : -1;
            // quelque part le long du front plutôt qu'en plein milieu
            double a = (_rng.Randf() - 0.5) * 0.95;
            float ca = Mathf.Cos((float)a), sa = Mathf.Sin((float)a);
            // tourné depuis le relèvement du GRAIN, comme la page — pas depuis l'éclair d'avant
            _farDir = new Vector2(_loomDir.X * ca - _loomDir.Y * sa, _loomDir.X * sa + _loomDir.Y * ca);
        }
    }

    /// <summary>
    /// Le temps qui passe : le jour qui défile, l'éclair qui s'éteint, et le gros
    /// temps tiré de l'état de la mer.
    /// </summary>
    public void UpdateWeather(double dt, double seaState)
    {
        if (DayRunning)
        {
            // une minute réelle pour DayRate heures de ciel
            Core.SetTimeOfDay(Core.DayTime + dt * DayRate / 60.0, Latitude);
        }

        Core.SetSeaState(seaState);
        FarLightning(dt, _loom);

        // orages seulement : sous une bonne brise il n'y a rien à décharger
        double p = Math.Max(0, (seaState - 5.2) / 3.8);
        if (p > 0 && _rng.Randf() < p * p * dt * 0.20) Strike();

        double f = 0;
        if (_flashQueue != null)
        {
            _flashT += dt;
            foreach (var s in _flashQueue)
            {
                double dtt = _flashT - s.At;
                if (dtt >= 0) f = Math.Max(f, s.A * Math.Exp(-dtt * 16.0));
            }
            if (_flashT > 1.2) _flashQueue = null;
        }
        Core.Flash = f;
        Core.RefreshFlash();

        Apply();
    }
}
