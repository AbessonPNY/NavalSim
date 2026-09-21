using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE PLAN D'ARRIMAGE — le panneau « Cale · arrimage » de la page.
///
/// Trois hauteurs et cinq cales, assez pour que l'arrimage soit une vraie
/// décision sans devenir un tableur. Les cales sont les compartiments que
/// l'envahissement emploie déjà : une cale est un vrai morceau de coque, avec un
/// volume, une largeur et un creux mesurés, pas une boîte dessinée sur un plan.
/// Et tout ce qu'on y pose passe par <see cref="ShipPhysics.LoadCargo"/> : du
/// POIDS, qui enfonce la coque, déplace son centre de gravité, la raidit au fond
/// et la rend molle sur le pont. Rien n'a eu à être écrit pour ça.
///
/// Un clic charge du lest, un clic droit en décharge — le même geste côté quai,
/// sans second mode à retenir. La grille montre TOUT ce que porte chaque case,
/// lest et épices, puisque c'est le poids qui compte pour la coque.
/// </summary>
public partial class ShipDemo : Node3D
{
    /* LES NIVEAUX DE LA PAGE, une seule définition : HoldFloor (le fond, où le
       comptoir range ses épices) en est le dernier, et une case que la grille ne
       connaîtrait pas ne pourrait jamais montrer ce qu'on y a mis. */
    static readonly (string Name, double Y)[] Levels =
    {
        ("Pont", 0.85),     // sur le pont : n'achète rien, coûte de la stabilité
        ("Entre", 0.50),
        ("Fond", HoldFloor) // au fond de la cale : du lest, et elle se raidit
    };
    static readonly string[] Holds = { "Ar", "2", "3", "4", "Av" };
    static readonly double[] Steps = { 1, 5, 10, 25, 50, 100 };

    PanelContainer? _stowPanel;
    readonly List<(Button B, int Hold, double Level, bool Deck)> _stowCells = new();
    Label? _stowStep;
    readonly List<Button> _stowSides = new();
    double _stowSide;
    int _stepIdx = 2;

    /// <summary>La touche du panneau : l'emplacement Z d'un QWERTY, lu dans la langue du clavier.</summary>
    static string StowKeyName =>
        OS.GetKeycodeString(DisplayServer.KeyboardGetKeycodeFromPhysical(Key.Z));

    void BuildStow(CanvasLayer layer)
    {
        /* EN HAUT AU MILIEU : les instruments tiennent la gauche, le soleil et le
           comptoir la droite, l'encart le bas — le plan doit pouvoir rester
           ouvert à quai pendant qu'on achète, puisque c'est là qu'on arrime. */
        _stowPanel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -190, OffsetRight = 190, OffsetTop = 14,
            Visible = false
        };
        _stowPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.80f),
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 10
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        _stowPanel.AddChild(box);
        layer.AddChild(_stowPanel);

        var title = new Label { Text = "Cale · arrimage" };
        title.AddThemeFontSizeOverride("font_size", 15);
        title.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        box.AddChild(title);

        var grid = new GridContainer { Columns = Holds.Length + 1 };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        box.AddChild(grid);
        Label Hd(string t)
        {
            var l = new Label { Text = t, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(44, 0) };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", new Color(0.66f, 0.70f, 0.74f));
            return l;
        }
        grid.AddChild(Hd(""));
        foreach (var h in Holds) grid.AddChild(Hd(h));
        foreach (var (name, y) in Levels)
        {
            grid.AddChild(Hd(name));
            for (int i = 0; i < Holds.Length; i++)
            {
                int hold = i;
                double level = y;
                var b = new Button
                {
                    Text = "·", CustomMinimumSize = new Vector2(52, 26), FocusMode = Control.FocusModeEnum.None,
                    TooltipText = $"{name} · cale {Holds[i]}"
                };
                /* LE CLIC DROIT DÉCHARGE : un Button ne sait pas dire lequel l'a
                   pressé, donc on lit l'événement nous-mêmes, sur le bouton —
                   c'est un Control, il a la souris avant tout le reste. */
                b.GuiInput += e =>
                {
                    if (e is not InputEventMouseButton mb || !mb.Pressed) return;
                    if (mb.ButtonIndex == MouseButton.Left) Stow(hold, level, Steps[_stepIdx]);
                    else if (mb.ButtonIndex == MouseButton.Right) Stow(hold, level, -Steps[_stepIdx]);
                    else return;
                    b.AcceptEvent();
                };
                grid.AddChild(b);
                _stowCells.Add((b, hold, level, y > 0.7));
            }
        }

        // le bord : le lest d'un bord fait gîter, et c'est parfois ce qu'on veut
        var sides = new HBoxContainer();
        sides.AddThemeConstantOverride("separation", 4);
        foreach (var (label, s) in new[] { ("Bâbord", -1.0), ("Centre", 0.0), ("Tribord", 1.0) })
        {
            double side = s;
            var b = new Button
            {
                Text = label, ToggleMode = true, ButtonPressed = side == 0,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None
            };
            b.Pressed += () =>
            {
                _stowSide = side;
                foreach (var o in _stowSides) o.SetPressedNoSignal(o == b);
            };
            _stowSides.Add(b);
            sides.AddChild(b);
        }
        box.AddChild(sides);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 4);
        Button Small(string t, Action act)
        {
            var b = new Button { Text = t, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(30, 0) };
            b.Pressed += act;
            foot.AddChild(b);
            return b;
        }
        Small("−", () => { _stepIdx = Math.Max(0, _stepIdx - 1); ShowStep(); });
        _stowStep = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(60, 0) };
        foot.AddChild(_stowStep);
        Small("+", () => { _stepIdx = Math.Min(Steps.Length - 1, _stepIdx + 1); ShowStep(); });
        var clear = Small("Vider", ClearHold);
        clear.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        box.AddChild(foot);

        var hint = new Label { Text = $"Clic : charger · clic droit : décharger · {StowKeyName} masquer" };
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.58f, 0.62f, 0.66f));
        box.AddChild(hint);
        ShowStep();
    }

    void ShowStep() { if (_stowStep != null) _stowStep.Text = $"{Steps[_stepIdx]:0} t"; }

    void ToggleStow()
    {
        if (_stowPanel == null) return;
        _stowPanel.Visible = !_stowPanel.Visible;
        StowTick();
    }

    /// <summary>Du lest dans une case : positif on charge, négatif on décharge.</summary>
    void Stow(int hold, double level, double tonnes)
    {
        _ship.Physics.LoadCargo(hold, level, _stowSide, tonnes);
        StowTick();
    }

    /* VIDER, c'est TOUT jeter — les épices comprises, comme dans la page : c'est
       la cale qui fait foi, et une cargaison passée par-dessus bord est perdue.
       Au moins on le dit, pour qu'une main distraite sache ce qu'elle a fait. */
    void ClearHold()
    {
        double spice = _ship.Physics.CargoOf("epice");
        _ship.Physics.ClearCargo();
        StowTick();
        Say(spice > 0.05
            ? FormattableString.Invariant($"Cale vidée — {spice:F1} t d'épices par-dessus bord")
            : "Cale vidée");
    }

    /// <summary>La grille relue : ce que chaque case porte, tous bords et toutes marchandises confondus.</summary>
    void StowTick()
    {
        if (_stowPanel == null || !_stowPanel.Visible) return;
        if (!_info.Visible || _inTitle) { _stowPanel.Visible = false; return; }
        var cargo = _ship.Physics.Cargo;
        foreach (var (b, hold, level, deck) in _stowCells)
        {
            double t = 0;
            foreach (var p in cargo) if (p.Hold == hold && p.Level == level) t += p.Kg / 1000;
            b.Text = t > 0 ? (t < 10 ? FormattableString.Invariant($"{t:F1}") : FormattableString.Invariant($"{t:F0}")) : "·";
            // au fond la charge est un atout, sur le pont un danger : la couleur le dit
            b.Modulate = t <= 0 ? Colors.White
                       : deck ? new Color(1.0f, 0.72f, 0.45f)
                       : new Color(0.62f, 0.95f, 0.88f);
        }
    }
}
