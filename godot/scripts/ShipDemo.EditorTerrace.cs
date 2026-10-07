using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE TERRASSEMENT DU MODE CRÉATION (demandé : « monter le sol et le descendre, ajuster
/// la hauteur au bord de l'eau ou surélever le terrain qui le nécessite »). T bascule
/// l'outil, 1 monter, 2 descendre, 3 aplanir (à la hauteur du premier clic), 4 adoucir ;
/// la molette règle le rayon (⇧ : la force), le clic gauche terrasse, Ctrl+Z annule le
/// dernier coup.
///
/// La grille est un <see cref="Terraform"/> du noyau, sur le carré du sol peint du port :
/// tout ce qui lit la terre la voit à l'instant (l'échouage, les sondes, l'œil). La terre
/// DESSINÉE se rebâtit par morceaux pendant le geste (LandNode.Reshape). Ce qui a été
/// posé une fois au chargement — la végétation semée, les maisons des faubourgs, le
/// champ de distance au rivage, l'abri — se recale au prochain chargement ; les objets
/// posés à la main sous le pinceau, eux, se reposent au lever.
///
/// Enregistré avec les retouches (Ctrl+S, ou en quittant le mode), dans
/// world/terrassement/&lt;port&gt;.bin.
/// </summary>
public partial class ShipDemo
{
    Terraform? _terra;
    string _terraPath = "";
    bool _terracing, _terraDown, _terraDirty;
    Terraform.Tool _terraTool = Terraform.Tool.Raise;
    double _terraR = 8, _terraRate = 1.5, _terraTarget, _terraSince;
    (double X0, double Z0, double X1, double Z1)? _terraRect, _terraStroke;
    readonly LinkedList<float[]> _terraUndo = new();
    const int TerraUndoDepth = 6;
    /// <summary>Le pas de la grille, en mètres : une berge se retouche au mètre.</summary>
    const double TerraStep = 1.0;

    static readonly string[] TerraNames = { "monter", "descendre", "aplanir", "adoucir" };
    static readonly Color[] TerraColors =
        { new(0.45f, 0.95f, 0.45f), new(1f, 0.45f, 0.35f), new(0.55f, 0.75f, 1f), new(1f, 0.9f, 0.5f) };

    /// <summary>
    /// Le terrassement de la région, s'il y en a un : sur le carré du premier sol peint dont
    /// le port existe. Appelé JUSTE APRÈS la lecture du monde, avant que rien n'y lise le
    /// relief (villes, abris, semis) : chargé plus tard, il arriverait après eux.
    /// </summary>
    void TerraSetup()
    {
        if (_world == null) return;
        foreach (var p in _world.Region.Paints)
        {
            if (_world.ByKey(p.Port) is not { } isl) continue;
            _terra = new Terraform(isl.X, isl.Z, p.Side, TerraStep);
            _terraPath = System.IO.Path.Combine(Assets.Root, "world", "terrassement", p.Port + ".bin");
            if (System.IO.File.Exists(_terraPath))
            {
                if (_terra.Load(System.IO.File.ReadAllBytes(_terraPath)))
                    GD.Print($"[terrassement] {p.Port} relu : {_terra.N}×{_terra.N} au pas de {TerraStep} m");
                else GD.PushWarning($"[terrassement] {_terraPath} ne correspond pas au carré ({_terra.N}×{_terra.N}) : laissé de côté, il n'est pas écrasé tant qu'on ne terrasse pas");
            }
            _world.Terraforms.Add(_terra);
            break;
        }
    }

    void ToggleTerraceMode()
    {
        if (_terra == null) { Say("Pas de terrain à terrasser dans cette région (le carré du sol peint d'un port)"); return; }
        _terracing = !_terracing;
        _terraDown = false;
        _painting = false; _paintDown = false;
        _sel = null;
        Say(_terracing ? $"Terrassement : {TerraNames[(int)_terraTool]} · 1 monter, 2 descendre, 3 aplanir, 4 adoucir" : "Terrassement rangé");
    }

    bool TerraceInput(InputEvent e)
    {
        if (!_terracing || _terra == null) return false;
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            int pick = key switch { Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3, _ => -1 };
            if (pick >= 0) { _terraTool = (Terraform.Tool)pick; Say($"Terrassement : {TerraNames[pick]}"); return true; }
            if (k.CtrlPressed && key == Key.Z) { Say(TerraUndo() ? "Coup de terrassement annulé" : "Rien à annuler"); return true; }
            if (key == Key.T && !k.CtrlPressed) { ToggleTerraceMode(); return true; }
            return false;
        }
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed && !_terraDown) TerraBegin();
                else if (!mb.Pressed && _terraDown) TerraEnd();
                _terraDown = mb.Pressed;
                return true;
            }
            if (mb.Pressed && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                bool up = mb.ButtonIndex == MouseButton.WheelUp;
                if (mb.ShiftPressed) _terraRate = Math.Clamp(_terraRate * (up ? 1.25 : 0.8), 0.1, 10);
                else _terraR = Math.Clamp(_terraR * (up ? 1.15 : 1 / 1.15), 1, 80);
                return true;
            }
        }
        return false;
    }

    void TerraBegin()
    {
        _terraUndo.AddLast((float[])_terra!.H.Clone());
        while (_terraUndo.Count > TerraUndoDepth) _terraUndo.RemoveFirst();
        _terraStroke = null;
        // l'aplanissoir vise la hauteur du sol sous le premier clic
        if (GroundUnder(GetViewport().GetMousePosition(), true) is { } g) _terraTarget = _world!.IslandHeight(g.X, g.Z);
    }

    void TerraEnd()
    {
        TerraFlush(true);
        // les objets posés à la main sous le coup reprennent leur pied
        if (_terraStroke is { } s && _editReg != null)
            foreach (var it in _editReg.Items)
                if (it.X >= s.X0 && it.X <= s.X1 && it.Z >= s.Z0 && it.Z <= s.Z1) it.Push?.Invoke(it);
        _terraStroke = null;
    }

    /// <summary>Rebâtir ce que le pinceau a touché depuis la dernière fois — au plus dix fois par seconde, et au lever.</summary>
    void TerraFlush(bool now)
    {
        if (_terraRect is not { } r || _land == null) return;
        if (!now && _terraSince < 0.1) return;
        _land.Reshape(r.X0, r.Z0, r.X1, r.Z1);
        _terraRect = null;
        _terraSince = 0;
    }

    bool TerraUndo()
    {
        if (_terra == null || _terraUndo.Count == 0) return false;
        Array.Copy(_terraUndo.Last!.Value, _terra.H, _terra.H.Length);
        _terraUndo.RemoveLast();
        _terraDirty = true;
        _land?.Reshape(_terra.X0, _terra.Z0, _terra.X0 + _terra.Side, _terra.Z0 + _terra.Side);
        return true;
    }

    void TerraceTick(double dt)
    {
        if (_terra == null) return;
        _terraSince += dt;
        if (!_terracing) return;
        var under = GroundUnder(GetViewport().GetMousePosition(), true);
        if (_terraDown && under is { } g && _terra.Covers(g.X, g.Z))
        {
            // la force est PAR SECONDE : un clic tenu monte, un passage rapide effleure
            var hit = _terra.Dab(g.X, g.Z, _terraR, _terraTool, _terraRate, Math.Min(dt, 0.1), _terraTarget, _world!.ReliefHeight);
            if (hit is { } h)
            {
                double x0 = _terra.X0 + h.I0 * TerraStep, z0 = _terra.Z0 + h.J0 * TerraStep;
                double x1 = _terra.X0 + h.I1 * TerraStep, z1 = _terra.Z0 + h.J1 * TerraStep;
                _terraRect = _terraRect is { } r ? (Math.Min(r.X0, x0), Math.Min(r.Z0, z0), Math.Max(r.X1, x1), Math.Max(r.Z1, z1)) : (x0, z0, x1, z1);
                _terraStroke = _terraStroke is { } s ? (Math.Min(s.X0, x0), Math.Min(s.Z0, z0), Math.Max(s.X1, x1), Math.Max(s.Z1, z1)) : (x0, z0, x1, z1);
                _terraDirty = true;
            }
        }
        TerraFlush(false);

        if (_edRing != null && under is { } u)
        {
            var o = _sea.Core.Origin;
            _edRing.Visible = true;
            float rr = (float)_terraR;
            _edRing.Position = new Vector3((float)(u.X - o.X), (float)_world!.HeightAt(u.X, u.Z) + 0.15f, (float)(u.Z - o.Z));
            _edRing.Scale = new Vector3(rr, 1, rr);
            ((StandardMaterial3D)_edRing.MaterialOverride).AlbedoColor =
                _terra.Covers(u.X, u.Z) ? TerraColors[(int)_terraTool] : new Color(0.5f, 0.5f, 0.5f);
        }
    }

    /// <summary>Écrire la grille. Rien si elle n'a pas changé ; vide et jamais écrite, rien non plus.</summary>
    string TerraSave()
    {
        if (_terra == null || !_terraDirty) return "";
        if (!System.IO.File.Exists(_terraPath) && !_terra.Any()) { _terraDirty = false; return ""; }
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_terraPath)!);
            System.IO.File.WriteAllBytes(_terraPath, _terra.Save());
            _terraDirty = false;
            return " · terrassement enregistré";
        }
        catch (Exception ex) { GD.PushWarning($"[terrassement] {ex.Message}"); return " · terrassement NON enregistré"; }
    }

    /// <summary>-- --terrasser dx,dz,rayon,mètres : relever (ou creuser) le sol près du port de départ, SANS rien écrire — pour l'essai.</summary>
    string? _terraTest;

    void TerraTestTick()
    {
        if (_terraTest == null || _terra == null || _land == null || _world?.StartPort is not { } home) return;
        var a = _terraTest.Split(',');
        _terraTest = null;
        double dx = a[0].ToFloat(), dz = a[1].ToFloat(), r = a.Length > 2 ? a[2].ToFloat() : 20, m = a.Length > 3 ? a[3].ToFloat() : 3;
        double x = home.Port.Hx + dx, z = home.Port.Hz + dz;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _terra.Dab(x, z, r, m >= 0 ? Terraform.Tool.Raise : Terraform.Tool.Lower, Math.Abs(m), 1, 0, _world.ReliefHeight);
        double t1 = clock.Elapsed.TotalMilliseconds;
        _land.Reshape(x - r, z - r, x + r, z + r);
        // et l'œil dessus, de biais, pour le voir
        var o = _sea.Core.Origin;
        _fixLook = new Vector3((float)(x - o.X), (float)_world.HeightAt(x, z), (float)(z - o.Z));
        _fixEye = _fixLook + new Vector3((float)(r * 2.2), (float)(r * 0.8), (float)(r * 2.2));
        _planted = true;
        GD.Print(FormattableString.Invariant($"[terrassement] essai : {m:+0.0;-0.0} m sur {r:F0} m en ({x:F0}, {z:F0}), non enregistré ; grille {t1:F1} ms, terre rebâtie {clock.Elapsed.TotalMilliseconds - t1:F1} ms ; sol {_world.HeightAt(x, z):F2} m"));
    }

    string TerraStatus() =>
        FormattableString.Invariant($"TERRASSEMENT — {TerraNames[(int)_terraTool]} · rayon {_terraR:F1} m · force {_terraRate:F1} m/s{(_terraDirty ? " · non enregistré" : "")}\n") +
        "1 monter · 2 descendre · 3 aplanir (à la hauteur du premier clic) · 4 adoucir · clic gauche terrasser · molette rayon (⇧ force) · Ctrl+Z annuler le coup · T ranger";
}
