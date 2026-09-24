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
    Label? _mkPort, _mkSpice, _mkPowder, _mkHold;
    Control? _mkPowderRow;
    GridContainer? _mkNews;

    void BuildMarket(CanvasLayer layer)
    {
        if (_world != null) _market.Tune(_world.LongestLeg);

        _mkPanel = new PanelContainer
        {
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -330, OffsetRight = -14, OffsetTop = 130,
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
        _mkSpice = L("", 15, ink);
        Buttons(box, ("Acheter 10 t", () => BuySpice(10)), ("Vendre 10 t", () => SellSpice(10)));
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
        _mkSpice!.Text = FormattableString.Invariant(
            $"Épices, la tonne     {_market.BuyPrice(_portHere.Key, t):F0} / {_market.SellPrice(_portHere.Key, t):F0}");
        _mkPowder!.Text = FormattableString.Invariant($"Poudre, la charge    {Market.Poudre:F0}");
        // un bord sans pièces n'a pas de soute à remplir
        _mkPowder.Visible = _mkPowderRow!.Visible = p.PowderMax > 0;
        _mkHold!.Text = $"à bord : {p.CargoOf("epice"):F1} t d'épices, {p.Powder} / {p.PowderMax} charges";

        /* CE QU'ON SAIT D'AILLEURS : le prix ferme, et son âge à côté. Les plus
           fraîches d'abord — c'est l'ordre dans lequel on leur fait confiance. */
        var news = new List<News>();
        foreach (var isl in _world!.Isles) if (isl != _portHere) news.Add(_market.NewsOf(_portHere, isl, t));
        news.Sort((a, b) => a.Lag.CompareTo(b.Lag));
        while (_mkNews!.GetChildCount() < news.Count * 3)
        {
            int col = _mkNews.GetChildCount() % 3;
            var l = new Label
            {
                HorizontalAlignment = col == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                SizeFlagsHorizontal = col == 0 ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill
            };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", col == 1 ? new Color(0.95f, 0.88f, 0.66f) : new Color(0.80f, 0.83f, 0.86f));
            _mkNews.AddChild(l);
        }
        for (int i = 0; i < news.Count; i++)
        {
            ((Label)_mkNews.GetChild(i * 3)).Text = news[i].Name;
            ((Label)_mkNews.GetChild(i * 3 + 1)).Text = FormattableString.Invariant($"{news[i].Sell:F0}");
            ((Label)_mkNews.GetChild(i * 3 + 2)).Text = Market.Age(news[i].Lag);
        }
    }

    void BuySpice(double tonnes)
    {
        if (_portHere == null) return;
        double prix = _market.BuyPrice(_portHere.Key, _sea.Core.Time) * tonnes;
        if (!_purse.Take(prix)) { Say("Bourse trop courte"); return; }
        _ship.Physics.LoadCargo(Config.NComp / 2, HoldFloor, 0, tonnes, "epice");
        Say(FormattableString.Invariant($"{tonnes:F0} t d'épices à {prix:F0} pièces"));
        MarketTick();
    }

    void SellSpice(double tonnes)
    {
        if (_portHere == null) return;
        double sorti = _ship.Physics.UnloadKind("epice", tonnes);
        if (sorti < 0.05) { Say("Rien à vendre"); return; }
        double gain = Js.Round(_market.SellPrice(_portHere.Key, _sea.Core.Time) * sorti);
        _purse.Add(gain);
        Say(FormattableString.Invariant($"{sorti:F1} t vendues — {gain:F0} pièces"));
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
        $"bourse     {_purse.Ecus} écus {_purse.Pieces} pièces    épices {_ship.Physics.CargoOf("epice"):F1} t\n";

    /// <summary>
    /// ABORDÉS : le pirate emporte la moitié de la bourse et toutes les épices —
    /// ce que la page lui fait prendre. Le reste de la cale n'est pas à lui.
    /// </summary>
    string Pillage()
    {
        long sous = _purse.Sous / 2;
        _purse.Take(sous);
        double tonnes = _ship.Physics.UnloadKind("epice", 1e6);
        return $"Abordés ! Le pirate emporte {sous / Market.SousParEcu} écus"
             + (tonnes > 0.05 ? FormattableString.Invariant($" et {tonnes:F1} t d'épices") : "");
    }
}
