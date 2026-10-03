using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Les ports : pontons, livres de bord, havre, mouillage (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    /// <summary>Le monde et sa terre — nuls tant qu'une région n'a pas été lue.</summary>
    public NavalSim.Core.World? _world;
    LandNode? _land;
    TownNode? _town;
    FolkNode? _folk;
    /// <summary>Le départ de la fiche est au mouillage : l'ancre tombe à la première image de jeu.</summary>
    bool _anchorAtStart;
    Vector3? _anchorTest;
    JettyNode? _jetty;

    /// <summary>Les filins d'abordage : la règle est dans le noyau, le dessin dans GrappleNode.</summary>
    readonly Grapple _grapples = new();
    GrappleNode? _grappleNode;
    MooredNode? _moored;
    /// <summary>Quand chaque pirate a lancé sa dernière volée : on ne relance pas à chaque image.</summary>
    readonly Dictionary<ShipPhysics, double> _volee = new();
    /// <summary>Le sort des crochets. Semé en dur : une volée doit se rejouer à l identique au banc.</summary>
    readonly Random _grappleRng = new(20261001);
    /// <summary>Les pontons, pour le solveur : il s'y cogne. Voir ShipPhysics.Jetties.</summary>
    (double Sx, double Sz, double Hx, double Hz)[] _jetties = System.Array.Empty<(double, double, double, double)>();

    /// <summary>
    /// CE QUE LE SOLVEUR SAIT DES QUAIS : ceux des ports, et les pontons de la fiche.
    /// Refait quand l'éditeur en déplace un, et redonné à toutes les coques qui
    /// l'avaient — en mètres MONDE VRAIS, le solveur retranche l'origine lui-même.
    /// </summary>
    void RefreshJetties()
    {
        if (_world == null) return;
        var list = _world.Isles.Where(i => i.Port.Hx != 0 || i.Port.Hz != 0)
                               .Select(i => (i.Port.Sx, i.Port.Sz, i.Port.Hx, i.Port.Hz)).ToList();
        if (_jetty != null) list.AddRange(_jetty.PierSegments());
        _jetties = list.ToArray();
        if (_ship?.Physics != null) _ship.Physics.Jetties = _jetties;
        foreach (var s in _others) s.Physics.Jetties = _jetties;
    }
    ChartNode? _chart;
    bool _dressed;
    /// <summary>--carte : la vue se penche sur la feuille dès qu'elle est trouvée.</summary>
    bool _overChart;
    NavalSim.Core.Logbook? _book;
    AnchorNode? _anchor2;
    SoundNode? _sound;
    /// <summary>La première image bâtit tout ce qui est à portée : on ne part pas d'un port à moitié dessiné.</summary>
    bool _landEager = true;

    /* LA MUSIQUE SUIT LA SITUATION, comme dans la page : une voile hostile en
       vue OU du fer en l'air, et l'on passe à l'action ; le calme revient quinze
       secondes après que tout s'est tu. Le seuil n'est pas le même dans les deux
       sens — 1200 m pour s'échauffer, 1800 pour se rasseoir —, sans quoi une
       voile qui louvoie à la limite ferait clignoter la musique. */
    /* LES DEUX MORCEAUX SONT NOMMÉS PAR LE MANIFESTE (medias/sound/sons.json).
       Ce qui suit n'est que le dernier recours, pour qu'un dossier sans manifeste
       ait quand même sa musique. */
    const string AmbNavDefaut = "Vivaldi for Focus & Energy  Fireplace Classical Music.ogg";
    const string AmbActionDefaut = "Musique Action Epique - Musique avec Tension   Musique Libre de Droit.ogg";
    string AmbNav => _ambCalme.Length > 0 ? _ambCalme : AmbNavDefaut;
    string AmbAction => _ambChaud.Length > 0 ? _ambChaud : AmbActionDefaut;
    const double EnVue = 1200, Lachee = 1800, Oubli = 15;
    double _lastDanger = -1e9;

    void AmbianceTick()
    {
        if (_sound == null) return;
        /* LE CINÉMA A SA BANDE, et elle passe même quand la musique d'ambiance est
           coupée : on a demandé un film, pas l'ambiance du jeu. À la sortie du mode,
           elle s'éteint en fondu et la situation reprend la main. */
        bool film = _cine && _ambCine.Length > 0;
        // la mer baisse de soixante pour cent sous la bande du film, et remonte après
        _sound.DuckSea(film ? 0.4 : 1, now: film);
        // le film frappe d'entrée : pas de montée (demandé), l'extinction reste en fondu
        if (film) { _sound.Ambiance(_ambCine, _ambCineGain, attack: true); return; }
        if (!_settings.Music) { _sound.Ambiance(null); return; }

        double near = double.MaxValue;
        var me = _ship.Physics.Body.Pos;
        foreach (var s in _others)
        {
            if (s.Physics.Foundered || !_pirates.ContainsKey(s)) continue;
            var o = s.Physics.Body.Pos;
            near = Math.Min(near, Math.Sqrt((o.X - me.X) * (o.X - me.X) + (o.Z - me.Z) * (o.Z - me.Z)));
        }
        bool hot = _sound.Playing == AmbAction;
        if (near < (hot ? Lachee : EnVue) || _gunnery.Shots.Count > 0) _lastDanger = _t;
        _sound.Ambiance(_t - _lastDanger < Oubli ? AmbAction : AmbNav);
    }

    /// <summary>
    /// LE CARNET, à côté des réglages et en clair : un carnet qu'on ne peut pas
    /// ouvrir dans un éditeur est un carnet dont on ne sait pas s'il a retenu.
    /// </summary>
    /* UN CARNET PAR RÉGION : ce qu'on y trace est en mètres de SA carte, et un
       trait de la Jamaïque posé sur la Tortue passerait au travers des terres.
       La Jamaïque garde l'ancien nom de fichier — ce qu'on y a déjà tracé reste. */
    string BookPath => BookPathOf(_world?.Region.Key ?? "");

    /// <summary>Le carnet de CETTE région-là. Une seule définition : la reprise,
    /// l'enregistrement et l'oubli s'en servent tous les trois.</summary>
    public static string BookPathOf(string region) =>
        region is "" or "caraibes" ? "user://carnet.json" : $"user://carnet-{region}.json";

    /* ------------------------------------------------------------------ */
    /*  CE QUE LE BORD A VU APPARTIENT À SA PARTIE                         */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// TOUS LES CARNETS, RÉGION PAR RÉGION — lus sur le disque, sauf celui de la
    /// région ouverte, qui vit en mémoire et serait en retard d'une traversée.
    /// </summary>
    Dictionary<string, string> BooksNow()
    {
        var outp = new Dictionary<string, string>();
        string ici = _world?.Region.Key ?? "";
        foreach (var r in Regions())
        {
            if (r.Key == ici) continue;
            string path = BookPathOf(r.Key);
            if (!FileAccess.FileExists(path)) continue;
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null) outp[r.Key] = f.GetAsText();
        }
        if (_book != null) { KeepEstimate(); outp[ici] = _book.ToJson(); }
        return outp;
    }

    /// <summary>
    /// OUBLIER LA CARTE — au départ d'une partie neuve, libre ou d'histoire.
    ///
    /// Le carnet était un fichier GLOBAL : une sortie neuve rouvrait celui de la
    /// précédente, avec ses traits, ses relevés et son voile déjà levé sur la
    /// moitié de la mer (signalé). Ce que le bord a vu appartient à SA partie, et
    /// une partie qui commence n'a rien vu.
    ///
    /// Tous les carnets, pas seulement celui de la région ouverte : la partie
    /// qu'on quitte a pu passer à la Tortue, et ce qu'elle y a relevé ne doit pas
    /// attendre la nôtre là-bas. Et le carnet de MÉMOIRE avec, sans quoi le
    /// premier enregistrement réécrirait le fichier qu'on vient d'effacer.
    /// </summary>
    void ForgetBooks()
    {
        foreach (var r in Regions())
        {
            string path = BookPathOf(r.Key);
            if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
        // la Jamaïque garde l'ancien nom de fichier : elle n'est pas forcément dans la liste
        if (FileAccess.FileExists("user://carnet.json"))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://carnet.json"));
        _book = new NavalSim.Core.Logbook();
        _chart?.Rebook(_book);
    }

    /// <summary>Reposer les carnets d'une partie qu'on reprend, et effacer les autres.</summary>
    void PutBooks(Dictionary<string, string> books)
    {
        foreach (var r in Regions())
        {
            string path = BookPathOf(r.Key);
            if (books.TryGetValue(r.Key, out var txt) && txt.Length > 0)
            {
                using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
                f?.StoreString(txt);
            }
            else if (FileAccess.FileExists(path))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }

    NavalSim.Core.Logbook LoadBook()
    {
        if (!FileAccess.FileExists(BookPath)) return new NavalSim.Core.Logbook();
        using var f = FileAccess.Open(BookPath, FileAccess.ModeFlags.Read);
        return f == null ? new NavalSim.Core.Logbook() : NavalSim.Core.Logbook.FromJson(f.GetAsText());
    }

    void SaveBook()
    {
        if (_book == null) return;
        KeepEstimate();
        using var f = FileAccess.Open(BookPath, FileAccess.ModeFlags.Write);
        f?.StoreString(_book.ToJson());
    }

    /* UN SEUL HAVRE À LA FOIS, et c'est assez : on n'est jamais dans deux ports.
       Le plus proche est poussé au shader de la mer ET à la passe d'écume — les
       deux qui, avec l'échantillonneur du noyau, doivent lire le MÊME abri. */
    void PushHarbour(Vec3d here)
    {
        if (_world == null) return;
        Harbour? best = null;
        double bestD = double.MaxValue;
        foreach (var isl in _world.Isles)
        {
            if (isl.Port.Harbour is not Harbour H) continue;
            double dx = H.Cx - here.X, dz = H.Cz - here.Z;
            double d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = H; }
        }
        /* LE HAVRE EST POUSSÉ DANS LE REPÈRE OÙ LES SHADERS TRAVAILLENT, c'est-à-dire
           DÉCALÉ DE L'ORIGINE FLOTTANTE — et c'est tout le sujet du défaut corrigé ici.

           Le monde tient ses havres en mètres VRAIS ; la mer, l'écume et le fond,
           eux, calculent près de zéro, sur des coordonnées dont l'origine a glissé.
           Ces uniformes partaient en mètres vrais : tant qu'on naviguait autour de
           zéro les deux se confondaient, mais mouiller dans un port REBASE l'origine
           SUR le port (voir Moor), et dès lors le shader mesurait la distance entre
           un point proche de zéro et un centre de havre à plusieurs centaines de
           mètres. Il le trouvait hors du bassin et ne calmait rien.

           Conséquence, signalée : « le navire à quai est comme figé, et si j'ajoute
           de la houle l'eau lui est indifférente ». Elle l'était en effet — la coque
           flottait sur une mer abritée à 12 % (le noyau, lui, convertit bien en
           mètres vrais avant d'appeler World.Shelter) pendant que l'ŒIL voyait la
           houle du large entrer dans la rade. Deux calculateurs sur trois lisaient
           le même abri, et c'est le troisième qu'on regardait. */
        var o = _sea.Core.Origin;
        var v = best is Harbour h && bestD < 4000 * 4000
            ? new Vector4((float)(h.Cx - o.X), (float)(h.Cz - o.Z), (float)(h.R + h.Wall), 1)
            : Vector4.Zero;
        /* The inner radius travels with the pass point: the shader used to take it
           back out of u_harbour.z with the wall's 18 m written into it, a second
           definition that World would not have kept in step. */
        var pass = best is Harbour h2
            ? new Vector4((float)(h2.Px - o.X), (float)(h2.Pz - o.Z), (float)h2.R, 0) : Vector4.Zero;

        /* L'ABRI DU RIVAGE, aux MÊMES matières que le havre — et c'est la règle des
           trois calculateurs : le noyau le lit déjà par World.Shelter. La grille la
           plus proche, son coin décalé de l'origine comme le havre. */
        ShelterMap? near = null;
        double nd = double.MaxValue;
        foreach (var map in _world.ShelterMaps)
        {
            double mx = map.X0 + map.Size * 0.5 - here.X, mz = map.Z0 + map.Size * 0.5 - here.Z;
            double d2 = mx * mx + mz * mz;
            if (d2 < nd) { nd = d2; near = map; }
        }
        Texture2D? tex = near != null ? ShelterTexture(near) : null;
        // le coin de la texture : la demi-maille avant le premier nœud, le texel étant centré sur lui
        var rect = near != null
            ? new Vector4((float)(near.X0 - 0.5 * ShelterMap.Cell - o.X), (float)(near.Z0 - 0.5 * ShelterMap.Cell - o.Z),
                          (float)(1 / near.Size), 1)
            : Vector4.Zero;
        /* The same four to every reader, every frame. Writing only on change would
           save little (this is a fraction of a millisecond) and would miss a
           material created after the last change: the silent failure of rule three. */
        void Shel(ShaderMaterial? m)
        {
            if (m == null) return;
            m.SetShaderParameter(U.Harbour, v);
            m.SetShaderParameter(U.HarbourPass, pass);
            m.SetShaderParameter(U.ShelterRect, rect);
            if (tex != null) m.SetShaderParameter(U.ShelterTex, tex);
        }
        Shel(_sea.Material);
        foreach (var fm in _foam.Materials) Shel(fm);
        // le reflet du teleporteur ride sur la MEME mer, donc sur le meme abri
        if (_ship != null) foreach (var m in _ship.MirrorMaterials) Shel(m);
        // la lumière du fond lit le même abri : une rade calme n'a pas les
        // nervures d'une rade battue
        Shel(_land?.Ground);
        Shel(_fishNode?.Material);
        Shel(ShipNode.Caustic);
        Shel(_mist?.Material);
    }

    readonly Dictionary<ShelterMap, ImageTexture> _shelterTex = new();

    /// <summary>La grille d'abri en texture, une fois : un flottant par maille.</summary>
    ImageTexture ShelterTexture(ShelterMap map)
    {
        if (_shelterTex.TryGetValue(map, out var t)) return t;
        var bytes = new byte[map.V.Length * 4];
        Buffer.BlockCopy(map.V, 0, bytes, 0, bytes.Length);
        var img = Image.CreateFromData(map.N, map.N, false, Image.Format.Rf, bytes);
        return _shelterTex[map] = ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// BÂTIR LES VILLES, une fois. Chaque port porte la sienne — on ne mouille
    /// pas devant un rivage désert —, et la fiche peut en déclarer d'autres là
    /// où il n'y a pas de ponton : Kingston, sur la rive d'en face.
    /// </summary>
    void BuildTowns()
    {
        if (_world == null || _town == null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        uint seed = 1;
        foreach (var isl in _world.Isles)
        {
            // un débarcadère n'a pas de ville : c'est ce qui le distingue d'un port
            if (isl.Wild) continue;
            _town.Build(isl.Name, isl.X, isl.Z, 420, 150, seed += 7919);
            _town.BuildCentre(isl);
        }
        foreach (var t in _world.Region.Towns)
        {
            var g = _world.Geo.ToXZ(t.Lat, t.Lon);
            _town.Build(t.Name, g.X, g.Z, t.Radius, t.Houses, seed += 7919);
        }
        GD.Print(FormattableString.Invariant($"villes bâties en {watch.Elapsed.TotalMilliseconds:F0} ms"));
        Plage();
    }

    /// <summary>
    /// DU MONDE SUR LA PLAGE DU PORT DE DÉPART.
    ///
    /// Semé après les villes et pour la même raison : c'est du décor posé une
    /// fois, quand rien ne bouge encore. Et seulement là où le joueur commence —
    /// quatorze plages peuplées coûteraient quatorze fois le semis pour treize
    /// endroits qu'il ne verra peut-être jamais, et le semis est le seul travail
    /// coûteux ici (le dessin, lui, est instancié).
    /// </summary>
    void Plage()
    {
        if (_folk == null || _world?.StartPort is not NavalSim.Core.Isle home) return;
        if (Vat.Load("pirate_0001") is not Vat.Figure f) return;
        _folk.Plant(f, home.Name, home.X, home.Z, 320, 20, 20260929);
    }

    /// <summary>
    /// À SON POSTE. Le zéro local EST le poste : l'origine flottante s'y place,
    /// la coque reste à zéro, et tout ce qui se calcule près d'elle garde sa
    /// précision. C'est ce que fait la page au lancement.
    /// </summary>
    /// <summary>
    /// LA MER, POUSSÉE À QUI LA LIT — houle, aiguisage, soleil et ciel. Tout ce qui
    /// inclut gerstner.gdshaderinc en a besoin : la mer elle-même, l écume, les
    /// caustiques, la brume rasante, et le reflet du téléporteur.
    /// </summary>
    void PushSeaTo(ShaderMaterial m)
    {
        _sea.PushWaves(m);
        m.SetShaderParameter(U.Sharp, (float)_sea.Core.Sharp);
        m.SetShaderParameter(U.Sunlit, (float)_sky.Sunlit);
        _sky.PushTo(m);
    }

    void Moor()
    {
        if (_world?.StartPort is not NavalSim.Core.Isle home) return;
        var (x, z, heading) = NavalSim.Core.Berth.At(home, _ship.Spec.L, _ship.Spec.B);
        /* UN DÉPART ÉCRIT DANS LA FICHE l'emporte sur le ponton : une place, un cap
           (boussole : 0 nord, 90 est — d'où le signe), et l'ancre au fond dès la
           première image. */
        if (home.StartAt is { } st)
        {
            (x, z, heading) = (st.X, st.Z, Compass.YawOf(st.Cap));
            _anchorAtStart = true;
        }
        if (_askHeading is double ask) heading = ask;
        RecentreOn(x, z);
        var b = _ship.Physics.Body;
        b.Pos = new Vec3d(0, b.Pos.Y, 0);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), heading);
        _ship.SyncTransform();
        double bed = _world.HeightAt(x, z);
        var fix = _world.Geo.Fix(x, z);
        GD.Print(FormattableString.Invariant(
            $"à quai : {home.Name}, {NavalSim.Core.Geo.Format(fix.Lat, true)} {NavalSim.Core.Geo.Format(fix.Lon, false)}, {-bed:F1} m d'eau"));
    }

    /// <summary>
    /// REVENIR AU PONTON — se ramener au port de départ sans rien déranger de ce
    /// qui navigue.
    ///
    /// C'est le cousin de <see cref="Moor()"/>, et toute la différence tient en
    /// une ligne qu'il ne fait PAS : il ne touche pas à l'origine.
    ///
    /// Moor() déplace l'ORIGINE du monde jusqu'au ponton et pose la coque à zéro.
    /// C'est juste au départ d'une partie, où rien d'autre ne flotte — mais un
    /// changement d'origine emmène tout le monde avec lui : les voiles croisées,
    /// le pirate qu'on fuyait, l'épave qu'on venait de faire, tous se
    /// retrouveraient au port. Ce ne serait pas un retour, ce serait un
    /// déménagement.
    ///
    /// Ici on ne bouge QUE la coque, en lui donnant la position locale qui la
    /// met au ponton. Tout le reste garde la sienne, donc sa place vraie. Le
    /// glissement du monde, en fin d'image, recentrera l'ensemble — c'est un pur
    /// changement de repère, il ne déplace personne.
    ///
    /// L'ESTIME EST RECALÉE, parce qu'on sait où l'on est : un capitaine qui
    /// rentre au port relève sa position sur les amers. Sans cela le point
    /// estimé resterait au large, avec l'erreur qu'on venait d'accumuler.
    /// </summary>
    void BackToBerth()
    {
        if (_inTitle || _ship == null || _world?.StartPort is not NavalSim.Core.Isle home) return;
        var (x, z, heading) = NavalSim.Core.Berth.At(home, _ship.Spec.L, _ship.Spec.B);
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        b.Pos = new Vec3d(x - o.X, b.Pos.Y, z - o.Z);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), heading);
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        _ship.SyncTransform();
        /* On rentre AU PONTON, donc sous voiles serrées et machine stoppée : y
           arriver toute toile dehors ferait repartir la coque à la seconde même,
           et le joueur croirait le bouton cassé. */
        _ship.Ctrl.SailsSet = false;
        _ship.Ctrl.Throttle = 0;
        _reck?.Fix(TruePos().X, TruePos().Z);
        _menu.Visible = false;
        Say($"De retour à {home.Name}");
        JournalLog($"Retour au ponton de {home.Name}.");
    }
}
