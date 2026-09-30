using Godot;
using System;
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
        L("comptoir ouvert", 12, dim);

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
        foreach (var w in _market.Wares.List) WareRow(w, ink, dim);

        _mkPowder = L("", 15, ink);
        _mkPowderRow = Buttons(box, ("Embarquer 25 coups", () => BuyPowder(25)), ("Faire le plein", () => BuyPowder(int.MaxValue)));
        _mkHold = L("", 13, dim);
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

    static Control Buttons(VBoxContainer box, params (string Text, Action Do)[] bs)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        foreach (var (text, act) in bs)
        {
            var b = new Button { Text = text, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
            b.Pressed += act;
            row.AddChild(b);
        }
        box.AddChild(row);
        return row;
    }

    /// <summary>
    /// ON NE NÉGOCIE QU'À QUAI, et « à quai » se mesure plutôt que se décrète :
    /// près du musoir et sans erre. Un navire qui passe au large d'un port ne
    /// commerce pas avec lui, et un navire lancé à six nœuds ne débarque rien.
    /// </summary>
    Isle? PortInFront()
    {
        if (_world == null) return null;
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        if (Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z) > 0.8) return null;
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
    void MarketTick()
    {
        if (_mkPanel == null) return;
        var avant = _portHere;
        _portHere = _inTitle ? null : PortInFront();
        // on entre : la manœuvre de port a ses propres cris
        if (avant == null && _portHere != null) Shout("port", 0.2, 20);
        _mkPanel.Visible = _portHere != null && _hudOn && !_inTitle;
        if (_portHere == null) return;

        double t = _sea.Core.Time;
        var p = _ship.Physics;
        _mkPort!.Text = _portHere.Name;
        foreach (var (w, _, price, aboard) in _mkRows)
        {
            price.Text = FormattableString.Invariant(
                $"{_market.BuyPrice(w.Key, _portHere.Key, t):F0} / {_market.SellPrice(w.Key, _portHere.Key, t):F0}");
            double n = p.CargoOf(w.Key);
            aboard.Text = n < 0.05 ? "" : FormattableString.Invariant($"{n:F1} t");
        }
        _mkPowder!.Text = FormattableString.Invariant($"Poudre, la charge    {Market.Poudre:F0}");
        // un bord sans pièces n'a pas de soute à remplir
        _mkPowder.Visible = _mkPowderRow!.Visible = p.PowderMax > 0;
        _mkHold!.Text = FormattableString.Invariant(
            $"cale : {p.CargoTonnes:F1} t    soute : {p.Powder} / {p.PowderMax} charges");

        /* CE QU'ON SAIT D'AILLEURS : le prix ferme, et son âge à côté. Les plus
           fraîches d'abord — c'est l'ordre dans lequel on leur fait confiance. */
        var news = new List<News>();
        foreach (var isl in _world!.Isles) if (isl != _portHere) news.Add(_market.NewsOf(_portHere, isl, t));
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
        double prix = _market.BuyPrice(good, _portHere.Key, _sea.Core.Time) * tonnes;
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
        double gain = Js.Round(_market.SellPrice(good, _portHere.Key, _sea.Core.Time) * sorti);
        _purse.Add(gain);
        Say(FormattableString.Invariant($"{sorti:F1} t de {w.Name.ToLowerInvariant()} — {gain:F0} pièces"));
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
