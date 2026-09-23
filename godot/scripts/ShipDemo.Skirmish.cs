using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'ESCARMOUCHE — douze coques, deux pavillons, et rien d'autre à faire que la
/// bataille.
///
/// CE QUI CHANGE, ET C'EST TOUT : jusqu'ici l'hostilité était une PAIRE, née d'un
/// boulet reçu — celui qui vous touche devient votre ennemi, un contre un. Une
/// escarmouche demande la notion qui manquait, le CAMP : deux navires qui portent
/// le même pavillon ne se tirent jamais dessus, <b>y compris quand l'un reçoit un
/// boulet de l'autre</b>. Un coup fratricide n'est plus qu'une maladresse.
///
/// Le camp, c'est le pavillon, et il existait déjà : <see cref="ShipNode.Ensign"/>
/// est l'entrée de flags.json qu'une coque arbore. Rien de nouveau n'est inventé
/// pour le porter. Amener ses couleurs ne change PAS de camp — la drisse baisse
/// l'étoffe, elle ne change pas la nation —, et c'est voulu : on ne déserte pas
/// une ligne de bataille en tirant sur un bout.
///
/// L'ennemi de chacun est le plus proche d'un AUTRE pavillon, choisi une fois et
/// gardé tant qu'il flotte : rechoisir à chaque image ferait louvoyer la barre
/// entre deux proies équidistantes.
/// </summary>
public partial class ShipDemo
{
    /// <summary>La bataille est-elle en cours ? Seul ce mode fait des camps.</summary>
    bool _skirmish;
    /// <summary>Qui vise qui : gardé tant que la proie flotte.</summary>
    readonly Dictionary<ShipNode, ShipNode> _melee = new();
    /// <summary>Le compte des deux camps, dit une fois quand l'un d'eux disparaît.</summary>
    bool _meleeDone;
    Label? _meleeLine;
    Nation? _campA, _campB;

    /// <summary>Douze coques : ce que l'utilisateur a demandé, sous le plafond de seize (mesuré).</summary>
    const int SkirmishHulls = 12;

    /* CE QUI PEUT SE BATTRE. Une escarmouche ne met pas un chaland ni une caisse
       en ligne : on prend les coques qui portent de la toile ET une batterie.
       Nommées ici plutôt que devinées, parce que la batterie ne se lit qu'une fois
       le modèle chargé — trop tard pour choisir qui mettre à l'eau. */
    static readonly string[] Combatants =
        { "frigate17e.json", "frigate.json", "pirate.json", "schooner.json", "cotre.json" };

    // ------------------------------------------------------------------
    //  LES CAMPS
    // ------------------------------------------------------------------

    /// <summary>Deux coques du même bord ? Sans pavillon, on n'est l'amie de personne.</summary>
    bool Allied(ShipNode a, ShipNode b)
    {
        if (a == b) return true;
        var na = a.Ensign; var nb = b.Ensign;
        return na != null && nb != null && na.Id == nb.Id;
    }

    /// <summary>
    /// L'ennemi qu'une coque s'est donné dans la mêlée : le plus proche d'un autre
    /// pavillon. Gardé tant qu'il flotte, repris quand il sombre.
    /// </summary>
    ShipNode? MeleeFoe(ShipNode s)
    {
        if (_melee.TryGetValue(s, out var kept) && IsInstanceValid(kept)
            && !kept.Physics.Foundered && !Allied(s, kept)) return kept;

        ShipNode? best = null;
        double bd = double.MaxValue;
        var from = s.Physics.Body.Pos;
        void Weigh(ShipNode o)
        {
            if (o == s || o.IsGhost || o.Physics.Foundered || Allied(s, o)) return;
            var d = o.Physics.Body.Pos - from;
            double q = d.X * d.X + d.Z * d.Z;
            if (q < bd) { bd = q; best = o; }
        }
        Weigh(_ship);
        foreach (var o in _others) Weigh(o);

        if (best != null) _melee[s] = best; else _melee.Remove(s);
        return best;
    }

    /// <summary>Ce qui reste debout de chaque camp.</summary>
    (int A, int B) MeleeCount()
    {
        int a = 0, b = 0;
        void Tally(ShipNode s)
        {
            if (s.IsGhost || s.Physics.Foundered) return;
            if (_campA != null && s.Ensign?.Id == _campA.Id) a++;
            else if (_campB != null && s.Ensign?.Id == _campB.Id) b++;
        }
        Tally(_ship);
        foreach (var o in _others) Tally(o);
        return (a, b);
    }

    static string CampName(Nation? n) => n?.Pays ?? n?.Id ?? "sans pavillon";

    // ------------------------------------------------------------------
    //  LA METTRE EN LIGNE
    // ------------------------------------------------------------------

    /// <summary>
    /// DEUX LIGNES DE FILE QUI SE PRÉSENTENT LE TRAVERS, à six cents mètres — hors
    /// de portée du plein fouet (trois cent quarante mètres), pour qu'on ait le
    /// temps de choisir son bord avant que ça parle. Chacune vient sur l'autre.
    /// </summary>
    void Skirmish()
    {
        _quests?.Stop();
        SaveQuests();
        Home();                      // une ligne neuve : personne autour, la bourse pleine
        Offshore();                  // et de l'eau libre, loin des côtes
        _melee.Clear();
        _meleeDone = false;
        _skirmish = true;

        // deux pavillons différents, tirés parmi les nations — jamais le noir
        var pool = _nations.All.FindAll(x => !x.Pirate);
        if (pool.Count < 2) { Say("Il faut deux pavillons pour une escarmouche"); _skirmish = false; Play(); return; }
        int i0 = _flagRng.Next(pool.Count);
        int i1 = (i0 + 1 + _flagRng.Next(pool.Count - 1)) % pool.Count;
        _campA = pool[i0]; _campB = pool[i1];

        /* PAS DE BATAILLE EN CHALAND : une coque sans batterie prend celle du
           premier combattant. On ne peut pas jouer une escarmouche dans un navire
           qui ne peut pas tirer, et le navire courant est celui qu on avait. */
        if (_ship.Battery.Guns.Count == 0)
        {
            int k = _paths.FindIndex(p => System.IO.Path.GetFileName(p) == Combatants[0]);
            if (k >= 0) { Launch(k); Offshore(); }
        }

        // le vôtre : vous menez la première ligne
        _ship.SetEnsign(_campA.Image, _campA);
        _ship.ShowColours(true, true);
        _colours = true;

        var me = _ship.Physics.Body;
        // l'étrave au nord, les deux lignes se présentent par le travers
        me.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), 0);
        me.Pos = new Vec3d(0, me.Pos.Y, -300);
        _ship.SyncTransform();

        int perSide = SkirmishHulls / 2;
        var specs = new List<int>();
        foreach (var name in Combatants)
        {
            int k = _paths.FindIndex(p => System.IO.Path.GetFileName(p) == name);
            if (k >= 0) specs.Add(k);
        }
        if (specs.Count == 0) { Say("Aucun navire de combat dans ships/"); _skirmish = false; Play(); return; }

        // vous comptez pour un : onze coques à mettre à l'eau, cinq à vous, six en face
        int born = 0;
        for (int n = 0; n < SkirmishHulls - 1; n++)
        {
            bool mine = n < perSide - 1;
            int rank = mine ? n + 1 : n - (perSide - 1);
            if (!Line(specs[(n + 1) % specs.Count], mine, rank)) continue;
            born++;
        }

        var c = MeleeCount();
        GD.Print(FormattableString.Invariant(
            $"escarmouche : {CampName(_campA)} {c.A} contre {CampName(_campB)} {c.B}, {born} coque(s) mises a l eau"));
        ShowNotice("Escarmouche",
            $"{CampName(_campA)} contre {CampName(_campB)}, {c.A} contre {c.B}. "
            + "Vous menez la ligne au sud. Un navire de votre pavillon ne vous tirera jamais dessus.");
        _reck?.Fix(TruePos().X, TruePos().Z);
        Play();
    }

    /// <summary>
    /// Une coque de plus dans une ligne. <paramref name="mine"/> : la vôtre, au
    /// sud, cap au nord ; sinon celle d'en face, au nord, cap au sud. Faux si elle
    /// n'a pas pu être mise à l'eau.
    /// </summary>
    bool Line(int specIndex, bool mine, int rank)
    {
        int before = _others.Count;
        /* SANS L HUMEUR DE PIRATE. Arm() inscrit la coque comme pirate, et un pirate
           choisit sa proie lui-meme : le pavillon ne compterait plus, et les onze
           se jetteraient sur vous. Ici c est le camp qui commande, rien d autre. */
        SpawnFleet(1, specIndex, arm: false);
        if (_others.Count == before) return false;
        var s = _others[^1];
        var b = s.Physics.Body;

        /* EN LIGNE DE FILE, l'une derrière l'autre à cent trente mètres — deux
           longueurs de frégate, ce qu'une escadre tenait pour ne pas s'aborder en
           virant. Les deux lignes à trois cents mètres de part et d'autre. */
        double x = (rank - 2.5) * 130;
        double z = mine ? -300 : 300;
        double now = _stepT0 + _stepSub * _stepDt;
        b.Pos = new Vec3d(x, b.Pos.Y + _sea.Core.Sample(x, z, now), z);
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        // cap au nord (+z) pour la vôtre, au sud pour l'autre : elles se ferment
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), mine ? 0 : Math.PI);
        s.SyncTransform();

        // le camp, pavillon visible ou non : c est lui qui dit qui elle ne doit pas canonner
        var n = mine ? _campA : _campB;
        if (n != null) { s.SetEnsign(n.Image, n); s.ShowColours(true, true); }

        /* AU PLEIN FOUET, ET PAS PLUS PRÈS : une barre réglée court, et douze
           coques finissent en tas au milieu. Deux cent vingt mètres, c'est encore
           dans la portée utile et cela laisse la place de virer. */
        HelmOf(s).Standoff = 220;
        s.Ctrl.SailsSet = true;
        s.Ctrl.Sheet = 0.6;
        return true;
    }

    // ------------------------------------------------------------------
    //  LE COMPTE, PENDANT
    // ------------------------------------------------------------------

    /// <summary>Le tableau des deux camps, en haut à droite, et le mot de la fin.</summary>
    void MeleeTick()
    {
        if (_meleeLine == null) return;
        _meleeLine.Visible = _skirmish && _hudOn && !_inTitle;
        if (!_skirmish) return;
        var (a, b) = MeleeCount();
        _meleeLine.Text = $"{CampName(_campA)} {a}   ·   {CampName(_campB)} {b}";
        if (_meleeDone || (a > 0 && b > 0)) return;
        _meleeDone = true;
        Say(a > 0 ? "La journée est à " + CampName(_campA) + " !"
                  : b > 0 ? CampName(_campB) + " reste maître de la mer" : "Plus personne à flot");
    }
}
