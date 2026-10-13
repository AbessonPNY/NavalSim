using Godot;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'INVENTAIRE (I) : ce qu'on a remonté et qui reste à bord, rangé dans un coffre
/// ouvert — trois rangées de huit cases, vides tant qu'on n'a rien trouvé.
///
/// En tête, la BOURSE : écus d'or et pièces d'argent, lus sur <c>_purse</c> comme
/// la ligne des instruments. Puis ce qui se garde à la pièce (<c>_treasureHold</c> :
/// lambis, perles, pierres, bijoux, orfèvrerie), une case par sorte dans l'ordre
/// de treasure/tresor.json, le nombre en coin.
///
/// Les images des sortes (« icone ») et du coffre (« dessus ») se lisent dans la
/// même fiche ; sans elles, le coffre est dessiné et la case porte le nom à la main.
/// </summary>
public partial class ShipDemo : Node3D
{
    CanvasLayer? _invLayer;
    InventoryChest? _invChest;
    bool _invOpen;
    Input.MouseModeEnum _invMouseWas;
    readonly Dictionary<string, Texture2D?> _invImages = new();
    /// <summary>ESSAI (`--inventaire 2`) : des cases montrées pleines, sans rien mettre dans la cale.</summary>
    bool _invSample;

    void BuildInventory()
    {
        _invLayer = new CanvasLayer { Layer = 8, Visible = false };
        AddChild(_invLayer);
        _invLayer.AddChild(new ColorRect
        {
            AnchorRight = 1, AnchorBottom = 1,
            Color = new Color(0.03f, 0.03f, 0.04f, 0.72f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _invChest = new InventoryChest { AnchorRight = 1, AnchorBottom = 1, Title = "Le coffre du bord" };
        _invLayer.AddChild(_invChest);
    }

    /// <summary>Une image de la fiche, lue une fois ; null si le chemin est vide ou le fichier absent.</summary>
    Texture2D? InventoryImage(string rel)
    {
        if (rel.Length == 0) return null;
        if (_invImages.TryGetValue(rel, out var t)) return t;
        string path = Assets.Path(rel);
        Texture2D? tex = null;
        if (System.IO.File.Exists(path) && Image.LoadFromFile(path) is { } img)
        {
            img.GenerateMipmaps();
            tex = ImageTexture.CreateFromImage(img);
        }
        else GD.PrintErr($"[inventaire] image introuvable : {rel}");
        return _invImages[rel] = tex;
    }

    void FillInventory()
    {
        if (_invChest == null) return;
        var slots = _invChest.Slots;
        System.Array.Clear(slots);
        int n = 0;
        void Put(string name, string unit, string units, string note, long count, string icon, bool money = false)
        {
            if (n >= slots.Length) return;
            string tip = $"{name} — {count} {(count > 1 ? units : unit)}" + (note.Length > 0 ? "\n" + note : "");
            slots[n++] = new InventoryChest.Slot { Name = name, Count = count, Icon = InventoryImage(icon), Tip = tip, Money = money };
        }
        /* LA BOURSE EN TÊTE (demandé) : les écus et les pièces d'argent, les mêmes
           nombres que la ligne des instruments — c'est la même bourse, lue au même
           endroit. Une monnaie dont on n'a rien ne prend pas de case. */
        if (_purse.Ecus > 0 && _tresors.ByKey("ecus") is { } ec)
            Put(ec.Name, "écu", "écus", ec.Note, _purse.Ecus, ec.Icon, money: true);
        if (_purse.Pieces > 0)
            Put(_tresors.SilverName, "pièce", "pièces", $"{Market.SousParEcu} pièces font un écu",
                _purse.Pieces, _tresors.SilverIcon, money: true);
        var hold = _invSample
            ? new Dictionary<string, int> { ["lambi"] = 7, ["perle"] = 2, ["pierres"] = 3, ["bijoux"] = 1, ["orfevrerie"] = 1 }
            : _treasureHold;
        var seen = new HashSet<string>();
        foreach (var k in _tresors.Kinds)
        {
            seen.Add(k.Key);
            if (k.ToPurse || hold.GetValueOrDefault(k.Key) <= 0) continue;
            Put(k.Name, k.Unit.Length > 0 ? k.Unit : k.Name, k.Units.Length > 0 ? k.Units : k.Name, k.Note,
                hold[k.Key], k.Icon);
        }
        // une sorte gardée que la fiche ne connaît plus (sauvegarde ancienne) : on la montre quand même
        foreach (var (key, count) in hold)
            if (count > 0 && !seen.Contains(key)) Put(key, key, key, "", count, "");

        _invChest.Top = InventoryImage(_tresors.ChestTop);
        _invChest.Footer = (n == 0 ? "Le coffre est vide" : $"{n} case{(n > 1 ? "s" : "")} sur {slots.Length}")
            + "   ·   I ou Échap : refermer";
        _invChest.Refresh();
    }

    void ToggleInventory()
    {
        if (_invLayer == null || _inTitle) return;
        _invOpen = !_invLayer.Visible;
        _invLayer.Visible = _invOpen;
        if (_invOpen)
        {
            if (_chartOpen) ToggleChart();
            // le coffre a l écran pour lui : les instruments et le comptoir s effacent le temps de le lire
            _hud.Visible = false;
            FillInventory();
            // la souris, pour lire les bulles des cases ; rendue telle qu'on l'a prise
            _invMouseWas = Input.MouseMode;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else { Input.MouseMode = _invMouseWas; _hud.Visible = true; }
    }

    /// <summary>Le coffre ouvert ne prend que ce qui le referme : le navire, lui, continue sa route.</summary>
    bool InventoryInput(InputEvent e)
    {
        if (!_invOpen || e is not InputEventKey k || !k.Pressed || k.Echo) return false;
        Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
        if (key == Key.Escape || (key == Key.I && !k.ShiftPressed && !k.CtrlPressed)) { ToggleInventory(); return true; }
        // Ctrl I : la carte prend la place, et le coffre se referme dessous
        if (key == Key.I && k.CtrlPressed) ToggleInventory();
        return false;
    }
}
