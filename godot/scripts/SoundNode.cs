using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE BRUIT D'UNE BORDÉE, ET SURTOUT LE TEMPS QU'IL MET À VENIR — portage de
/// sound.js, dont c'est l'idée entière.
///
/// LE SON MET UNE SECONDE ET DEMIE À FAIRE CINQ CENTS MÈTRES, et c'est cela,
/// bien plus que le timbre, qui fait lire un canon comme lointain. On voit la
/// flamme, on compte, puis on entend. Un échantillon sourd joué à l'instant du
/// feu sonne comme un canon en sourdine ; le même joué une seconde plus tard
/// sonne comme un canon LOIN. Le retard est gratuit — une division — et il est
/// l'essentiel de l'effet.
///
/// TROIS CENT QUARANTE-TROIS MÈTRES PAR SECONDE, ce qui n'est pas un réglage :
/// c'est la vitesse du son dans l'air à quinze degrés. À deux cents mètres cela
/// fait six dixièmes de seconde ; à un demi-mille, deux secondes et demie.
///
/// L'OREILLE EST À LA CAMÉRA — Godot écoute par la caméra active, ce qui est
/// exactement le choix de la page : on entend d'où l'on regarde. La vue fixe
/// plantée à deux cents mètres retarde donc VOTRE propre bordée.
///
/// CE QUE GODOT FAIT DÉJÀ, on le lui laisse : l'atténuation en 1/r
/// (<c>InverseDistance</c> avec la distance de référence de la page), le
/// panoramique et la mise à jour quand la caméra bouge. Ce qu'il ne fait pas —
/// le RETARD de propagation, et l'absorption des aigus par l'air réglée sur la
/// distance du coup — est écrit ici, comme dans la page.
/// </summary>
public partial class SoundNode : Node3D
{
    /// <summary>La vitesse du son dans l'air à quinze degrés.</summary>
    public const double C = 343;
    /// <summary>Au-delà, un coup n'apprend plus rien : deux milles et demi.</summary>
    public const float Portee = 2500;
    /// <summary>Où l'on bascule sur l'échantillon lointain.</summary>
    public const double Loin = 400;
    /// <summary>La distance de référence de l'atténuation — une pression décroît en 1/r.</summary>
    public const float Ref = 55;
    /* L'AIR MANGE LES AIGUS, et c'est ce qui assourdit un coup bien avant qu'il
       ne devienne un grondement. Une exponentielle, parce que l'absorption l'est
       en distance : à 260 m la coupure tombe d'un facteur e, de 20 kHz à 7,4. */
    const double Etouffe = 260, FcMin = 700;
    /// <summary>Jamais plus de voix à la fois : au-delà, un coup de plus ne s'entend pas.</summary>
    const int MaxVivants = 24;

    readonly List<AudioStreamPlayer3D> _pool = new();
    /// <summary>Ceux qui attendent que leur son ait fait le chemin.</summary>
    readonly List<(double When, AudioStreamPlayer3D P)> _waiting = new();
    readonly Dictionary<string, AudioStream> _buf = new();
    readonly RandomNumberGenerator _rng = new();
    double _now;

    /// <summary>Les bruitages, qu'on peut couper sans couper la musique.</summary>
    public bool On = true;

    public override void _Ready()
    {
        for (int i = 0; i < MaxVivants; i++)
        {
            var p = new AudioStreamPlayer3D
            {
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
                UnitSize = Ref,
                MaxDistance = Portee,
                MaxDb = 3,
                // le filtre de l'air : sa coupure est posée coup par coup, sur la distance
                AttenuationFilterDb = -24,
                Bus = "Master"
            };
            AddChild(p);
            _pool.Add(p);
        }
        LoadAll();
    }

    /// <summary>
    /// Les quatre échantillons, lus dans medias/sound comme la page — mêmes
    /// fichiers, mêmes clés. Chargés à l'exécution, hors du projet Godot : un
    /// dossier de sons pour les deux versions.
    /// </summary>
    void LoadAll()
    {
        Load("pres", "cannon_fire_001.ogg");
        Load("loin", "cannon_far_away.ogg");
        Load("bois1", "wood_crash_001.ogg");
        Load("bois2", "wood_crash_002.ogg");
    }

    void Load(string key, string file)
    {
        string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", file);
        if (!System.IO.File.Exists(path))
        {
            GD.PushWarning($"échantillon absent : {file} — ce bruit-là ne se fera pas.");
            return;
        }
        var s = AudioStreamOggVorbis.LoadFromFile(path);
        if (s != null) _buf[key] = s;
    }

    public override void _Process(double delta)
    {
        _now += delta;
        AmbTick(delta);
        // ce qui a fini son voyage part maintenant
        for (int i = _waiting.Count - 1; i >= 0; i--)
            if (_waiting[i].When <= _now)
            {
                _waiting[i].P.Play();
                _waiting.RemoveAt(i);
            }
    }

    /// <summary>Un lecteur libre, ou rien — c'est le plafond de voix de la page.</summary>
    AudioStreamPlayer3D? Free()
    {
        foreach (var p in _pool)
        {
            if (p.Playing) continue;
            bool waiting = false;
            foreach (var w in _waiting) if (w.P == p) { waiting = true; break; }
            if (!waiting) return p;
        }
        return null;
    }

    /* TOUT CE QUI SONNE PASSE PAR ICI, et c'est la seule raison pour laquelle un
       impact à quatre cents mètres est en retard et mat sans qu'une ligne le
       redise : le retard et l'absorption de l'air sont des propriétés de la
       DISTANCE, pas du coup de canon. Les écrire une seconde fois pour le bois
       qui casse, ce serait se donner deux acoustiques à tenir en accord. */
    void Play(string key, Vec3d at, double rate, double vol)
    {
        if (!On || !_buf.TryGetValue(key, out var stream)) return;
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var pos = new Vector3((float)at.X, (float)at.Y, (float)at.Z);
        double d = pos.DistanceTo(cam.GlobalPosition);
        if (d > Portee) return;

        var p = Free();
        if (p == null) return;                       // le plafond de voix : ce coup-ci ne se fera pas
        p.Stream = stream;
        p.GlobalPosition = pos;
        p.PitchScale = (float)Math.Clamp(rate, 0.6, 1.6);
        p.VolumeDb = Mathf.LinearToDb((float)Math.Clamp(vol, 0.001, 1));
        // la même détonation, privée de son claquement à mesure qu'elle vient de loin
        p.AttenuationFilterCutoffHz = (float)Math.Max(FcMin, 20000 * Math.Exp(-d / Etouffe));

        // LE RETARD, qui est tout l'intérêt : la distance divisée par la vitesse du son
        _waiting.Add((_now + d / C, p));
    }

    /// <summary>
    /// UN COUP DE CANON. <paramref name="k"/> est le calibre relatif que la
    /// batterie calcule déjà : une caronade n'est pas un trente-deux, et une
    /// grosse pièce sonne plus GRAVE. Rendu par la vitesse de lecture plutôt que
    /// par un troisième échantillon, ce qui allonge du même coup la détente.
    /// </summary>
    public void Boom(Vec3d at, double k)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        double d = new Vector3((float)at.X, (float)at.Y, (float)at.Z).DistanceTo(cam.GlobalPosition);
        string key = d > Loin ? (_buf.ContainsKey("loin") ? "loin" : "pres")
                              : (_buf.ContainsKey("pres") ? "pres" : "loin");
        // jamais deux fois le même coup : la charge était dosée à la main
        Play(key, at, (1.15 - 0.30 * k) * (1 + (_rng.Randf() - 0.5) * 0.06), 1);
    }

    /* UN SON QU'ON FABRIQUE, faute d'échantillon : écrit une fois dans un tampon,
       puis joué par la même acoustique que les autres, donc retardé, étouffé et
       placé pareillement. Rien à charger, rien à embarquer. */
    AudioStream Synth(string key, double duree, Action<float[], int> remplir)
    {
        if (_buf.TryGetValue(key, out var had)) return had;
        const int rate = 22050;
        int n = (int)(duree * rate);
        var f = new float[n];
        remplir(f, rate);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short v = (short)(Math.Clamp(f[i], -1, 1) * 32000);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        var w = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate, Stereo = false, Data = data
        };
        _buf[key] = w;
        return w;
    }

    /// <summary>
    /// LE TONNERRE d'un coup au but : un claquement sec, puis un roulement qui
    /// s'éteint en battant — du bruit brun, qui est ce que l'air renvoie d'une
    /// décharge répercutée par les nuages.
    /// </summary>
    public void Thunder(Vec3d at)
    {
        Synth("tonnerre", 4.5, (d, sr) =>
        {
            double brun = 0;
            for (int i = 0; i < d.Length; i++)
            {
                double t = (double)i / sr, w = _rng.Randf() * 2 - 1;
                brun = (brun + 0.02 * w) / 1.02;
                double claque = t < 0.14 ? w * (1 - t / 0.14) : 0;
                double roule = brun * 6.5 * Math.Exp(-t * 0.8)
                             * (0.6 + 0.4 * Math.Sin(t * 7 + Math.Sin(t * 2.3) * 2));
                d[i] = (float)Math.Clamp(claque * 0.9 + roule, -1, 1);
            }
        });
        Play("tonnerre", at, 1, 1);
    }

    /// <summary>
    /// UN GRONDEMENT sous la coque : très grave, lent à monter, battu comme un
    /// souffle. Une fondamentale qui glisse de 42 à 30 Hz et ses deux premières
    /// harmoniques, noyées dans du bruit brun — assez bas pour se SENTIR plus
    /// que s'entendre, ce qui est ce qu'on veut d'une chose qu'on ne voit pas
    /// encore.
    /// </summary>
    public void Growl(Vec3d at)
    {
        Synth("grondement", 3.6, (d, sr) =>
        {
            double brun = 0, ph = 0;
            for (int i = 0; i < d.Length; i++)
            {
                double t = (double)i / sr, w = _rng.Randf() * 2 - 1;
                brun = (brun + 0.02 * w) / 1.02;
                double f = 42 - 12 * Math.Min(1, t / 3.6);
                ph += 2 * Math.PI * f / sr;
                double env = Math.Min(1, t / 0.6) * Math.Exp(-Math.Max(0, t - 1.6) * 1.4);
                double souffle = 0.65 + 0.35 * Math.Sin(t * 2 * Math.PI * 5.5);
                double ton = Math.Sin(ph) + 0.45 * Math.Sin(2 * ph + 0.4) + 0.2 * Math.Sin(3 * ph + 1.1);
                d[i] = (float)Math.Clamp(env * souffle * (0.55 * ton + brun * 4), -1, 1);
            }
        });
        Play("grondement", at, 1, 1);
    }

    /// <summary>
    /// LE BOIS QUI CASSE, à la distance de la CIBLE et non du canon : on entend
    /// d'abord la pièce, puis, s'il y a de la distance, le coup dans la muraille.
    /// Les deux voyagent à la même vitesse depuis deux endroits différents, et
    /// l'arithmétique s'en occupe toute seule.
    ///
    /// Deux échantillons tirés au sort, parce qu'un seul se reconnaît à la
    /// troisième touche et cesse d'être un choc pour devenir un bruitage. Et un
    /// mât n'est pas une muraille : même bois, plus léger et plus sec, donc le
    /// même échantillon monté d'un ton.
    /// </summary>
    public void Crash(Vec3d at, double k, double speed, string what)
    {
        string key = _rng.Randf() < 0.5 ? "bois1" : "bois2";
        if (!_buf.ContainsKey(key)) key = key == "bois1" ? "bois2" : "bois1";
        // un boulet arrivé à bout de course cogne moins fort ; 300 m/s est le plein fouet
        double fort = Math.Clamp((speed > 0 ? speed : 200) / 300, 0.3, 1);
        double aigu = what == "mast" ? 1.18 : 1.0;
        Play(key, at, aigu * (1.15 - 0.30 * k) * (1 + (_rng.Randf() - 0.5) * 0.10), 0.85 * fort);
    }

    // ------------------------------------------------------------------
    /* LA MUSIQUE NE PASSE PAS PAR L'ACOUSTIQUE, et ce n'est pas un détail de
       plomberie : le retard, l'absorption de l'air et le relief gauche-droite
       sont les propriétés d'un son qui vient d'un ENDROIT. Une musique ne vient
       de nulle part — elle est dans la tête du commandant, pas sur l'eau.

       Et elle reste BIEN EN DESSOUS des bruitages : une nappe qui couvre une
       bordée a cessé d'être une ambiance pour devenir un problème. */
    const float VolAmb = 0.34f;
    /// <summary>Le fondu d'un morceau à l'autre.</summary>
    const double Fondu = 2.5;

    AudioStreamPlayer? _amb;
    string _ambSrc = "";
    double _ambGoal, _ambNow;

    /// <summary>Ce qui joue, ou rien — pour que l'appelant n'ait pas à s'en souvenir.</summary>
    public string Playing => _ambSrc;

    /// <summary>
    /// Mettre une musique, ou la taire (<c>null</c>). Le morceau en place s'en va
    /// en douceur ; celui qui arrive monte de même.
    /// </summary>
    public void Ambiance(string? file)
    {
        if ((file ?? "") == _ambSrc) return;
        _ambSrc = file ?? "";
        if (string.IsNullOrEmpty(file)) { _ambGoal = 0; return; }

        string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", file);
        if (!System.IO.File.Exists(path))
        {
            GD.PushWarning($"musique absente : {file}");
            _ambSrc = "";
            return;
        }
        var stream = AudioStreamOggVorbis.LoadFromFile(path);
        if (stream == null) { _ambSrc = ""; return; }
        stream.Loop = true;
        _amb ??= AddAmb();
        _amb.Stream = stream;
        _amb.VolumeDb = Mathf.LinearToDb(0.001f);
        _ambNow = 0;
        _ambGoal = VolAmb;
        _amb.Play();
    }

    AudioStreamPlayer AddAmb()
    {
        var a = new AudioStreamPlayer { Bus = "Master" };
        AddChild(a);
        return a;
    }

    /// <summary>Le fondu, image par image.</summary>
    void AmbTick(double dt)
    {
        if (_amb == null) return;
        double step = dt / Fondu * VolAmb;
        if (_ambNow < _ambGoal) _ambNow = Math.Min(_ambGoal, _ambNow + step);
        else if (_ambNow > _ambGoal) _ambNow = Math.Max(_ambGoal, _ambNow - step);
        /* Ce qui s'est tu se tait POUR DE BON : un flux laissé en lecture à
           volume nul continue de décoder pour rien. */
        if (_ambNow <= 0.0005) { if (_amb.Playing) _amb.Stop(); return; }
        _amb.VolumeDb = Mathf.LinearToDb((float)_ambNow);
    }
}
