using Godot;

namespace NavalSim;

/// <summary>
/// L'ÉCRITURE DU CAPITAINE, en Estonia (Robert Leuschke, licence OFL) : une
/// anglaise à la plume, comme on écrivait un livre de bord. Lue à l'exécution,
/// sans import, et UNE fois pour tous ses usagers — les notes de la carte et le
/// temps qu'il fait. Absente, chacun garde la police du moteur.
/// </summary>
public static class HandFont
{
    static Font? _font;
    static bool _tried;

    public static Font? Get()
    {
        if (_tried) return _font;
        _tried = true;
        string path = ProjectSettings.GlobalizePath("res://fonts/Estonia-Regular.ttf");
        if (!System.IO.File.Exists(path)) return null;
        var f = new FontFile();
        if (f.LoadDynamicFont(path) != Error.Ok) return null;
        _font = f;
        GD.Print("la main du capitaine : Estonia");
        return _font;
    }
}
