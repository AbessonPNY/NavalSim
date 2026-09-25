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
    /// <summary>Les échantillons, PLUSIEURS par clé : on en tire un au hasard.</summary>
    readonly Dictionary<string, List<AudioStream>> _buf = new();
    readonly RandomNumberGenerator _rng = new();
    double _now;

    /// <summary>Les bruitages, qu'on peut couper sans couper la musique.</summary>
    public bool On = true;

    /* LE DEHORS SUR SON PROPRE BUS. Tout partait au Master, si bien qu'il n'y
       avait aucun endroit où poser une main sur les bruits du large sans toucher
       aussi à la musique. Un bus à eux, avec un passe-bas, et une chambre peut
       enfin fermer sa porte. Bâti à la volée plutôt que dans un
       default_bus_layout.tres : deux réglages ne valent pas un fichier de plus,
       et celui-ci se lirait mal à côté du code qui s'en sert. */
    public const string OutBus = "Dehors";
    AudioEffectLowPassFilter? _muffle;
    int _outIdx = 0;
    double _indoorsNow;

    void MakeOutBus()
    {
        for (int i = 0; i < AudioServer.BusCount; i++)
            if (AudioServer.GetBusName(i) == OutBus) { _outIdx = i; break; }
        if (_outIdx == 0)
        {
            AudioServer.AddBus();
            _outIdx = AudioServer.BusCount - 1;
            AudioServer.SetBusName(_outIdx, OutBus);
            AudioServer.SetBusSend(_outIdx, "Master");
        }
        for (int e = AudioServer.GetBusEffectCount(_outIdx) - 1; e >= 0; e--)
            AudioServer.RemoveBusEffect(_outIdx, e);
        /* VINGT-QUATRE DÉCIBELS PAR OCTAVE, ET C'EST LÀ QUE TOUT SE JOUAIT.
           Godot monte un passe-bas à SIX dB par octave par défaut : à 900 Hz de
           coupure, ce qui est deux octaves plus haut ne perd que douze décibels —
           autant dire rien. On entendait donc la BAISSE de volume et pas
           l'étouffement, ce qui explique qu'aucun réglage de seuil ni de bande
           n'ait rien changé à l'oreille (signalé deux fois). La pente la plus
           raide que Godot offre coupe quatre fois plus vite, et c'est elle qu'il
           faut pour une cloison de chêne ou pour de l'eau. */
        _muffle = new AudioEffectLowPassFilter
        {
            CutoffHz = 20500,
            Db = AudioEffectFilter.FilterDB.Filter24Db
        };
        AudioServer.AddBusEffect(_outIdx, _muffle);
        Indoors(0);
    }

    /* DERRIÈRE UNE CLOISON DE CHÊNE. `k` va de zéro (sur le pont) à un (enfermé).
       Deux choses ensemble, parce que c'est ce que fait une cloison : elle BAISSE
       et elle ÉTOUFFE — les aigus passent moins bien que les graves, et c'est
       l'étouffement, plus que la baisse, qui fait entendre qu'on est dedans. La
       coupure descend de 20 kHz à 900 Hz, ce qui laisse le canon gronder et
       emporte le claquement de la toile. Neuf cents et non sept : à sept, ce qui
       venait de loin ne passait plus du tout. */
    public void Indoors(double k) { _indoorsNow = Math.Clamp(k, 0, 1); Muffle(); }

    /* ------------------------------------------------------------------ */
    /*  ET SOUS L'EAU                                                       */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// L'ŒIL SOUS LA SURFACE. Rien ne porte le son comme l'eau — elle le mène
    /// quatre fois plus vite que l'air et l'éteint bien moins —, mais une tête
    /// immergée n'a plus l'oreille faite pour l'entendre : ce qui reste est un
    /// grondement sans aigus. Une bataille écoutée de dessous est ce grondement,
    /// et c'est ce qu'on vient y chercher. Plus bas que la cloison de la chambre,
    /// donc, et moins éteint : on veut en profiter, pas en être privé.
    /// </summary>
    public void Underwater(double k) { _wetNow = Math.Clamp(k, 0, 1); Muffle(); }
    double _wetNow;

    float _wroteCut = -1;

    /* LA COUPURE BASCULE D'UN COUP, LE VOLUME SEUL GLISSE — et ce n'est pas un
       compromis, c'est le bon geste. Une porte est entre vous et la mer, ou elle
       ne l'est pas ; la tête est sous l'eau, ou elle est dehors. La faire glisser
       l'ÉCRIVAIT À CHAQUE IMAGE, et republier une ressource d'effet au serveur
       audio soixante fois par seconde traîne — une seconde de latence de part et
       d'autre de la porte (signalé), quand la rampe est mesurée à 131 ms. Une
       écriture par passage suffit, et le fondu du volume porte tout le glissé
       qu'on entend. */
    /* LES QUATRE NOMBRES SONT DANS LE MANIFESTE (sons.json → etouffe), parce
       qu'ils s'écoutent et ne se calculent pas : celui qui les règle doit pouvoir
       les pousser sans recompiler. Ce sont des Hz de coupure et des décibels de
       baisse, pour la cloison et pour l'eau. */
    public float HzCabine = 900, HzEau = 380;
    public float DbCabine = -9, DbEau = -4;

    void Muffle()
    {
        // deux causes, et la plus forte l'emporte : une cloison sous l'eau ne filtre pas deux fois
        bool wet = _wetNow > 0.5, dedans = _indoorsNow > 0.5;
        float hz = wet ? HzEau : dedans ? HzCabine : 20500f;
        if (_muffle != null && hz != _wroteCut) { _muffle.CutoffHz = hz; _wroteCut = hz; }
        // et la baisse, au volume du bus : instantanée, sans coût, et elle seule fond
        AudioServer.SetBusVolumeDb(_outIdx, (float)(DbCabine * _indoorsNow + DbEau * _wetNow));
    }

    public override void _Ready()
    {
        MakeOutBus();
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
                // le large a son bus : une chambre peut l'étouffer sans toucher à la musique
                Bus = OutBus
            };
            AddChild(p);
            _pool.Add(p);
        }
        LoadAll();
    }

    /* LES ÉCHANTILLONS ÉTAIENT NOMMÉS ICI, EN DUR. Quatre lignes de code pour
       dire quel fichier est le coup de canon : il fallait recompiler pour en
       changer, et le nom ne se lisait nulle part ailleurs. Ils sont maintenant
       dans medias/sound/sons.json, avec tout le reste — et PLUSIEURS par clé, ce
       qu'un échantillon unique ne permettait pas : on reconnaît vite le même
       craquement. La liste ci-dessous n'est que le DERNIER RECOURS, pour qu'un
       dossier sans manifeste fasse quand même du bruit. */
    static readonly (string Key, string File)[] Defaults =
    {
        ("pres", "cannon_fire_001.ogg"),
        ("loin", "cannon_far_away.ogg"),
        ("bois", "wood_crash_001.ogg"),
        ("bois", "wood_crash_002.ogg")
    };

    void LoadAll()
    {
        foreach (var (k, f) in Defaults) Load(k, f);
    }

    /// <summary>Un échantillon de plus sous cette clé — plusieurs, tirées au hasard.</summary>
    public void Load(string key, string file)
    {
        // un nom tout court ou un chemin : les deux partent de medias/sound
        string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", file);
        if (!System.IO.File.Exists(path))
        {
            GD.PushWarning($"échantillon absent : {file} — ce bruit-là ne se fera pas.");
            return;
        }
        if (Read(path) is not { } s) return;
        if (!_buf.TryGetValue(key, out var list)) _buf[key] = list = new List<AudioStream>();
        list.Add(s);
    }

    /// <summary>Oublier ce qui était rangé sous cette clé : le manifeste remplace, il n'ajoute pas.</summary>
    public void Forget(string key) => _buf.Remove(key);

    public bool Knows(string key) => _buf.TryGetValue(key, out var l) && l.Count > 0;

    /// <summary>
    /// UN SON, QUEL QUE SOIT SON FLACON. Le projet n'avait que de l'Ogg parce que
    /// c'est ce qu'il embarquait ; ce qu'on enregistre ou qu'on achète arrive en
    /// MP3, et refuser un fichier pour son extension serait une tracasserie sans
    /// raison. Le WAV passe aussi : c'est ce que rend un montage.
    /// </summary>
    public static AudioStream? Read(string path)
    {
        try
        {
            return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => AudioStreamMP3.LoadFromFile(path),
                ".wav" => AudioStreamWav.LoadFromFile(path),
                _ => AudioStreamOggVorbis.LoadFromFile(path)
            };
        }
        catch (Exception e)
        {
            GD.PushWarning($"son illisible : {System.IO.Path.GetFileName(path)} ({e.Message})");
            return null;
        }
    }

    /* ------------------------------------------------------------------ */
    /*  LA VOIX DU BORD                                                     */
    /* ------------------------------------------------------------------ */

    /// <summary>Les échantillons de l'équipage, par événement : plusieurs par clé, tirés au hasard.</summary>
    readonly Dictionary<string, List<AudioStream>> _crew = new();
    /// <summary>Quand chaque clé pourra reparler : un ordre crié deux fois de suite n'est plus un ordre.</summary>
    readonly Dictionary<string, double> _crewAgain = new();

    /* DEUX RÉSERVES, ET CE N'EST PAS UN DOUBLON. Ce qui VOYAGE — un coup de
       canon, un boulet dans un bordé — passe par _buf : il arrive en retard de sa
       distance, et l'air lui a mangé ses aigus. Ce qui se fait À BORD — une voix,
       la toile qui tombe — passe par _crew : c'est à vingt mètres, rien ne le
       retarde et rien ne le filtre, et il a droit à son propre délai de répétition.
       Les mêmes échantillons dans le même sac se comporteraient mal d'un côté ou
       de l'autre. */

    /// <summary>Combien d'échantillons cette clé a reçus (zéro : elle se taira).</summary>
    public int CrewCount(string key) => _crew.TryGetValue(key, out var l) ? l.Count : 0;

    /// <summary>Oublier ce que cette clé de bord avait : un manifeste remplace, il n'ajoute pas.</summary>
    public void ForgetCrew(string key) => _crew.Remove(key);

    /// <summary>Ranger un échantillon d'équipage sous sa clé.</summary>
    public void AddCrew(string key, string path)
    {
        if (Read(path) is not { } s) return;
        if (!_crew.TryGetValue(key, out var list)) _crew[key] = list = new List<AudioStream>();
        list.Add(s);
    }

    /// <summary>
    /// UN ORDRE CRIÉ, ou un bruit de la vie du bord. Il part du PONT, donc par le
    /// bus du dehors : entendu depuis la chambre, il traverse une cloison de
    /// chêne comme le reste, et c'est juste — ce n'est pas vous qui criez.
    /// <paramref name="hold"/> : les secondes avant que cette clé puisse reparler.
    /// Rend faux si la clé est muette ou si elle vient de parler.
    /// </summary>
    /// <param name="inside">Vrai : au Master, sans cloison ni eau entre lui et l'oreille.</param>
    public bool Crew(string key, Vec3d at, double gain = 1, double hold = 2, bool inside = false)
    {
        if (!On || !_crew.TryGetValue(key, out var list) || list.Count == 0) return false;
        if (_crewAgain.TryGetValue(key, out double t) && _now < t) return false;
        _crewAgain[key] = _now + hold;
        var stream = list[(int)(_rng.Randf() * list.Count) % list.Count];
        var p = Free();
        if (p == null) return false;
        p.Bus = inside ? "Master" : OutBus;
        p.Stream = stream;
        p.GlobalPosition = new Vector3((float)at.X, (float)at.Y, (float)at.Z);
        // une voix n'est pas un coup de canon : ni variation de hauteur, ni filtre de l'air
        p.PitchScale = 1;
        p.VolumeDb = Mathf.LinearToDb((float)Math.Clamp(gain, 0.001, 1));
        p.AttenuationFilterCutoffHz = 20500;
        /* IL PART MAINTENANT, PAS À L'IMAGE SUIVANTE. Ce qui se fait à bord n'a
           aucun chemin à parcourir : le mettre dans la file d'attente lui coûtait
           une image pour rien — et la file, elle, existe pour retenir ce qui
           voyage. Un son déjà en lecture n'est de toute façon plus libre, donc
           rien à signaler à Free(). */
        p.Play();
        return true;
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
    /* CE QUI SE FAIT À BORD NE PASSE PAS PAR LA CLOISON. Un boulet dans votre
       muraille, ce sont les bois de la chambre elle-même qui craquent : l'étouffer
       comme un bruit du large, c'est le supprimer (signalé). Ces voix-là restent
       au Master, quoi qu'on ferme. Le reste — les autres navires, la mer, le
       tonnerre — arrive du dehors et passe par le bus qui filtre. */
    /* DEUX QUESTIONS, ET ON LES AVAIT CONFONDUES EN UN SEUL DRAPEAU.
       « À BORD » dit que le son se fait sur NOTRE navire, donc qu'il n'a pas de
       chemin à parcourir : c'est le retard qu'il règle.
       « DEDANS » dit qu'il se fait dans la PIÈCE OÙ L'ON EST, donc qu'aucune
       cloison ne le sépare de l'oreille : c'est le bus qu'il règle.
       Les deux ensemble ont rendu la bordée du joueur inétouffable depuis la
       chambre (signalé) — or les pièces sont sur le pont de batterie, dehors, et
       on les entend à travers le navire. Un boulet dans notre muraille, lui, fait
       craquer les bois de la chambre elle-même : celui-là est bien dedans. */
    /// <param name="after">
    /// Ce qu'il faut attendre AVANT de le lancer, en secondes, par-dessus le
    /// voyage du son. Une soute ne saute pas d'un coup : trois explosions
    /// décalées, et le son doit suivre les mêmes décalages que la flamme.
    /// </param>
    void Play(string key, Vec3d at, double rate, double vol, bool aboard = false, bool inside = false, double after = 0)
    {
        if (!On || !_buf.TryGetValue(key, out var bag) || bag.Count == 0) return;
        var stream = bag[(int)(_rng.Randf() * bag.Count) % bag.Count];
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var pos = new Vector3((float)at.X, (float)at.Y, (float)at.Z);
        double d = pos.DistanceTo(cam.GlobalPosition);
        if (d > Portee) return;

        var p = Free();
        if (p == null) return;                       // le plafond de voix : ce coup-ci ne se fera pas
        p.Bus = inside ? "Master" : OutBus;
        p.Stream = stream;
        p.GlobalPosition = pos;
        p.PitchScale = (float)Math.Clamp(rate, 0.6, 1.6);
        p.VolumeDb = Mathf.LinearToDb((float)Math.Clamp(vol, 0.001, 1));
        // la même détonation, privée de son claquement à mesure qu'elle vient de loin
        p.AttenuationFilterCutoffHz = (float)Math.Max(FcMin, 20000 * Math.Exp(-d / Etouffe));

        /* LE RETARD, QUI EST TOUT L'INTÉRÊT : la distance divisée par la vitesse
           du son. SAUF POUR CE QUI SE FAIT À BORD. La caméra est l'oreille, et en
           vue extérieure elle se tient à cinquante mètres du navire : sa propre
           bordée arrivait alors avec cent cinquante millisecondes de retard
           (mesuré : 143 ms à 49 m, 172 à 59), ce qui se lit comme une latence et
           non comme de la distance. Or on n'est pas à cinquante mètres de son
           propre pont, on y est. Vingt-cinq millièmes au plus pour ce qui vient
           de chez nous — le temps que le coup traverse le navire —, et tout le
           reste garde son voyage. */
        double wait = (aboard ? Math.Min(d / C, 0.025) : d / C) + Math.Max(0, after);
        _waiting.Add((_now + wait, p));
    }

    /// <summary>
    /// UN COUP DE CANON. <paramref name="k"/> est le calibre relatif que la
    /// batterie calcule déjà : une caronade n'est pas un trente-deux, et une
    /// grosse pièce sonne plus GRAVE. Rendu par la vitesse de lecture plutôt que
    /// par un troisième échantillon, ce qui allonge du même coup la détente.
    /// </summary>
    /// <param name="aboard">Notre propre pièce : rien à parcourir, mais elle est sur le pont.</param>
    public void Boom(Vec3d at, double k, bool aboard = false)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        double d = new Vector3((float)at.X, (float)at.Y, (float)at.Z).DistanceTo(cam.GlobalPosition);
        string key = d > Loin ? (Knows("loin") ? "loin" : "pres")
                              : (Knows("pres") ? "pres" : "loin");
        // jamais deux fois le même coup : la charge était dosée à la main
        Play(key, at, (1.15 - 0.30 * k) * (1 + (_rng.Randf() - 0.5) * 0.06), 1, aboard);
    }

    /// <summary>
    /// LA SOUTE QUI SAUTE. <paramref name="k"/> va avec la taille du bâtiment :
    /// un vaisseau part plus GRAVE qu'une chaloupe, rendu par la vitesse de
    /// lecture comme pour le canon. <paramref name="after"/> décale le coup,
    /// parce qu'une soute part en trois fois.
    ///
    /// Sans échantillon, rien : elle sautait déjà en silence, et un tonnerre de
    /// synthèse ne ressemble pas à une explosion — mieux vaut le manque que le
    /// faux.
    /// </summary>
    public void Blast(Vec3d at, double k = 1, double after = 0)
    {
        Play("explosion", at, (1.10 - 0.28 * Math.Clamp(k, 0, 1.6)) * (1 + (_rng.Randf() - 0.5) * 0.08), 1,
             false, false, after);
    }

    /* UN SON QU'ON FABRIQUE, faute d'échantillon : écrit une fois dans un tampon,
       puis joué par la même acoustique que les autres, donc retardé, étouffé et
       placé pareillement. Rien à charger, rien à embarquer. */
    AudioStream Synth(string key, double duree, Action<float[], int> remplir)
    {
        if (_buf.TryGetValue(key, out var had) && had.Count > 0) return had[0];
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
        _buf[key] = new List<AudioStream> { w };
        return w;
    }

    /// <summary>
    /// LE TONNERRE d'un coup au but : un claquement sec, puis un roulement qui
    /// s'éteint en battant — du bruit brun, qui est ce que l'air renvoie d'une
    /// décharge répercutée par les nuages.
    /// </summary>
    /// <summary>
    /// LE TONNERRE. Deux échantillons s'il y en a — le coup qu'on prend sur la
    /// tête et le roulement qu'on entend de loin —, et la synthèse en dernier
    /// recours, pour qu'un dossier sans manifeste tonne quand même.
    ///
    /// La frontière est BIEN PLUS LOIN que celle du canon (400 m) : une pièce à
    /// un demi-mille est déjà un bruit sourd, tandis qu'un coup de foudre à un
    /// demi-mille claque encore. Au-delà d'un mille, on n'entend plus que le
    /// roulement — et c'est ce roulement qui fait la nuit d'orage.
    ///
    /// Le VOYAGE du son n'est pas ici : <see cref="Play"/> le retarde déjà de la
    /// distance. On voit l'éclair, on compte, puis on entend.
    /// </summary>
    public const double TonnerreLoin = 1500;

    public void Thunder(Vec3d at)
    {
        var cam0 = GetViewport().GetCamera3D();
        if (cam0 != null && (Knows("tonnerre-pres") || Knows("tonnerre-loin")))
        {
            double dd = new Vector3((float)at.X, (float)at.Y, (float)at.Z).DistanceTo(cam0.GlobalPosition);
            string k = dd > TonnerreLoin ? (Knows("tonnerre-loin") ? "tonnerre-loin" : "tonnerre-pres")
                                         : (Knows("tonnerre-pres") ? "tonnerre-pres" : "tonnerre-loin");
            // jamais deux fois le même coup : un peu de hauteur en moins ou en plus
            Play(k, at, 1 + (_rng.Randf() - 0.5) * 0.14, 1);
            return;
        }
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
    /// <param name="aboard">Dans NOTRE bordé : ce sont les bois de la pièce où l'on est.</param>
    public void Crash(Vec3d at, double k, double speed, string what, bool aboard = false)
    {
        // le tirage entre échantillons est fait par Play : une seule clé suffit
        const string key = "bois";
        // un boulet arrivé à bout de course cogne moins fort ; 300 m/s est le plein fouet
        double fort = Math.Clamp((speed > 0 ? speed : 200) / 300, 0.3, 1);
        double aigu = what == "mast" ? 1.18 : 1.0;
        Play(key, at, aigu * (1.15 - 0.30 * k) * (1 + (_rng.Randf() - 0.5) * 0.10), 0.85 * fort, aboard, aboard);
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
    /* ------------------------------------------------------------------ */
    /*  LA MER, QUI NE S'ARRÊTE JAMAIS                                       */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// LE BRUIT DE FOND DE LA MER — une boucle, et c'est AUTRE CHOSE QUE LA
    /// MUSIQUE : on peut couper l'une sans l'autre, et la mer continue quand le
    /// morceau se tait. Elle a donc son propre lecteur.
    ///
    /// Sur le bus du dehors : entendue depuis la chambre, elle passe la cloison
    /// comme tout ce qui vient du large. Et en fondu — la mer ne change pas de
    /// voix d'un coup quand le vent fraîchit.
    /// </summary>
    AudioStreamPlayer? _sea;
    string _seaSrc = "";
    double _seaGoal, _seaNow, _seaGain = 0.7;

    public string SeaPlaying => _seaSrc;

    public void Sea(string? file, double gain = 0.7)
    {
        _seaGain = Math.Clamp(gain, 0, 1);
        if ((file ?? "") == _seaSrc) { if (_seaSrc.Length > 0) _seaGoal = _seaGain; return; }
        _seaSrc = file ?? "";
        if (string.IsNullOrEmpty(file)) { _seaGoal = 0; return; }

        string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", file);
        if (!System.IO.File.Exists(path))
        {
            GD.PushWarning($"ambiance de mer absente : {file}");
            _seaSrc = "";
            return;
        }
        if (Read(path) is not { } stream) { _seaSrc = ""; return; }
        if (stream is AudioStreamOggVorbis o) o.Loop = true;
        else if (stream is AudioStreamMP3 m) m.Loop = true;
        else if (stream is AudioStreamWav w) w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        _sea ??= AddSea();
        _sea.Stream = stream;
        _sea.VolumeDb = Mathf.LinearToDb(0.001f);
        _seaNow = 0;
        _seaGoal = _seaGain;
        _sea.Play();
    }

    AudioStreamPlayer AddSea()
    {
        var a = new AudioStreamPlayer { Bus = OutBus };
        AddChild(a);
        return a;
    }

    void SeaTick(double dt)
    {
        if (_sea == null) return;
        double step = dt / Fondu * Math.Max(0.05, _seaGain);
        if (_seaNow < _seaGoal) _seaNow = Math.Min(_seaGoal, _seaNow + step);
        else if (_seaNow > _seaGoal) _seaNow = Math.Max(_seaGoal, _seaNow - step);
        if (_seaNow <= 0.0005) { if (_sea.Playing) _sea.Stop(); return; }
        if (!_sea.Playing) _sea.Play();
        _sea.VolumeDb = Mathf.LinearToDb((float)_seaNow);
    }

    void AmbTick(double dt)
    {
        SeaTick(dt);
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
