using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// UN OBJET QUE LA MAIN PEUT RETOUCHER : une maison, un pâté, un modèle posé, un
/// rocher d'un semis. Celui qui l'a bâti dit où il est de lui-même (Base*) et sait
/// le reposer (<see cref="Push"/>) ; l'éditeur ne fait que changer l'état voulu.
///
/// Le cap est celui d'une <c>Basis(Vector3.Up, cap)</c>, en radians, partout : une
/// seule convention, la même que celle des maisons.
/// </summary>
public sealed class Editable
{
    public string Id = "", Label = "";
    public double BaseX, BaseZ, BaseYaw;
    public double X, Z, Yaw, Scale = 1, Dy;
    public bool Removed;
    /// <summary>De quoi le viser : un rayon et une hauteur, en mètres, à l'échelle 1.</summary>
    public double Radius = 5, Height = 6;
    /// <summary>Le pied de l'objet, en mètres vrais, tel que <see cref="Push"/> l'a posé.</summary>
    public double GroundY;
    public Action<Editable>? Push;

    public bool Moved => Math.Abs(X - BaseX) > 1e-3 || Math.Abs(Z - BaseZ) > 1e-3;

    /// <summary>Rien de changé : la retouche n'a plus lieu d'être.</summary>
    public bool Pristine => !Removed && !Moved && Math.Abs(Yaw - BaseYaw) < 1e-4
                            && Math.Abs(Scale - 1) < 1e-4 && Math.Abs(Dy) < 1e-4;

    public (double X, double Z, double Yaw, double Scale, double Dy, bool Removed) State
    {
        get => (X, Z, Yaw, Scale, Dy, Removed);
        set => (X, Z, Yaw, Scale, Dy, Removed) = value;
    }
}

/// <summary>
/// LE REGISTRE DE CE QUI SE RETOUCHE, et les retouches d'une région. Ceux qui
/// bâtissent (la ville, la terre) y inscrivent chaque objet en le posant ; la
/// retouche enregistrée, s'il y en a une, est appliquée à l'inscription — si bien
/// qu'un objet retouché naît à la place voulue, sans passer par l'automatique.
/// </summary>
public sealed class EditRegistry
{
    public readonly Edits Edits;
    public readonly string Path, Region;
    public readonly List<Editable> Items = new();
    public bool Dirty;

    public EditRegistry(string path, string region)
    {
        Path = path; Region = region;
        try { Edits = Edits.Load(path); }
        catch (Exception ex)
        {
            Godot.GD.PushWarning($"[éditeur] {path} illisible ({ex.Message}) — on repart sans retouches, et l'on n'écrira pas par-dessus.");
            Edits = new Edits();
            _unreadable = true;
        }
    }
    readonly bool _unreadable;

    public void Add(Editable e)
    {
        if (Edits.Get(e.Id) is { } ed)
        {
            e.X = ed.X; e.Z = ed.Z; e.Yaw = ed.Yaw * Math.PI / 180;
            e.Scale = ed.Scale; e.Dy = ed.Dy; e.Removed = ed.Removed;
            if (ed.Removed) { e.X = e.BaseX; e.Z = e.BaseZ; e.Yaw = e.BaseYaw; }
        }
        Items.Add(e);
        e.Push?.Invoke(e);
    }

    /// <summary>Reposer l'objet et noter l'état voulu ; un objet rendu à l'automatique perd sa retouche.</summary>
    public void Commit(Editable e)
    {
        e.Push?.Invoke(e);
        if (e.Pristine) Edits.Forget(e.Id);
        else Edits.Set(new Edit
        {
            Id = e.Id, X = e.X, Z = e.Z, Yaw = e.Yaw * 180 / Math.PI,
            Scale = e.Scale, Dy = e.Dy, Removed = e.Removed
        });
        Dirty = true;
    }

    /// <summary>Écrire. Faux si le fichier d'origine était illisible : on ne l'écrase pas.</summary>
    public bool Save()
    {
        if (_unreadable) return false;
        Edits.Save(Path, Region);
        Dirty = false;
        return true;
    }
}
