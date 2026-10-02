using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE MODE CRÉATION — l'éditeur de niveau, étape une : prendre à la souris ce
/// que le monde a posé de lui-même (maisons, pâtés du centre, modèles posés,
/// rochers et cocotiers des semis), le déplacer, le tourner, le grandir, le
/// lever, le retirer, et enregistrer.
///
/// RIEN N'EST ÉCRIT DANS LES FICHES : les retouches vont dans
/// world/retouches/&lt;région&gt;.json, appliquées par-dessus le placement
/// automatique au moment où chaque objet se pose (<see cref="EditRegistry"/>).
/// Retirer une retouche rend l'objet aux règles.
///
/// La caméra y est LIBRE et ne quitte pas le navire des yeux pour autant : la
/// mer, le temps et le bord continuent — on retouche un monde qui vit. La terre
/// et les villes se chargent autour de l'œil, et non plus du navire, le temps du
/// mode ; tout le reste (la carte, les quêtes, les rencontres) reste au navire.
/// </summary>
public partial class ShipDemo
{
    EditRegistry? _editReg;
    bool _editing;
    Editable? _sel;
    Vec3d _edEye;                    // l'œil, en mètres VRAIS
    double _edYaw, _edPitch;
    Vector3? _edPrevEye, _edPrevLook;
    bool _edLooking, _edDragging;
    double _edGrabX, _edGrabZ;
    readonly Stack<(Editable E, (double, double, double, double, double, bool) S)> _edUndo = new();
    CanvasLayer? _edLayer;
    Label? _edLabel;
    MeshInstance3D? _edRing;

    // --- l'essai : --creation <mètres> ---
    double _edTest;
    int _edPasteTest;

    void EditTest()
    {
        double push = _edTest; _edTest = 0;
        if (!_editing) ToggleEdit();
        var mid = GetViewport().GetVisibleRect().Size * 0.5f;
        _sel = Pick(mid);
        if (_sel == null) { GD.Print("[éditeur] rien au milieu de l'écran"); return; }
        GD.Print(FormattableString.Invariant($"[éditeur] pris {_sel.Id} ({_sel.Label}) en ({_sel.X:F1}, {_sel.Z:F1}), pied {_sel.GroundY:F2}"));
        if (_edPasteTest > 0)
        {
            // copier ce qui est pris, le coller N fois en ligne ; puis un modèle brut de la palette
            EditCopy();
            var src = _sel;
            for (int i = 1; i <= _edPasteTest; i++) EditPaste(new Vec3d(src.X + 18 * i, 0, src.Z + 6));
            _clip = (null, "props/coffre_2k.glb", "coffre", 0.0, 1.0, 0.0);
            var raw = EditPaste(new Vec3d(src.X - 12, 0, src.Z + 8));
            GD.Print(FormattableString.Invariant($"[éditeur] {_edPasteTest} copie(s) de {src.Id}, et un coffre brut ({raw?.Radius:F2} m) ; enregistré : {_editReg!.Save()}"));
            return;
        }
        if (push > 0)
        {
            Change(_sel, s => { s.X += push; s.Yaw += Math.PI / 6; });
            GD.Print(FormattableString.Invariant($"[éditeur] poussé en ({_sel.X:F1}, {_sel.Z:F1}), pied {_sel.GroundY:F2} ; enregistré : {_editReg!.Save()}"));
        }
        else
        {
            Change(_sel, s => s.Removed = true);
            GD.Print($"[éditeur] retiré ; enregistré : {_editReg!.Save()}");
        }
    }

    /// <summary>Le fichier des retouches de la région chargée. Une seule définition.</summary>
    static string RetouchPath(string region) =>
        System.IO.Path.Combine(Assets.Root, "world", "retouches", region + ".json");

    /// <summary>Le registre, créé avec le monde : la ville et la terre s'y inscrivent en se bâtissant.</summary>
    void EditSetup()
    {
        if (_world == null) return;
        _editReg = new EditRegistry(RetouchPath(_world.Region.Key), _world.Region.Key);
        if (_editReg.Edits.Count > 0) GD.Print($"[éditeur] {_editReg.Edits.Count} retouche(s) lue(s) pour {_world.Region.Key}");
    }

    /// <summary>Où la terre et les villes se chargent : l'œil de l'éditeur, ou le navire.</summary>
    Vec3d ViewCentre(Vec3d ship) => _editing ? new Vec3d(_edEye.X, 0, _edEye.Z) : ship;

    void ToggleEdit()
    {
        if (_editReg == null) { Say("Pas de monde à retoucher"); return; }
        if (!_editing)
        {
            _editing = true;
            var o = _sea.Core.Origin;
            var p = _cam.GlobalPosition;
            _edEye = new Vec3d(p.X + o.X, Math.Max(p.Y, 4), p.Z + o.Z);
            var f = -_cam.GlobalTransform.Basis.Z;
            _edYaw = Math.Atan2(f.X, f.Z);
            _edPitch = Math.Clamp(Math.Asin(Math.Clamp(f.Y, -1, 1)), -1.45, 1.2);
            _edPrevEye = _fixEye; _edPrevLook = _fixLook;
            /* LE MONDE, PAS LE BORD : instruments, panneaux et comptoir s'effacent
               comme au cinéma, et reviennent tels qu'ils étaient. Si le cinéma les
               tient déjà, c'est lui qui les rendra. */
            if (!_cine) CineHide();
            EditHud(true);
            Say("Mode création · F1 pour les touches");
        }
        else
        {
            _editing = false;
            _sel = null; _edDragging = false; _edLooking = false;
            _fixEye = _edPrevEye; _fixLook = _edPrevLook;
            _painting = false; _paintDown = false;
            if (!_cine) CineShow();
            EditHud(false);
            string painted = _paint is { Dirty: true } ? (_paint.Save() ? " · sol peint enregistré" : " · sol peint NON enregistré") : "";
            if (_editReg.Dirty)
                Say((_editReg.Save() ? $"Retouches enregistrées ({_editReg.Edits.Count})" : "Retouches NON enregistrées : le fichier d'origine était illisible") + painted);
            else Say("Fin du mode création" + painted);
        }
    }

    void EditHud(bool on)
    {
        if (!on)
        {
            _edLayer?.QueueFree(); _edLayer = null; _edLabel = null; _edPalette = null; _edList = null;
            _edRing?.QueueFree(); _edRing = null;
            return;
        }
        _edLayer = new CanvasLayer { Layer = 6 };
        AddChild(_edLayer);
        _edLabel = new Label
        {
            Position = new Vector2(24, 24),
            Size = new Vector2(900, 200),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _edLabel.AddThemeFontSizeOverride("font_size", 17);
        _edLabel.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.78f));
        _edLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _edLabel.AddThemeConstantOverride("shadow_offset_x", 2);
        _edLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        _edLayer.AddChild(_edLabel);
        // l'anneau de la sélection : couché au sol, de la couleur d'un plan de géomètre
        _edRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1f, Rings = 48, RingSegments = 4 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1f, 0.55f, 0.1f),
                NoDepthTest = true
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false
        };
        AddChild(_edRing);
    }

    /// <summary>Chaque image du mode : l'œil qui se déplace, l'anneau, la ligne d'état.</summary>
    void EditTick(double dt)
    {
        double speed = (Physical(Key.Shift) ? 160 : 40) * dt;
        double fx = Math.Sin(_edYaw), fz = Math.Cos(_edYaw);
        double rx = -fz, rz = fx;                       // la droite de l'œil (tribord = −x quand il regarde +z)
        double mx = 0, mz = 0, my = 0;
        if (Physical(Key.W)) { mx += fx; mz += fz; }
        if (Physical(Key.S)) { mx -= fx; mz -= fz; }
        if (Physical(Key.D)) { mx += rx; mz += rz; }
        if (Physical(Key.A)) { mx -= rx; mz -= rz; }
        if (Physical(Key.E)) my += 1;
        if (Physical(Key.Q)) my -= 1;
        _edEye = new Vec3d(_edEye.X + mx * speed, _edEye.Y + my * speed, _edEye.Z + mz * speed);
        // jamais sous le sol ni sous la mer : on y perdrait la souris
        double floor = Math.Max(0, _world!.HeightAt(_edEye.X, _edEye.Z)) + 1.5;
        if (_edEye.Y < floor) _edEye = new Vec3d(_edEye.X, floor, _edEye.Z);

        var o = _sea.Core.Origin;
        var eye = new Vector3((float)(_edEye.X - o.X), (float)_edEye.Y, (float)(_edEye.Z - o.Z));
        double cp = Math.Cos(_edPitch);
        _fixEye = eye;
        _fixLook = eye + new Vector3((float)(fx * cp), (float)Math.Sin(_edPitch), (float)(fz * cp)) * 10f;

        if (_edRing != null)
        {
            ((StandardMaterial3D)_edRing.MaterialOverride).AlbedoColor = new Color(1f, 0.55f, 0.1f);
            _edRing.Visible = _sel is { Removed: false };
            if (_sel != null)
            {
                float r = (float)(_sel.Smoke ? 1.6 : _sel.Radius * _sel.Scale * 1.1 + 0.5);
                // un émetteur se montre à sa bouche, sur le toit ; un objet à son pied
                _edRing.Position = new Vector3((float)(_sel.X - o.X), (float)(_sel.Smoke ? _sel.AimY : _sel.GroundY + 0.15), (float)(_sel.Z - o.Z));
                _edRing.Scale = new Vector3(r, 1, r);
            }
        }
        if (_edLabel != null)
        {
            string head = $"MODE CRÉATION — {_editReg!.Edits.Count} retouche(s){(_editReg.Dirty ? ", non enregistrées" : "")}";
            string what = _sel == null
                ? "Cliquer un bâtiment, un rocher, un arbre."
                : FormattableString.Invariant(
                    $"{_sel.Label} · {_sel.Id}\ncap {((_sel.Yaw * 180 / Math.PI) % 360 + 360) % 360:F0}° · {(_sel.Smoke ? "force" : "échelle")} {_sel.Scale:F2} · levé {_sel.Dy:+0.0;-0.0;0} m{(_sel.Pristine ? " · à sa place" : " · retouché")}");
            if (_painting) { _edLabel.Text = head + "\n" + PaintStatus() + "\nZQSD se déplacer · A E descendre, monter · clic droit regarder · Ctrl+S enregistrer · ² quitter"; }
            else _edLabel.Text = head + "\n" + what +
                "\nZQSD se déplacer · A E descendre, monter · ⇧ plus vite · clic droit regarder" +
                "\nglisser déplacer · molette tourner (⇧ 45°) · PgUp PgDn échelle · ↑ ↓ lever · Suppr retirer · ⌫ rendre à l'automatique" +
                "\nCtrl+C copier · Ctrl+V coller sous la souris · Tab la palette · P le pinceau (herbe, pavés, sable)" +
                "\nCtrl+Z annuler · Ctrl+S enregistrer · ² quitter (et enregistrer)";
        }
        PaintTick(dt);
    }

    /// <summary>Le mode création prend TOUT ce qui arrive ; ² le bascule de partout.</summary>
    bool EditInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            // pas sous une page ouverte : on y écrit, et ² est une lettre comme une autre
            if (key == Key.Quoteleft && !_inTitle && !_chartOpen && !_journalOpen) { ToggleEdit(); return true; }
        }
        if (!_editing) return false;
        // Alt+Entrée reste au plein écran, F1 au mémento ; la palette a sa souris
        if (e is InputEventKey ak && (ak.AltPressed || ak.PhysicalKeycode == Key.F1)) return false;
        if (OverPalette(e)) return false;
        if (PaintInput(e)) return true;

        if (e is InputEventKey kk && kk.Pressed)
        {
            Key key = kk.PhysicalKeycode != Key.None ? kk.PhysicalKeycode : kk.Keycode;
            bool shift = kk.ShiftPressed, ctrl = kk.CtrlPressed;
            if (ctrl && key == Key.S && !kk.Echo)
            {
                string painted = _paint is { Dirty: true } ? (_paint.Save() ? " · sol peint enregistré" : " · sol peint NON enregistré") : "";
                Say((_editReg!.Save() ? $"Retouches enregistrées ({_editReg.Edits.Count})" : "Le fichier d'origine était illisible : rien d'écrit") + painted);
                return true;
            }
            if (key == Key.P && !ctrl && !kk.Echo) { TogglePaintMode(); return true; }
            if (ctrl && key == Key.Z) { EditUndo(); return true; }
            if (ctrl && key == Key.C && !kk.Echo) { EditCopy(); return true; }
            if (ctrl && key == Key.V && !kk.Echo) { EditPaste(); return true; }
            if (key == Key.Tab && !kk.Echo) { TogglePalette(); return true; }
            if (key == Key.Escape && !kk.Echo)
            {
                if (_edPalette is { Visible: true }) _edPalette.Visible = false;
                else if (_sel != null) _sel = null;
                else ToggleEdit();
                return true;
            }
            if (_sel == null) return true;
            switch (key)
            {
                case Key.Delete:
                    if (kk.Echo) break;
                    Change(_sel, s => s.Removed = true);
                    Say($"Retiré : {_sel.Label} · Ctrl+Z pour le rendre");
                    _sel = null;
                    break;
                case Key.Backspace:
                    if (kk.Echo) break;
                    if (_sel.Added) { Say("Un ajout n'a pas de place automatique · Suppr pour le retirer"); break; }
                    Change(_sel, s => s.State = (s.BaseX, s.BaseZ, s.BaseYaw, 1, 0, false));
                    Say("Rendu à sa place automatique");
                    break;
                // ⇧ par moitié : un modèle brut arrive souvent à une taille qui n'a rien à voir
                case Key.Pageup: Change(_sel, s => s.Scale = Math.Min(40, s.Scale * (shift ? 1.5 : 1.05))); break;
                case Key.Pagedown: Change(_sel, s => s.Scale = Math.Max(0.02, s.Scale / (shift ? 1.5 : 1.05))); break;
                case Key.Up: Change(_sel, s => s.Dy += shift ? 1 : 0.1); break;
                case Key.Down: Change(_sel, s => s.Dy -= shift ? 1 : 0.1); break;
            }
            return true;
        }

        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) _edLooking = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _sel = Pick(mb.Position);
                    _edDragging = false;
                    if (_sel != null && GroundUnder(mb.Position) is { } g)
                    {
                        _edUndo.Push((_sel, _sel.State));
                        _edGrabX = _sel.X - g.X; _edGrabZ = _sel.Z - g.Z;
                        _edDragging = true;
                    }
                }
                else _edDragging = false;
            }
            else if (mb.Pressed && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                double step = (mb.ShiftPressed ? 45 : 5) * Math.PI / 180 * (mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                if (_sel != null) Change(_sel, s => s.Yaw += step);
                else
                {
                    // sans sélection, la molette avance l'œil d'un bond
                    double d = mb.ButtonIndex == MouseButton.WheelUp ? 12 : -12;
                    double cp = Math.Cos(_edPitch);
                    _edEye = new Vec3d(_edEye.X + Math.Sin(_edYaw) * cp * d, _edEye.Y + Math.Sin(_edPitch) * d, _edEye.Z + Math.Cos(_edYaw) * cp * d);
                }
            }
            return true;
        }

        if (e is InputEventMouseMotion mm)
        {
            if (_edLooking)
            {
                _edYaw -= mm.Relative.X * 0.004;
                _edPitch = Math.Clamp(_edPitch - mm.Relative.Y * 0.004, -1.45, 1.2);
            }
            else if (_edDragging && _sel != null && GroundUnder(mm.Position) is { } g)
            {
                _sel.X = g.X + _edGrabX; _sel.Z = g.Z + _edGrabZ;
                _editReg!.Commit(_sel);
            }
            return true;
        }
        return true;
    }

    /// <summary>Changer un objet : l'état d'avant va sur la pile, le nouveau est posé et noté.</summary>
    void Change(Editable s, Action<Editable> how)
    {
        _edUndo.Push((s, s.State));
        how(s);
        _editReg!.Commit(s);
    }

    void EditUndo()
    {
        if (_edUndo.Count == 0) { Say("Rien à annuler"); return; }
        var (e, st) = _edUndo.Pop();
        e.State = st;
        _editReg!.Commit(e);
        _sel = e.Removed ? null : e;
    }

    /// <summary>Le rayon de la souris, en mètres VRAIS.</summary>
    (Vec3d From, Vec3d Dir) MouseRay(Vector2 at)
    {
        var o = _sea.Core.Origin;
        var f = _cam.ProjectRayOrigin(at);
        var d = _cam.ProjectRayNormal(at);
        return (new Vec3d(f.X + o.X, f.Y, f.Z + o.Z), new Vec3d(d.X, d.Y, d.Z));
    }

    /// <summary>
    /// L'OBJET SOUS LA SOURIS : le plus proche dont la sphère coupe le rayon. Une
    /// sphère par objet, posée à mi-hauteur — grossier, mais un bâtiment se prend
    /// toujours par le milieu, et le plus proche gagne quand deux se chevauchent.
    /// </summary>
    Editable? Pick(Vector2 at)
    {
        var (f, d) = MouseRay(at);
        Editable? best = null;
        double bestT = 4000;
        foreach (var e in _editReg!.Items)
        {
            // une cheminée sur une maison retirée n'est plus là non plus
            if (e.Removed || (e.Smoke && !e.Live)) continue;
            double r = e.Smoke ? 1.5 : Math.Max(e.Radius, e.Height * 0.5) * e.Scale;
            double cx = e.X - f.X, cy = e.AimY - f.Y, cz = e.Z - f.Z;
            double t = cx * d.X + cy * d.Y + cz * d.Z;
            if (t < 0 || t - r > bestT) continue;
            double px = cx - d.X * t, py = cy - d.Y * t, pz = cz - d.Z * t;
            if (px * px + py * py + pz * pz > r * r) continue;
            // le plus proche, compté au centre : un petit devant un grand l'emporte
            if (t < bestT) { bestT = t; best = e; }
        }
        return best;
    }

    /// <summary>Le sol (ou la mer) sous la souris, en mètres vrais : on avance le long du rayon, puis on resserre.</summary>
    Vec3d? GroundUnder(Vector2 at)
    {
        var (f, d) = MouseRay(at);
        double Surf(double t) => f.Y + d.Y * t - Math.Max(0, _world!.HeightAt(f.X + d.X * t, f.Z + d.Z * t));
        double t0 = 0;
        for (double t = 0.5; t < 6000; t += Math.Max(0.5, t * 0.01))
        {
            if (Surf(t) <= 0)
            {
                double lo = t0, hi = t;
                for (int i = 0; i < 20; i++) { double m = (lo + hi) * 0.5; if (Surf(m) > 0) lo = m; else hi = m; }
                return new Vec3d(f.X + d.X * hi, 0, f.Z + d.Z * hi);
            }
            t0 = t;
        }
        return null;
    }
}
