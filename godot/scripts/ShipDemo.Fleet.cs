using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE PANNEAU FLOTTE (⇧N) — celui de la page : les coques à flot, leur vitesse,
/// une croix pour en retirer une, et de quoi en mettre une autre à l'eau autour
/// de soi. Tout le travail est déjà fait ailleurs : SpawnFleet la met à l'eau
/// entière (solveur, profil, gerbes, humeur de pirate si elle arbore la tête de
/// mort), RemoveShip la retire et réécrit les profils. Ici, on la POSE — à côté
/// de vous et non autour de l'origine — et on montre.
/// </summary>
public partial class ShipDemo
{
    PanelContainer? _fleetPanel;
    VBoxContainer? _fleetRows;
    OptionButton? _fleetPick;
    Button? _fleetAdd;
    Label? _fleetNote;
    readonly List<(ShipNode Ship, Label Speed)> _fleetShown = new();

    void BuildFleetPanel(CanvasLayer layer)
    {
        _fleetPanel = new PanelContainer
        {
            AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -330, OffsetRight = -14, OffsetTop = 130,
            Visible = false
        };
        _fleetPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
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
        _fleetPanel.AddChild(box);
        layer.AddChild(_fleetPanel);

        var title = new Label { Text = "Flotte" };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        box.AddChild(title);

        _fleetRows = new VBoxContainer();
        _fleetRows.AddThemeConstantOverride("separation", 2);
        box.AddChild(_fleetRows);

        // le choix : chaque fiche du dossier, par son nom — remplie à la première ouverture, les fiches sont lues après ce panneau
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        _fleetPick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        row.AddChild(_fleetPick);
        _fleetAdd = new Button { Text = "Mettre à l'eau", FocusMode = Control.FocusModeEnum.None };
        _fleetAdd.Pressed += () => { if (_fleetPick.Selected >= 0) LaunchBeside(_fleetPick.GetItemId(_fleetPick.Selected)); };
        row.AddChild(_fleetAdd);
        box.AddChild(row);

        _fleetNote = new Label();
        _fleetNote.AddThemeFontSizeOverride("font_size", 12);
        _fleetNote.AddThemeColorOverride("font_color", new Color(0.66f, 0.70f, 0.74f));
        box.AddChild(_fleetNote);
    }

    void ToggleFleetPanel()
    {
        if (_fleetPanel == null) return;
        _fleetPanel.Visible = !_fleetPanel.Visible;
        if (_fleetPick != null && _fleetPick.ItemCount == 0)
            for (int i = 0; i < _paths.Count; i++)
            {
                var spec = ShipLibrary.Load(_paths[i]);
                _fleetPick.AddItem(spec?.Name ?? System.IO.Path.GetFileNameWithoutExtension(_paths[i]), i);
            }
        if (_fleetPanel.Visible) DrawFleet();
    }

    /* À CÔTÉ DE VOUS, bien dégagée, et en éventail pour qu'une troisième ne
       tombe pas sur la deuxième — comme la page : à 2,4 fois les deux longueurs
       en jeu, pour qu'une frégate ait la place d'une frégate, et au même cap que
       le navire commandé. */
    void LaunchBeside(int specIndex)
    {
        if (_fleet.Count >= Config.MaxShips) { DrawFleet(); return; }
        int before = _others.Count;
        SpawnFleet(1, specIndex);
        if (_others.Count == before) return;
        var s = _others[^1];
        var b = s.Physics.Body;
        var me = _ship.Physics.Body;
        double ang = _fleet.Count * 2.2;
        // un cercle de plus toutes les six coques : seize sur un seul cercle se touchaient
        double reach = 2.4 * (_ship.Spec.L + s.Spec.L) * (1 + (_fleet.Count - 2) / 6);
        /* À LA HAUTEUR DE LA MER LÀ OÙ ON LA POSE : l'équilibre est pris sur une mer
           aplatie (Settle), et la houle à cent mètres n'est pas celle de l'origine.
           Une frégate encaisse le mètre de trop ; une caisse vide de 0,2 t, qui
           déplace dix fois son poids noyée, partait à dix-sept mètres en l'air. */
        /* EN EAU LIBRE : au port, l'éventail tombait à terre — une caisse posée
           quarante-six mètres dans les terres, sur un sol à neuf mètres, en était
           éjectée. On tourne l'éventail par huitièmes, puis on s'éloigne, jusqu'à
           trouver cinq mètres d'eau sous elle (relevé : le cotre à 16,5 m de fond). */
        double x = 0, z = 0;
        var o = _sea.Core.Origin;
        bool wet = false;
        for (int ring = 0; ring < 3 && !wet; ring++)
            for (int k = 0; k < 8 && !wet; k++)
            {
                double a = ang + k * Math.PI / 4, r = reach * (1 + ring);
                x = me.Pos.X + Math.Sin(a) * r; z = me.Pos.Z + Math.Cos(a) * r;
                wet = _world == null || _world.HeightAt(o.X + x, o.Z + z) < -5;
            }
        if (!wet) { RemoveShip(s); Say("Pas assez d'eau autour de vous pour la mettre à flot"); DrawFleet(); return; }
        double now = _stepT0 + _stepSub * _stepDt;
        b.Pos = new Vec3d(x, b.Pos.Y + _sea.Core.Sample(x, z, now), z);
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        b.Quat = me.Quat;
        s.SyncTransform();
        Say(s.Spec.Name + " est à l'eau");
        DrawFleet();
    }

    /* La liste refaite quand la flotte change, et seulement alors : la refaire
       à chaque rafraîchissement jetterait les boutons sous le pointeur (la page
       l'a appris). Entre-temps, seules les vitesses bougent. */
    void DrawFleet()
    {
        if (_fleetRows == null || _fleetNote == null || _fleetAdd == null) return;
        foreach (var c in _fleetRows.GetChildren()) { _fleetRows.RemoveChild(c); c.QueueFree(); }
        _fleetShown.Clear();
        FleetRow(_ship, true);
        // les spectres ne se commandent pas
        foreach (var o in _others) if (!o.IsGhost) FleetRow(o, false);
        bool full = _fleet.Count >= Config.MaxShips;
        _fleetAdd.Disabled = full;
        _fleetNote.Text = full
            ? $"Flotte au complet ({Config.MaxShips} coques)."
            : $"{_fleet.Count} / {Config.MaxShips} coques à l'eau.";
    }

    void FleetRow(ShipNode s, bool helm)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var name = new Label
        {
            Text = (helm ? "⚓ " : "") + s.Spec.Name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipText = true
        };
        name.AddThemeColorOverride("font_color", helm ? new Color(0.92f, 0.78f, 0.36f) : new Color(0.92f, 0.94f, 0.96f));
        row.AddChild(name);
        var sp = new Label { CustomMinimumSize = new Vector2(62, 0), HorizontalAlignment = HorizontalAlignment.Right };
        sp.AddThemeColorOverride("font_color", new Color(0.66f, 0.70f, 0.74f));
        row.AddChild(sp);
        if (!helm)
        {
            var rm = new Button { Text = "✕", TooltipText = "Retirer de la flotte", FocusMode = Control.FocusModeEnum.None };
            rm.Pressed += () => { RemoveShip(s); DrawFleet(); };
            row.AddChild(rm);
        }
        _fleetRows!.AddChild(row);
        _fleetShown.Add((s, sp));
        FleetSpeed(s, sp);
    }

    /* Une épave n'a pas de vitesse, elle a un sort : « 0 nds » sur une coque qui
       descend se lirait comme un navire en panne qu'on peut encore mener. */
    static void FleetSpeed(ShipNode s, Label sp)
    {
        var v = s.Physics.Body.Vel;
        sp.Text = s.Physics.Foundered ? "coulé" : $"{Math.Round(Math.Sqrt(v.X * v.X + v.Z * v.Z) * 1.94384)} nds";
    }

    void FleetTick()
    {
        if (_fleetPanel == null || !_fleetPanel.Visible) return;
        // une coque venue ou partie d'ailleurs (pirate, rencontre, soute sautée) : la liste est refaite
        int shown = 1;
        foreach (var o in _others) if (!o.IsGhost) shown++;
        bool stale = shown != _fleetShown.Count;
        foreach (var (s, _) in _fleetShown) if (!IsInstanceValid(s)) stale = true;
        if (stale) { DrawFleet(); return; }
        foreach (var (s, sp) in _fleetShown) FleetSpeed(s, sp);
    }
}
