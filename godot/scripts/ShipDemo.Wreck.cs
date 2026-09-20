using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE NAUFRAGE : l'air qui remonte, et ce qui reste à flot. Le noyau tient les
/// deux mécaniques (WreckAir) ; ici on leur prête la mer, l'embrun, le champ
/// d'écume et les mots.
/// </summary>
public partial class ShipDemo
{
    readonly WreckAir _wreckAir = new();
    readonly List<WreckHull> _wrecks = new();
    FlotsamNode _flotsam = null!;
    BubbleNode _bubbles = null!;
    CoinNode _coins = null!;

    void WireWreck()
    {
        // la gerbe basse que chaque poche soulève en crevant, et le bouillon qui reste
        _wreckAir.OnBurst = (at, water, speed, jet) => _spray.Pool.Burst(at, water, speed, jet);
        // ce qu'on voit du trajet : la poche elle-même, qui monte en chapelet
        _wreckAir.OnSlug = (at, v, rise, travel) =>
            _bubbles.Slug(new Vector3((float)at.X, (float)at.Y, (float)at.Z), v, rise, travel);
        _flotsam.BottleOneIn = _bottleOneIn;
        // ce qui crève la surface en remontant jette son peu d'eau, par la même réserve
        _flotsam.OnBreak = (at, water, speed) => _spray.Pool.Burst(at, water, speed);
        /* CE QU'ELLE EMPORTE. Une cale qui s'ouvre lâche sa bourse : les pièces
           descendent en voltigeant et l'eau les avale. Le compte suit sa taille —
           un galion emporte plus qu'une chaloupe. */
        _flotsam.OnWreck = s =>
        {
            var b = s.Physics.Body;
            _coins.Spill(new Vector3((float)b.Pos.X, (float)b.Pos.Y, (float)b.Pos.Z),
                s.Spec.L, s.Spec.B, (int)Math.Clamp(s.Spec.L * 10, 120, 500));
        };
        // ce qu'elle contient : une page de journal, ou une carte (ShipDemo.Bottle.cs)
        _flotsam.OnBottle = OpenBottle;
        _flotsam.OnCargo = ClaimCargo;
        _flotsam.World = _world;
        _wreckAir.OnLastBreath = (at, v) =>
        {
            var b = _ship.Physics.Body;
            if ((at - b.Pos).Length < 3000)
                Say(FormattableString.Invariant($"Elle disparaît dans un grand bouillon — {v:F0} m³ d'air d'un coup."));
        };
    }

    /* ------------------------------------------------------------------ */
    /*  ELLE REPOSE PAR LE FOND                                            */
    /* ------------------------------------------------------------------ */

    bool _lost;
    PanelContainer? _lostBox;

    /// <summary>La hauteur d'équilibre trouvée au lancement : celle où on la remet.</summary>
    double _eqY;

    void BuildLost(CanvasLayer layer)
    {
        _lostBox = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.32f, AnchorBottom = 0.32f,
            OffsetLeft = -260, OffsetRight = 260, OffsetTop = -52, OffsetBottom = 52,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _lostBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.03f, 0.03f, 0.90f),
            BorderColor = new Color(0.55f, 0.18f, 0.14f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 14, ContentMarginBottom = 14
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        var title = new Label { Text = "Votre navire a sombré !", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.78f));
        var how = new Label
        {
            Text = "Elle repose par le fond.   R pour la renflouer.",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        how.AddThemeFontSizeOverride("font_size", 15);
        how.AddThemeColorOverride("font_color", new Color(0.88f, 0.84f, 0.82f));
        box.AddChild(title); box.AddChild(how);
        _lostBox.AddChild(box);
        layer.AddChild(_lostBox);
    }

    /// <summary>
    /// PERDUE QUAND IL N'EN DÉPASSE PLUS RIEN — la règle de la page : tant qu'une
    /// pomme de mât perce encore la surface, on ne le lui dit pas. La page exige
    /// dix mètres d'eau au-dessus du point le plus haut ; deux suffisent ici, et
    /// il le FAUT : dans une rade de onze mètres une coque posée sur le sable n'a
    /// jamais dix mètres sur la tête, et le bandeau ne venait pas là où l'on
    /// s'échoue le plus souvent. Deux mètres, c'est déjà « on ne la voit plus »,
    /// et c'est au-dessus du creux d'une houle ordinaire.
    ///
    /// Elle reste VISIBLE, contrairement à la page qui l'escamote : ici l'eau se
    /// traverse du regard, et voir sa propre coque posée sur le sable est ce que
    /// le naufrage a de plus juste.
    /// </summary>
    void LostTick()
    {
        if (_lostBox == null) return;
        if (!_ship.Physics.Foundered) { if (_lost) Found(); return; }
        if (_lost) return;
        var b = _ship.Physics.Body;
        var box = _ship.LocalBounds();
        double top = b.Pos.Y + box.End.Y;
        double sea = _sea.Core.Sample(b.Pos.X, b.Pos.Z, _t);
        if (top - sea > -2) return;
        _lost = true;
        _lostBox.Visible = true;
    }

    void Found()
    {
        _lost = false;
        if (_lostBox != null) _lostBox.Visible = false;
    }

    /// <summary>
    /// RÉPARER ET RENFLOUER, la touche R de la page : les trous bouchés, l'eau
    /// pompée, la mâture replantée — et si elle était au fond, elle retrouve sa
    /// flottaison sur place, sans être renvoyée au port.
    /// </summary>
    void Salvage()
    {
        bool sunk = _lost || _ship.Physics.Foundered;
        _ship.Physics.Salvage();
        _ship.RestoreMasts();
        if (!sunk) { Say("Radoub : la mâture est remise en état."); return; }
        var b = _ship.Physics.Body;
        b.Pos = new Vec3d(b.Pos.X, _eqY, b.Pos.Z);
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        b.Quat = Quatd.Identity;
        _ship.SyncTransform();
        Found();
        Say("Renflouée : les pompes ont eu raison de l'eau, la mâture est replantée.");
    }

    /// <summary>Une image de naufrage : l'air de chaque coque, les bouillons dans l'écume, les débris.</summary>
    void WreckTick(double dt)
    {
        _wrecks.Clear();
        _wrecks.Add(new WreckHull(_ship.Physics, _ship.HullShell()));
        foreach (var s in _others) _wrecks.Add(new WreckHull(s.Physics, s.HullShell()));
        _wreckAir.Update(dt, _wrecks, _sea.Core, _t);
        _foam.SetBoils(_wreckAir.Boils);
        _flotsam.Step(dt, _sea.Core, _allShipsForFlotsam(), _ship, _cam);
        LostTick();
        _bubbles.Step(dt, _sea.Core, _t);
        // l'or ne s'allume pas tout seul : il lui faut la course du soleil et sa couleur
        _sky.PushTo(_coins.Material);
        _coins.Step(dt);
    }

    readonly List<ShipNode> _flotsamFleet = new();
    IReadOnlyList<ShipNode> _allShipsForFlotsam()
    {
        _flotsamFleet.Clear();
        _flotsamFleet.Add(_ship);
        _flotsamFleet.AddRange(_others);
        return _flotsamFleet;
    }
}
