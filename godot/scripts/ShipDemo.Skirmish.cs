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
/// L'adversaire de chacun est choisi une fois et gardé tant qu'il flotte —
/// rechoisir à chaque image ferait louvoyer la barre entre deux proies
/// équidistantes — et c'est celui que LE MOINS DE MONDE attaque déjà, la distance
/// ne départageant que les ex æquo. Une bataille, et non une procession.
/// </summary>
public partial class ShipDemo
{
    /// <summary>La bataille est-elle en cours ? Seul ce mode fait des camps.</summary>
    bool _skirmish;
    /// <summary>Qui vise qui : gardé tant que la proie flotte.</summary>
    readonly Dictionary<ShipNode, ShipNode> _melee = new();
    /// <summary>La fin est dite une fois : le panneau ne revient pas à chaque image.</summary>
    bool _meleeDone;
    /// <summary>Les deux camps ont-ils été à flot en même temps ? Sans quoi une ligne
    /// à demi mise à l'eau se déclarerait vainqueur avant que l'autre existe.</summary>
    bool _meleeJoined;
    Label? _meleeLine;
    Nation? _campA, _campB;

    /// <summary>Douze coques : ce que l'utilisateur a demandé, sous le plafond de seize (mesuré).</summary>
    const int SkirmishHulls = 12;

    /// <summary>
    /// La demi-distance entre les deux lignes : 250 m de part et d'autre, donc
    /// 500 m au premier regard — hors du plein fouet (340 m), le temps de choisir
    /// son bord, et pas plus.
    /// </summary>
    const double MeleeGap = 250;

    /// <summary>
    /// L'erre qu'une escadre a en se présentant : trois mètres par seconde, six
    /// nœuds. On n'arrive pas en bataille à l'arrêt.
    /// </summary>
    const double MeleeWay = 3.0;

    /* CE QUI PEUT SE BATTRE — ET ON LE VÉRIFIE, on ne le suppose pas (demandé).
       Une batterie ne se lit qu'une fois le modèle chargé : on ne peut donc pas
       la demander AVANT de mettre une coque à l'eau. Une fiche est donc essayée,
       et renvoyée au port si elle n'a pas un canon — une fois pour toutes, la
       fiche stérile étant retenue pour ne pas la rappeler onze fois.

       La liste n'est qu'un ORDRE DE PRÉFÉRENCE : elle dit par quoi commencer, et
       le reste du dossier suit si elle ne suffit pas. */
    static readonly string[] Combatants =
        { "frigate17e.json", "frigate.json", "pirate.json", "schooner.json", "cotre.json" };

    /// <summary>Les fiches essayées qui n'avaient pas de batterie : jamais rappelées.</summary>
    readonly HashSet<int> _barren = new();

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

    /// <summary>Au-delà, la proie est abandonnée : on ne traverse pas la mer derrière elle.</summary>
    const double MeleeGiveUp = 1200;

    /// <summary>
    /// L'adversaire qu'une coque s'est donné dans la mêlée : celui que le moins de
    /// monde attaque déjà, le plus proche à égalité. Gardé tant qu'il flotte et
    /// qu'il est à portée de poursuite, repris quand il sombre ou s'éloigne trop.
    /// </summary>
    ShipNode? MeleeFoe(ShipNode s)
    {
        var from = s.Physics.Body.Pos;
        if (_melee.TryGetValue(s, out var kept) && IsInstanceValid(kept)
            && !kept.Physics.Foundered && !Allied(s, kept)
            && (kept.Physics.Body.Pos - from).Length < MeleeGiveUp) return kept;

        /* CHACUN SON ADVERSAIRE, et c'est ce qui fait une bataille plutôt qu'une
           procession. Le plus proche seul ne suffit pas : douze coques trouvaient
           le MÊME plus proche, s'y rendaient toutes, et comme la barre contourne
           la garde d'une proie toujours du même côté, elles se rangeaient en file
           indienne derrière elle sans jamais lui présenter le travers (signalé,
           capture à l'appui). On regarde donc d'abord COMBIEN de coques visent
           déjà chaque adversaire, et la distance ne départage que les ex æquo. Un
           deuxième assaillant n'arrive donc sur une proie que lorsque tout le
           monde en a une. */
        var busy = new Dictionary<ShipNode, int>();
        foreach (var (who, whom) in _melee)
        {
            if (who == s || !IsInstanceValid(who) || who.Physics.Foundered) continue;
            if (!IsInstanceValid(whom)) continue;
            busy[whom] = busy.GetValueOrDefault(whom) + 1;
        }

        ShipNode? best = null;
        int bn = int.MaxValue;
        double bd = double.MaxValue;
        void Weigh(ShipNode o)
        {
            if (o == s || o.IsGhost || o.Physics.Foundered || Allied(s, o)) return;
            var d = o.Physics.Body.Pos - from;
            double q = d.X * d.X + d.Z * d.Z;
            int n = busy.GetValueOrDefault(o);
            if (n > bn || (n == bn && q >= bd)) return;
            bn = n; bd = q; best = o;
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
    /// DEUX LIGNES DE FILE QUI SE PRÉSENTENT LE TRAVERS, à cinq cents mètres —
    /// hors de portée du plein fouet (trois cent quarante mètres), pour qu'on ait
    /// le temps de choisir son bord avant que ça parle, et pas davantage. Chacune
    /// vient sur l'autre, avec son erre et par bonne brise.
    /// </summary>
    void Skirmish()
    {
        _quests?.Stop();
        SaveQuests();
        Home();                      // une ligne neuve : personne autour, la bourse pleine
        Offshore();                  // et de l'eau libre, loin des côtes
        _melee.Clear();
        _barren.Clear();              // un modèle a pu recevoir sa batterie depuis
        _meleeDone = false;
        _meleeJoined = false;
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
            foreach (var name in Combatants)
            {
                int k = _paths.FindIndex(p => System.IO.Path.GetFileName(p) == name);
                if (k < 0) continue;
                Launch(k);
                Offshore();
                if (_ship.Battery.Guns.Count > 0) break;
            }

        // le vôtre : vous menez la première ligne
        _ship.SetEnsign(_campA.Image, _campA);
        _ship.ShowColours(true, true);
        _colours = true;

        /* DU VENT POUR SE BATTRE. Offshore() pose le temps de l'AFFICHE — force 3,
           « il fait route, lentement » —, et deux lignes s'y rejoignaient à deux
           nœuds et demi : quatre minutes avant le premier coup, pendant lesquelles
           on ne voit qu'une procession (signalé). Force 5 par le même 75°, et les
           coques arrivent AVEC DE L'ERRE, comme une escadre qui se présente. */
        _force = 5; _windDeg = 75;
        Restate();

        var me = _ship.Physics.Body;
        // l'étrave au nord, les deux lignes se présentent par le travers
        me.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), 0);
        me.Pos = new Vec3d(0, me.Pos.Y, -MeleeGap);
        me.Vel = me.Quat.Rotate(new Vec3d(0, 0, MeleeWay));
        _ship.SyncTransform();

        int perSide = SkirmishHulls / 2;
        // les préférées d'abord, puis tout le reste du dossier : c'est l'essai qui tranche
        var specs = new List<int>();
        foreach (var name in Combatants)
        {
            int k = _paths.FindIndex(p => System.IO.Path.GetFileName(p) == name);
            if (k >= 0) specs.Add(k);
        }
        for (int k = 0; k < _paths.Count; k++) if (!specs.Contains(k)) specs.Add(k);
        if (specs.Count == 0) { Say("Aucune fiche de navire"); _skirmish = false; Play(); return; }

        // vous comptez pour un : onze coques à mettre à l'eau, cinq à vous, six en face
        int born = 0;
        for (int n = 0; n < SkirmishHulls - 1; n++)
        {
            bool mine = n < perSide - 1;
            int rank = mine ? n + 1 : n - (perSide - 1);
            // on descend la liste jusqu'à une coque qui porte des pièces
            for (int t = 0; t < specs.Count; t++)
            {
                int idx = specs[(n + 1 + t) % specs.Count];
                if (_barren.Contains(idx)) continue;
                if (!Line(idx, mine, rank)) continue;
                born++;
                break;
            }
        }
        if (born < SkirmishHulls - 1)
            GD.PushWarning($"escarmouche : {born} coque(s) armee(s) seulement sur {SkirmishHulls - 1} demandees");

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

        /* PAS DE COQUE DÉSARMÉE DANS LA LIGNE (demandé). Un navire sans pièce ne
           peut que servir de cible : il fausse le compte des camps et fait durer
           une bataille qu'il ne peut pas conclure. On la remet au port, et on
           retient la fiche pour ne pas la rappeler. */
        if (s.Battery.Guns.Count == 0)
        {
            _barren.Add(specIndex);
            GD.Print($"escarmouche : {s.Spec.Name} n a pas de batterie, ecartee de la ligne");
            RemoveShip(s);
            return false;
        }

        var b = s.Physics.Body;

        /* EN LIGNE DE FILE, l'une derrière l'autre à cent trente mètres — deux
           longueurs de frégate, ce qu'une escadre tenait pour ne pas s'aborder en
           virant. Les deux lignes de part et d'autre, à MeleeGap. */
        double x = (rank - 2.5) * 130;
        double z = mine ? -MeleeGap : MeleeGap;
        double now = _stepT0 + _stepSub * _stepDt;
        b.Pos = new Vec3d(x, b.Pos.Y + _sea.Core.Sample(x, z, now), z);
        b.AngVel = Vec3d.Zero;
        // cap au nord (+z) pour la vôtre, au sud pour l'autre : elles se ferment
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), mine ? 0 : Math.PI);
        b.Vel = b.Quat.Rotate(new Vec3d(0, 0, MeleeWay));
        s.SyncTransform();

        // le camp, pavillon visible ou non : c est lui qui dit qui elle ne doit pas canonner
        var n = mine ? _campA : _campB;
        if (n != null) { s.SetEnsign(n.Image, n); s.ShowColours(true, true); }

        /* À PORTÉE DE MOUSQUET, et c'est mesuré. On avait mis deux cent vingt
           mètres de garde — « au plein fouet, et pas plus près » —, ce qui donne
           trois cents mètres de portée réelle une fois la tangente prise. Or à
           trois cents mètres un boulet a déjà plongé de trois mètres et demi et
           tombe à l'eau AVANT la muraille : cent cinquante-quatre charges brûlées
           sans qu'une coque s'en ressente. Cent vingt mètres de garde donnent
           cent soixante de portée, où le boulet est encore à hauteur de bordé et
           mord aux deux tiers — c'est la distance à laquelle on se battait. */
        HelmOf(s).Standoff = 120;
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
        if (a > 0 && b > 0) { _meleeJoined = true; return; }
        if (_meleeDone || !_meleeJoined) return;
        _meleeDone = true;
        EndMelee(a, b);
    }

    // ------------------------------------------------------------------
    //  LE MOT DE LA FIN
    // ------------------------------------------------------------------

    PanelContainer? _meleePanel;
    Label? _meleeTitle, _meleeSay;

    /* LE PANNEAU DE LA FIN. Il ne met rien en pause — la mer continue derrière
       lui, comme le menu d'Échap : une bataille gagnée se regarde depuis le pont,
       pas depuis un écran noir. Deux sorties seulement : rester en mer sur le
       champ de bataille, ou rentrer au menu. */
    void BuildMelee(CanvasLayer layer)
    {
        _meleePanel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -260, OffsetRight = 260, OffsetTop = -140,
            Visible = false
        };
        _meleePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.94f),
            BorderColor = new Color(0.55f, 0.44f, 0.20f, 0.9f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 18, ContentMarginBottom = 20
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        _meleePanel.AddChild(box);
        layer.AddChild(_meleePanel);

        /* VICTOIRE ou DÉFAITE dans l'anglaise du titre : c'est le seul mot de tout
           le jeu qu'on veut lire de loin et sans chercher. */
        _meleeTitle = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _meleeTitle.AddThemeFontSizeOverride("font_size", HandFont.Get() != null ? 64 : 40);
        if (HandFont.Get() is { } hand) _meleeTitle.AddThemeFontOverride("font", hand);
        box.AddChild(_meleeTitle);

        _meleeSay = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _meleeSay.AddThemeFontSizeOverride("font_size", 16);
        _meleeSay.AddThemeColorOverride("font_color", new Color(0.93f, 0.92f, 0.88f));
        box.AddChild(_meleeSay);

        void Entry(string text, Action go)
        {
            var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, Flat = true };
            b.AddThemeFontSizeOverride("font_size", 18);
            b.Pressed += go;
            box.AddChild(b);
        }
        Entry("Rester en mer", () => { if (_meleePanel != null) _meleePanel.Visible = false; });
        Entry("Menu principal", () => { if (_meleePanel != null) _meleePanel.Visible = false; Open(); MainItems(); });
    }

    /// <summary>
    /// QUI A GAGNÉ : le camp du joueur, et non le joueur. Sa coque peut être au
    /// fond pendant que sa ligne balaie la mer — c'est une victoire, et c'est bien
    /// ainsi qu'une bataille se compte.
    /// </summary>
    void EndMelee(int a, int b)
    {
        if (_meleePanel == null || _meleeTitle == null || _meleeSay == null) return;
        var mine = _ship.Ensign?.Id == _campB?.Id ? _campB : _campA;
        var his = mine == _campA ? _campB : _campA;
        int left = mine == _campA ? a : b, gone = mine == _campA ? b : a;
        bool won = left > 0 && gone == 0;

        _meleeTitle.Text = won ? "Victoire" : gone > 0 ? "Défaite" : "Sans vainqueur";
        _meleeTitle.AddThemeColorOverride("font_color",
            won ? new Color(0.95f, 0.84f, 0.42f) : new Color(0.80f, 0.42f, 0.38f));
        _meleeSay.Text = won
            ? $"{CampName(his)} n'a plus une coque à flot. {CampName(mine)} reste maître de la mer "
              + $"avec {left} navire{(left > 1 ? "s" : "")}"
              + (_ship.Physics.Foundered ? " — mais vous n'êtes plus du nombre." : ".")
            : gone > 0
              ? $"{CampName(mine)} n'a plus une coque à flot. {CampName(his)} reste maître de la mer "
                + $"avec {gone} navire{(gone > 1 ? "s" : "")}."
              : "Les deux lignes sont au fond. La mer n'est à personne.";
        _meleePanel.Visible = true;
        GD.Print($"escarmouche finie : {CampName(_campA)} {a} contre {CampName(_campB)} {b} — "
               + (won ? "victoire" : gone > 0 ? "defaite" : "sans vainqueur"));
    }
}
