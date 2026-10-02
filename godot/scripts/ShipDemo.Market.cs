using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE COMPTOIR, et la bourse qu'on y vide ou qu'on y remplit.
///
/// Les règles sont dans le noyau (<see cref="Market"/>, <see cref="Purse"/>),
/// vérifiées contre la page à la pièce près ; il ne reste ici que ce qu'un écran
/// sait faire — dire le cours, prendre l'ordre, et le porter dans la cale.
///
/// LES ÉPICES SONT DU POIDS. Achetées, elles vont dans la cale par
/// <see cref="ShipPhysics.LoadCargo"/>, donc elles enfoncent la coque et
/// changent son assiette comme n'importe quel poids : une cargaison se paie sur
/// l'eau avant de se payer au comptoir. Et l'on ne vend que ce que la cale porte
/// RÉELLEMENT — c'est elle qui fait foi, pas un compteur tenu à côté.
/// </summary>
public partial class ShipDemo : Node3D
{
    Purse _purse = new(Market.Depart);
    readonly Market _market = new();
    Isle? _portHere;
    /// <summary>Sans erre au poste : c'est ce qui autorise les boutons, non la présence du port.</summary>
    bool _alongside;
    /// <summary>Le sous-titre : il dit pourquoi les boutons ne répondent pas.</summary>
    Label? _mkState;
    /// <summary>Le rayon des denrées, la poudre et leurs boutons : ils n'existent qu'à quai.</summary>
    Control? _mkSale;
    Label? _mkFar;

    /* OÙ L'ON RANGE CE QU'ON ACHÈTE : au fond, au milieu, ce qui est l'arrimage
       sûr. Et sur le niveau le plus bas que le plan d'arrimage de la page
       dessine (0,85, 0,50 et 0,18) : des épices chargées plus bas qu'aucune case
       ne pourrait les montrer, le jour où le plan sera porté ici — la page l'a
       appris par un bug signalé à l'usage, « les épices ne s'ajoutent pas ». */
    const double HoldFloor = 0.18;

    PanelContainer? _mkPanel;
    Label? _mkPort, _mkPowder, _mkHold;
    Control? _mkPowderRow;
    /// <summary>Une ligne de denrée : son nom, son cours, ce qu'on en porte, et les deux boutons.</summary>
    readonly List<(Good W, Label Name, Label Price, Label Aboard)> _mkRows = new();
    VBoxContainer? _mkWares;
    VBoxContainer? _mkTreasure;
    (Color Ink, Color Dim, Color Gold) _mkTreasureInk;
    string _mkTreasureSig = "";
    readonly List<(string Kind, Label Price)> _mkTreasureRows = new();
    GridContainer? _mkNews;

    /// <summary>
    /// LES MARCHANDISES, lues AVANT le panneau, puisque c'est la fiche qui dit
    /// combien de lignes il porte. Fichier absent ou abîmé : la liste reste vide
    /// et le comptoir ne montre que la poudre — on le dit, plutôt que d'inventer
    /// des cours que personne n'a écrits.
    /// </summary>
    void LoadWares()
    {
        string path = Assets.Path("market/marchandises.json");
        try
        {
            _market.Wares = Goods.FromJson(System.IO.File.ReadAllText(path));
            GD.Print($"{_market.Wares.List.Count} marchandise(s) au comptoir");
        }
        catch (Exception e)
        {
            GD.PushWarning($"market/marchandises.json illisible ({e.Message}) — le comptoir n'a rien à vendre.");
        }
    }

    /// <summary>
    /// COUPER UN LIBELLÉ AU LIEU DE LE LAISSER POUSSER.
    ///
    /// La largeur MINIMALE d'un Label est celle de son texte entier, et un
    /// conteneur respecte les minimums de ses enfants avant ses propres ancres.
    /// « Hog Crawle de Samuel Barry » suivi de « bois de campêche 1234 » poussait
    /// donc le panneau au-delà de sa boîte ; son bord gauche étant épinglé par
    /// l'ancre, il grandissait VERS LA DROITE et sortait de l'écran — signalé.
    ///
    /// Coupé, son minimum retombe à zéro et la boîte redevient maîtresse. Les
    /// points de suite disent qu'il manque quelque chose, ce qu'une coupe franche
    /// ne dirait pas — et l'infobulle porte le texte entier.
    /// </summary>
    static Label Ellipse(Label l)
    {
        l.ClipText = true;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return l;
    }

    void BuildMarket(CanvasLayer layer)
    {
        LoadWares();
        if (_world != null) _market.Tune(_world.LongestLeg);

        _mkPanel = new PanelContainer
        {
            /* LA LARGEUR EST TENUE PAR LES ANCRES, et il a fallu que le contenu la
               respecte : voir Ellipse plus bas. Un peu plus large qu'avant parce que
               le bandeau des autres ports porte des noms longs. */
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -392, OffsetRight = -14, OffsetTop = 130,
            Visible = false
        };
        _mkPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.78f),
            BorderColor = new Color(0.55f, 0.44f, 0.20f, 0.8f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 12
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _mkPanel.AddChild(box);
        layer.AddChild(_mkPanel);

        Label L(string text, int size, Color col)
        {
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", col);
            box.AddChild(l);
            return l;
        }
        var ink = new Color(0.92f, 0.94f, 0.96f);
        var gold = new Color(0.92f, 0.78f, 0.36f);
        var dim = new Color(0.66f, 0.70f, 0.74f);

        _mkPort = L("—", 18, gold);
        _mkState = L("comptoir ouvert", 12, dim);

        /* UNE LIGNE PAR DENRÉE, et elle défile : douze marchandises ne tiennent
           pas sous le comptoir, et couper la liste cacherait justement celles
           qu'on ne connaît pas encore. Le tableau est bâti UNE FOIS d'après la
           fiche — le nombre de denrées ne change pas en cours de partie, et
           refaire les boutons à chaque image les jetterait sous le pointeur,
           comme le panneau de flotte l'a appris. */
        var wscroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 186),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _mkWares = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mkWares.AddThemeConstantOverride("separation", 2);
        wscroll.AddChild(_mkWares);
        box.AddChild(wscroll);
        _mkSale = wscroll;
        foreach (var w in _market.Wares.List) WareRow(w, ink, dim);

        /* LES TRÉSORS À BORD, à la pièce et non à la tonne : un rubis, une coupe,
           une bague. Leur cours varie d'un port à l'autre comme celui des denrées,
           et le joaillier garde sa marge. La section n'existe que s'il y a de quoi
           vendre. */
        _mkTreasure = new VBoxContainer { Visible = false };
        _mkTreasure.AddThemeConstantOverride("separation", 2);
        box.AddChild(_mkTreasure);
        _mkTreasureInk = (ink, dim, gold);
        // le poisson frais et le chantier (ShipDemo.Yard.cs)
        BuildFishAndYard(box);

        _mkPowder = L("", 15, ink);
        _mkPowderRow = Buttons(box, ("Embarquer 25 coups", () => BuyPowder(25)), ("Faire le plein", () => BuyPowder(int.MaxValue)));
        _mkHold = L("", 13, dim);
        /* CE QUI REMPLACE LE COMPTOIR TANT QU'ON N'EST PAS RANGÉ LE LONG. Une place
           vide se lit comme un défaut ; une phrase se lit comme une consigne. */
        _mkFar = L("Rangez-vous le long du ponton pour charger.", 13, gold);
        L("Aux autres ports, à la dernière nouvelle", 13, gold);
        /* UN TABLEAU QUI DÉFILE : vingt ports ne tiennent pas sous le comptoir,
           et couper la liste cacherait justement les plus lointains — ceux dont
           la nouvelle est la plus alléchante et la plus vieille. */
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 230),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _mkNews = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _mkNews.AddThemeConstantOverride("h_separation", 12);
        _mkNews.AddThemeConstantOverride("v_separation", 1);
        scroll.AddChild(_mkNews);
        box.AddChild(scroll);
    }

    /// <summary>
    /// UNE DENRÉE, SA LIGNE. Le nom à gauche, le cours au milieu — ce qu'on
    /// demande et ce qu'on consent —, ce qu'on en porte à droite, et deux
    /// boutons dessous.
    ///
    /// CINQ TONNES ET NON DIX : le sloop des premières missions en déplace
    /// vingt-deux, et un bouton qui échoue neuf fois sur dix n'est pas un
    /// bouton. Le plan d'arrimage reste là pour les gros lots.
    /// </summary>
    void WareRow(Good w, Color ink, Color dim)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        Label C(string text, int size, Color col, HorizontalAlignment al, int min)
        {
            var l = new Label
            {
                Text = text, HorizontalAlignment = al, CustomMinimumSize = new Vector2(min, 0),
                SizeFlagsHorizontal = min == 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill
            };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", col);
            l.TooltipText = w.Note;
            row.AddChild(Ellipse(l));
            return l;
        }
        var name = C(w.Name, 14, ink, HorizontalAlignment.Left, 0);
        var price = C("", 14, new Color(0.95f, 0.88f, 0.66f), HorizontalAlignment.Right, 86);
        var aboard = C("", 12, dim, HorizontalAlignment.Right, 52);

        var acheter = new Button { Text = "+5 t", FocusMode = Control.FocusModeEnum.None };
        acheter.Pressed += () => Buy(w.Key, 5);
        row.AddChild(acheter);
        var vendre = new Button { Text = "−5 t", FocusMode = Control.FocusModeEnum.None };
        vendre.Pressed += () => Sell(w.Key, 5);
        row.AddChild(vendre);

        _mkWares!.AddChild(row);
        _mkRows.Add((w, name, price, aboard));
    }

    /// <summary>Tous les boutons du comptoir, pour les griser d'un coup tant qu'elle a de l'erre.</summary>
    readonly List<Button> _mkButtons = new();

    Control Buttons(VBoxContainer box, params (string Text, Action Do)[] bs)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        foreach (var (text, act) in bs)
        {
            var b = new Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
            b.Pressed += act;
            row.AddChild(b);
            _mkButtons.Add(b);
        }
        box.AddChild(row);
        return row;
    }

    /// <summary>
    /// LE COMPTOIR S'OUVRE À L'APPROCHE, IL NE SE NÉGOCIE QU'À QUAI — et ce sont
    /// deux questions différentes qu'on confondait.
    ///
    /// Le panneau ne paraissait qu'une fois le navire stoppé, si bien qu'on ne
    /// savait ce que le port offrait qu'après y être arrivé. Or c'est en approchant
    /// qu'on décide d'y entrer. Il paraît donc dans le rayon, quelle que soit
    /// l'erre ; ce sont les BOUTONS qui attendent qu'elle tombe, et le bandeau le
    /// dit — signalé, et la distinction valait d'être faite plutôt que de déplacer
    /// un seuil.
    /// </summary>
    /// <summary>
    /// EST-ELLE RANGÉE LE LONG DE CE PONTON ? La distance de la coque au SEGMENT
    /// du tablier, non à son musoir : on s'amarre le long, et un navire à mi-ponton
    /// est plus à quai qu'un navire au bout.
    /// </summary>
    bool Alongside(Isle isl)
    {
        var p = isl.Port;
        if (p.Hx == 0 && p.Hz == 0) return false;
        var o = _sea.Core.Origin;
        double wx = o.X + _ship.Physics.Body.Pos.X, wz = o.Z + _ship.Physics.Body.Pos.Z;
        double ex = p.Hx - p.Sx, ez = p.Hz - p.Sz;
        double ee = Math.Max(1e-9, ex * ex + ez * ez);
        double u = Math.Clamp(((wx - p.Sx) * ex + (wz - p.Sz) * ez) / ee, 0, 1);
        double dx = wx - (p.Sx + ex * u), dz = wz - (p.Sz + ez * u);
        // demi-tablier + demi-bau + une marge de manœuvre
        double seuil = NavalSim.Core.Berth.Width * 0.5 + _ship.Spec.B * 0.5 + 7;
        return dx * dx + dz * dz < seuil * seuil;
    }

    Isle? PortInFront()
    {
        if (_world == null) return null;
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        double wx = o.X + b.Pos.X, wz = o.Z + b.Pos.Z;
        foreach (var isl in _world.Isles)
        {
            /* PAS DE MARCHÉ AU DÉBARCADÈRE. On s'y amarre, on n'y vend rien :
               un enclos à cochons n'a ni entrepôt, ni courtier, ni cours des
               épices. Le panneau ne s'ouvre donc pas, et c'est la seule chose
               que le joueur remarquera vraiment en accostant là. */
            if (isl.Wild) continue;
            double dx = wx - isl.Port.Hx, dz = wz - isl.Port.Hz;
            if (dx * dx + dz * dz < 220 * 220) return isl;
        }
        return null;
    }

    /// <summary>À chaque tour du bandeau : le comptoir s'ouvre ou se ferme, les chiffres se relisent.</summary>
    /// <summary>Le comptoir ouvre à 5 h et ferme à 22 h. Les heures d'un port, pas d'une boutique.</summary>
    public const double Ouvre = 5, Ferme = 22;

    /// <summary>Ouvert maintenant ?</summary>
    bool MarketOpen => _sky.Core.DayTime >= Ouvre && _sky.Core.DayTime < Ferme;

    /// <summary>
    /// CE QUE LE MARCHÉ A PASSÉ FERMÉ, en secondes. L'horloge des cours est celle
    /// du jeu MOINS cela : les prix avancent de 5 h à 22 h et se figent la nuit.
    ///
    /// UN COMPTEUR ET NON UNE FORMULE, parce que le joueur règle lui-même le
    /// défilement du jour : l'heure n'est pas une fonction fixe du temps de jeu, et
    /// toute formule qui le supposerait mentirait dès qu'il touche au curseur.
    ///
    /// C'est le TEMPS FERMÉ qu'on accumule, et non le temps ouvert : ainsi une
    /// partie neuve part avec les deux horloges à la même valeur, et une
    /// sauvegarde d'avant ce jour reprend sans décalage.
    /// </summary>
    double _mkShut;

    /// <summary>L'heure des cours : celle du jeu, moins les nuits.</summary>
    double MarketTime => _sea.Core.Time - _mkShut;

    void MarketTick()
    {
        if (_mkPanel == null) return;
        var avant = _portHere;
        _portHere = _inTitle ? null : PortInFront();
        // on entre : la manœuvre de port a ses propres cris
        if (avant == null && _portHere != null) Shout("port", 0.2, 20);
        _mkPanel.Visible = _portHere != null && _hudOn && !_inTitle;
        /* À QUAI : LE LONG DU PONTON ET SANS ERRE.

           « Dans le rayon » ouvrait le comptoir à deux cents mètres, ce qui est la
           bonne distance pour VOIR un port et la mauvaise pour y charger. On mesure
           donc la distance au SEGMENT du ponton, pas à son musoir : ranger le long
           du tablier est ce qu'on fait vraiment, et le musoir n'en est qu'un bout.
           Le seuil laisse le poste d'amarrage dedans — Berth.At met le bordé à
           4,5 m du tablier — et rien au-delà d'une largeur de navire. */
        var bv = _ship.Physics.Body.Vel;
        double erre = Math.Sqrt(bv.X * bv.X + bv.Z * bv.Z);
        _alongside = erre <= 0.8 && MarketOpen && _portHere != null && Alongside(_portHere);
        if (_mkPanel.Visible)
        {
            /* ON DIT POURQUOI, ET AVEC L'ALLURE. Un bouton gris sans raison se lit
               comme une panne ; avec le chiffre, il se lit comme une manœuvre qui
               n'est pas finie — et l'on sait de combien il faut encore abattre. */
            /* ON DIT LAQUELLE DES TROIS RAISONS, dans l'ordre où le joueur peut y
               remédier : l'heure ne se force pas, l'erre se laisse tomber, la
               distance se rattrape à la barre. */
            if (_mkState != null)
                _mkState.Text = !MarketOpen
                        ? FormattableString.Invariant($"comptoir fermé — il ouvre à {Ouvre:F0} h")
                    : erre > 0.8 ? FormattableString.Invariant($"trop d'erre pour commercer — {erre * 1.94384:F1} nds")
                    : !_alongside ? "au large du ponton"
                    : "comptoir ouvert";
            /* LE RAYON DISPARAÎT, IL NE SE GRISE PAS. Des boutons gris qu'on ne peut
               pas atteindre depuis le large ne renseignent sur rien ; ce qui renseigne
               depuis le large, ce sont les COURS, et eux restent. */
            if (_mkSale != null) _mkSale.Visible = _alongside;
            if (_mkPowderRow != null) _mkPowderRow.Visible = _alongside;
            if (_mkPowder != null) _mkPowder.Visible = _alongside;
            if (_mkFar != null)
            {
                _mkFar.Visible = !_alongside;
                /* LA MEME RAISON QUE LE BANDEAU, dite en consigne. Deux phrases qui
                   se contredisent — « fermé » en haut, « rangez-vous » en bas —
                   valent moins qu une seule : le joueur croit avoir mal lu. */
                _mkFar.Text = !MarketOpen ? FormattableString.Invariant($"Le comptoir rouvre à {Ouvre:F0} h.")
                            : erre > 0.8 ? "Laissez tomber l erre pour charger."
                            : "Rangez-vous le long du ponton pour charger.";
            }
            foreach (var bt in _mkButtons) bt.Disabled = !_alongside;
        }
        if (_portHere == null) return;

        double t = MarketTime;
        var p = _ship.Physics;
        _mkPort!.Text = _portHere.Name;
        foreach (var (w, _, price, aboard) in _mkRows)
        {
            price.Text = FormattableString.Invariant(
                $"{_market.BuyPrice(w.Key, _portHere.Key, t):F0} / {_market.SellPrice(w.Key, _portHere.Key, t):F0}");
            double n = p.CargoOf(w.Key);
            aboard.Text = n < 0.05 ? "" : FormattableString.Invariant($"{n:F1} t");
        }
        TreasureRows(t);
        FishRows();
        YardRows();
        _mkPowder!.Text = FormattableString.Invariant($"Poudre, la charge    {Market.Poudre:F0}");
        // un bord sans pièces n'a pas de soute à remplir
        _mkPowder.Visible = _mkPowderRow!.Visible = p.PowderMax > 0;
        _mkHold!.Text = FormattableString.Invariant(
            $"cale : {p.CargoTonnes:F1} / {p.CargoCapacity:F0} t    soute : {p.Powder} / {p.PowderMax} charges");

        /* CE QU'ON SAIT D'AILLEURS : le prix ferme, et son âge à côté. Les plus
           fraîches d'abord — c'est l'ordre dans lequel on leur fait confiance. */
        /* UNE NOUVELLE EST UN ÉVÉNEMENT, PAS UNE FENÊTRE.

           Le noyau rend « le cours qu'il faisait là-bas il y a `lag` » — une
           fenêtre qui GLISSE avec l'heure. Le retard étant une constante (la
           distance divisée par la vitesse d'une nouvelle), l'âge affiché ne
           bougeait jamais pendant que le prix, lui, changeait sans cesse : « les
           prix changent sans que l'heure se rafraîchisse », signalé, et c'est
           exactement ce que le modèle produisait.

           Or un renseignement n'est pas une fenêtre : c'est un homme qui est
           arrivé, un jour, avec le chiffre qu'il avait en partant. Entre deux
           arrivées le chiffre ne bouge pas et VIEILLIT ; à l'arrivée suivante il
           saute et rajeunit. On quantifie donc l'heure sur la cadence des
           traversées — une nouvelle par voyage, ce que le retard mesure déjà — et
           l'on demande au noyau le cours de CETTE arrivée-là.

           HORS DU NOYAU, et c'est délibéré : Market est partagé avec la page et
           tenu par le banc de parité. Ce qui change ici est la façon de LIRE la
           nouvelle, pas la façon de la calculer — le banc reste vert, et la page
           garde ce qu'elle avait. */
        var news = new List<News>();
        foreach (var isl in _world!.Isles)
        {
            if (isl == _portHere) continue;
            var brut = _market.NewsOf(_portHere, isl, t);       // pour connaître le retard
            double pas = Math.Max(60, brut.Lag);                // une nouvelle par traversée
            double arrivee = Math.Floor(t / pas) * pas;         // la dernière qui soit arrivée
            var n = _market.NewsOf(_portHere, isl, arrivee);
            /* SON ÂGE MAINTENANT : ce que le chiffre avait déjà en arrivant, plus
               le temps passé depuis. Il grandit jusqu'à la prochaine arrivée. */
            news.Add(n with { Lag = t - arrivee + n.Lag });
        }
        news.Sort((a, b) => a.Lag.CompareTo(b.Lag));
        while (_mkNews!.GetChildCount() < news.Count * 3)
        {
            int col = _mkNews.GetChildCount() % 3;
            /* UNE LARGEUR MINIMALE AUX COLONNES QU ON COUPE, sans quoi elles
               s ecrasent a RIEN : coupe, un libelle n a plus de minimum, et une
               colonne en Fill prend justement son minimum. Le nom du port, lui,
               est en ExpandFill et se partage ce qui reste — c est lui qu on
               tronque, et c est le bon choix : un prix tronque ne veut rien dire,
               un nom de port se devine. */
            var l = new Label
            {
                HorizontalAlignment = col == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                SizeFlagsHorizontal = col == 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
                CustomMinimumSize = new Vector2(col == 1 ? 138 : col == 2 ? 88 : 0, 0)
            };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", col == 1 ? new Color(0.95f, 0.88f, 0.66f) : new Color(0.80f, 0.83f, 0.86f));
            _mkNews.AddChild(Ellipse(l));
        }
        for (int i = 0; i < news.Count; i++)
        {
            ((Label)_mkNews.GetChild(i * 3)).Text = news[i].Name;
            ((Label)_mkNews.GetChild(i * 3 + 1)).Text = FormattableString.Invariant(
                $"{news[i].GoodName.ToLowerInvariant()} {news[i].Sell:F0}");
            ((Label)_mkNews.GetChild(i * 3 + 2)).Text = Market.Age(news[i].Lag);
        }
    }

    void Buy(string good, double tonnes)
    {
        if (_portHere == null) return;
        var w = _market.Wares.ByKey(good);
        if (w == null) return;
        /* PAS PLUS QUE LA CALE : le comptoir chargeait tout ce qu'on payait, et un
           navire chargé sans regarder s'enfonçait jusqu'au pont. On charge ce qui
           reste de place, et l'on dit quand il n'y en a plus. */
        var ph = _ship.Physics;
        double room = Math.Max(0, ph.CargoCapacity - ph.CargoTonnes);
        if (room < 0.5) { Say(FormattableString.Invariant($"La cale est pleine — {ph.CargoCapacity:F0} t")); return; }
        tonnes = Math.Min(tonnes, Math.Floor(room));
        double prix = _market.BuyPrice(good, _portHere.Key, MarketTime) * tonnes;
        if (!_purse.Take(prix)) { Say("Bourse trop courte"); return; }
        _ship.Physics.LoadCargo(Config.NComp / 2, HoldFloor, 0, tonnes, good);
        Say(FormattableString.Invariant($"{tonnes:F0} t — {w.Name.ToLowerInvariant()} — {prix:F0} pièces"));
        MarketTick();
    }

    void Sell(string good, double tonnes)
    {
        if (_portHere == null) return;
        var w = _market.Wares.ByKey(good);
        if (w == null) return;
        double sorti = _ship.Physics.UnloadKind(good, tonnes);
        if (sorti < 0.05) { Say("Rien à vendre"); return; }
        double gain = Js.Round(_market.SellPrice(good, _portHere.Key, MarketTime) * sorti);
        _purse.Add(gain);
        Say(FormattableString.Invariant($"{sorti:F1} t de {w.Name.ToLowerInvariant()} — {gain:F0} pièces"));
        MarketTick();
    }

    /// <summary>
    /// La section des trésors : rebâtie seulement quand ce qu'on porte change —
    /// des boutons refaits à chaque image se déroberaient sous le pointeur —, et ses
    /// cours mis à jour à chaque passage.
    /// </summary>
    void TreasureRows(double t)
    {
        if (_mkTreasure == null || _portHere == null) return;
        var sig = string.Join(";", _treasureHold.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
        _mkTreasure.Visible = _alongside && sig.Length > 0;
        if (sig != _mkTreasureSig)
        {
            _mkTreasureSig = sig;
            foreach (var c in _mkTreasure.GetChildren()) c.QueueFree();
            _mkTreasureRows.Clear();
            var (ink, dim, gold) = _mkTreasureInk;
            if (sig.Length > 0)
            {
                var head = new Label { Text = "Trésors à bord" };
                head.AddThemeFontSizeOverride("font_size", 13);
                head.AddThemeColorOverride("font_color", gold);
                _mkTreasure.AddChild(head);
            }
            foreach (var (kind, n) in _treasureHold.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key))
            {
                var k = _tresors.ByKey(kind);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                // le nombre à part : « 2 pièces d'orfèvrerie » ne tenait pas dans la colonne
                var count = new Label { Text = n.ToString(), HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(26, 0) };
                count.AddThemeFontSizeOverride("font_size", 14);
                count.AddThemeColorOverride("font_color", dim);
                row.AddChild(count);
                var name = new Label { Text = k?.Name ?? kind, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = k?.Note ?? "" };
                name.AddThemeFontSizeOverride("font_size", 14);
                name.AddThemeColorOverride("font_color", ink);
                row.AddChild(Ellipse(name));
                var price = new Label { HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(96, 0) };
                price.AddThemeFontSizeOverride("font_size", 14);
                price.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.66f));
                row.AddChild(price);
                var one = new Button { Text = "Vendre 1", FocusMode = Control.FocusModeEnum.None };
                one.Pressed += () => SellTreasure(kind, 1);
                row.AddChild(one);
                var all = new Button { Text = "Tout", FocusMode = Control.FocusModeEnum.None };
                all.Pressed += () => SellTreasure(kind, int.MaxValue);
                row.AddChild(all);
                _mkTreasure.AddChild(row);
                _mkTreasureRows.Add((kind, price));
            }
        }
        foreach (var (kind, price) in _mkTreasureRows)
            price.Text = FormattableString.Invariant($"{_tresors.SellPrice(kind, _portHere.Key, t, _market.Palier):F0} p. l'unité");
    }

    void SellTreasure(string kind, int n)
    {
        if (_portHere == null || !_alongside) return;
        int have = _treasureHold.GetValueOrDefault(kind);
        n = Math.Min(n, have);
        if (n <= 0) { Say("Rien à vendre"); return; }
        double gain = _tresors.SellPrice(kind, _portHere.Key, MarketTime, _market.Palier) * n;
        _purse.Add(gain);
        _treasureHold[kind] = have - n;
        Say(FormattableString.Invariant($"{_tresors.Say(kind, n)} — {gain:F0} pièces ({gain / Market.SousParEcu:F0} écus)"));
        MarketTick();
    }

    void BuyPowder(int n)
    {
        if (_portHere == null) return;
        var p = _ship.Physics;
        n = Math.Min(n, p.PowderMax - p.Powder);
        if (n <= 0) { Say("Soute pleine"); return; }
        double prix = Market.Poudre * n;
        if (!_purse.Take(prix)) { Say("Bourse trop courte"); return; }
        p.Powder += n;
        Say(FormattableString.Invariant($"{n} coups embarqués — {prix:F0} pièces"));
        MarketTick();
    }

    /// <summary>La ligne de la bourse, pour les instruments : les écus, les pièces, ce que porte la cale.</summary>
    string PurseLine() =>
        FormattableString.Invariant(
            $"bourse     {_purse.Ecus} écus {_purse.Pieces} pièces    cale {_ship.Physics.CargoTonnes:F1} t\n");

    /// <summary>
    /// ABORDÉS : le pirate emporte la moitié de la bourse et TOUT CE QUI SE VEND.
    ///
    /// La page ne lui laissait prendre que les épices, du temps où c'était la
    /// seule denrée. Un pirate qui laisserait l'indigo pour n'emporter que le
    /// poivre serait un pirate de comédie : il prend ce qui a un cours, et
    /// laisse le reste — les vivres d'un fret sous contrat ne valent rien pour
    /// lui, et c'est justement ce qui distingue une cargaison d'un chargement.
    /// </summary>
    string Pillage()
    {
        long sous = _purse.Sous / 2;
        _purse.Take(sous);
        double tonnes = 0;
        foreach (var w in _market.Wares.List) tonnes += _ship.Physics.UnloadKind(w.Key, 1e6);
        return $"Abordés ! Le pirate emporte {sous / Market.SousParEcu} écus"
             + (tonnes > 0.05 ? FormattableString.Invariant($" et {tonnes:F1} t de cargaison") : "");
    }
}
