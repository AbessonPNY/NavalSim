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

    /// <summary>Le défilement du jour, en heures de ciel par minute réelle.</summary>
    public double DayRate = 60.0;
    public bool DayRunning;

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
            TonemapExposure = 1.0f
        };
        AddChild(new WorldEnvironment { Environment = Env });

        Sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            ShadowNormalBias = 1.4f,   // la toile est d'épaisseur nulle
            DirectionalShadowMaxDistance = 320
        };
        AddChild(Sun);

        Apply();
    }

    /// <summary>Recopie l'état du noyau dans le moteur et dans le dôme.</summary>
    public void Apply()
    {
        var d = Core.SunDir;
        var dir = new Vector3((float)d.X, (float)d.Y, (float)d.Z);

        // une lumière directionnelle regarde le long de son -Z : elle est donc
        // placée du côté du soleil et braquée sur l'origine
        Sun.Position = dir * 400f;
        Sun.LookAt(Vector3.Zero, Vector3.Up);

        Sun.LightColor = new Color((float)Core.SunColor.R, (float)Core.SunColor.G, (float)Core.SunColor.B);
        Sun.LightEnergy = (float)Core.SunIntensity;

        /* L'ambiante est celle du ciel, mais son ÉNERGIE porte l'éclair : c'est
           ce qui fait que le pont et la toile l'attrapent. Un éclair qui
           n'éclairerait que le ciel se lit comme un fond d'écran qui clignote. */
        Env.AmbientLightEnergy = (float)Math.Max(0.05, Core.HemiIntensity * 3.2);

        PushTo(_domeMat);
    }

    /// <summary>
    /// LE MÊME CIEL, POUSSÉ PARTOUT. Le dôme, la mer, et tout ce qui respire le
    /// même air. Ajouter un matériau à la liste des appelants est le seul geste
    /// qu'il faut ne pas oublier — et c'est pour cela que tout est écrit ici et
    /// nulle part ailleurs.
    /// </summary>
    public void PushTo(ShaderMaterial m)
    {
        if (m == null) return;
        var d = Core.SunDir;
        m.SetShaderParameter("u_sun", new Vector3((float)d.X, (float)d.Y, (float)d.Z));
        // Vector3 et non Color : ces valeurs sont deja lineaires, et passer par
        // un Color sur un uniforme `source_color` les convertirait une fois de trop.
        m.SetShaderParameter("u_zenith", new Vector3((float)Core.Zenith.R, (float)Core.Zenith.G, (float)Core.Zenith.B));
        m.SetShaderParameter("u_horizon", new Vector3((float)Core.Horizon.R, (float)Core.Horizon.G, (float)Core.Horizon.B));
        m.SetShaderParameter("u_storm", (float)Core.Storm);
        m.SetShaderParameter("u_haze", (float)Core.Haze);
        m.SetShaderParameter("u_haze_h", (float)Core.HazeHeight);
        m.SetShaderParameter("u_flash", (float)Core.Flash);
    }

    public void SetCloud(ShaderMaterial m, double amount, double skyTime)
    {
        m?.SetShaderParameter("u_cloud", (float)amount);
        m?.SetShaderParameter("u_sky_time", (float)skyTime);
    }

    /// <summary>Le grain qu'on voit venir : son relèvement et sa noirceur.</summary>
    public void SetLoom(ShaderMaterial m, Vector2 bearing, double loom)
    {
        m?.SetShaderParameter("u_storm_dir", bearing);
        m?.SetShaderParameter("u_storm_loom", (float)loom);
        m?.SetShaderParameter("u_storm_flash_dir", _farDir);
        m?.SetShaderParameter("u_storm_flash", (float)_farA);
    }
    Vector2 _farDir = Vector2.Right;

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
            _farDir = new Vector2(_farDir.X * ca - _farDir.Y * sa, _farDir.X * sa + _farDir.Y * ca);
        }
    }

    /// <summary>
    /// Le temps qui passe : le jour qui défile, l'éclair qui s'éteint, et le gros
    /// temps tiré de l'état de la mer.
    /// </summary>
    public void UpdateWeather(double dt, double seaState, double loom = 0)
    {
        if (DayRunning)
        {
            // une minute réelle pour DayRate heures de ciel
            Core.SetTimeOfDay(Core.DayTime + dt * DayRate / 60.0, Latitude);
        }

        Core.SetSeaState(seaState);
        FarLightning(dt, loom);

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
