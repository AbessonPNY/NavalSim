using System;
using System.Collections.Generic;
using Godot;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// UN OBJET QUE LA MAIN PEUT RETOUCHER : une maison, un pâté, un modèle posé, un
/// rocher d'un semis, un figurant — ou un AJOUT, posé de plus par la main. Celui
/// qui l'a bâti dit où il est de lui-même (Base*) et sait le reposer
/// (<see cref="Push"/>) ; l'éditeur ne fait que changer l'état voulu.
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

    /* DE QUOI LE COPIER. Chaque bâtisseur sait refaire l'objet à part, en un nœud
       qu'on pose où l'on veut : MakeVisual le rend à l'échelle 1, son origine au
       point qu'on pose, déjà tourné de VisualYaw (un semis cuit son cap dans sa
       copie) et à Lift au-dessus du sol (un rocher s'enfonce, un homme a ses pieds
       sous son origine). La FAMILLE groupe les objets d'un même modèle : la
       palette en montre un par famille. */
    public Func<Node3D>? MakeVisual;
    public double VisualYaw, Lift;
    public string Family = "", FamilyLabel = "";

    /// <summary>
    /// UN ÉMETTEUR, et non un objet : une fumée de cheminée n'a pas de corps. Elle
    /// sort à <see cref="Lift"/> au-dessus de son pied (le faîte d'un toit), et son
    /// échelle est sa FORCE — PgUp la fait fumer davantage.
    /// </summary>
    public bool Smoke;
    /// <summary>Le tirage de la densité (0..1), fait une fois sur le nom ; négatif tant qu'il n'est pas fait.</summary>
    public double Roll = -1;
    /// <summary>La part de bouffée que la cheminée doit encore au ciel (GunFxNode.Hearth) : l'émission est régulière, pas tirée au sort.</summary>
    public double SmokeDue;

    /* UNE CHEMINÉE APPARTIENT À SA MAISON. Tant qu'on ne l'a pas déplacée
       elle-même, elle la SUIT — sa place le long du faîte (HostOx, dans l'axe de
       la maison), sa hauteur au-dessus du pied (HostTop), le cap et l'échelle de
       la maison — et s'éteint quand la maison est retirée. Déplacée à la main,
       elle devient indépendante : on l'a mise ailleurs exprès. */
    public Editable? Host;
    public double HostOx, HostTop;
    /// <summary>Les cheminées d'une maison, dans son repère : une copie les emporte.</summary>
    public List<(double Ox, double Top)>? Chimneys;

    /// <summary>Elle fume : ni retirée, ni sur une maison retirée.</summary>
    public bool Live => !Removed && (Host == null || Moved || !Host.Removed);

    /// <summary>Poser un émetteur : sur sa maison s'il la suit, sinon sur le sol où on l'a mis.</summary>
    public void PlaceEmitter(Func<double, double, double> heightAt)
    {
        if (Host != null && !Moved)
        {
            double s = Host.Scale, ca = Math.Cos(Host.Yaw), sa = Math.Sin(Host.Yaw);
            X = BaseX = Host.X + HostOx * ca * s;
            Z = BaseZ = Host.Z - HostOx * sa * s;
            GroundY = Host.GroundY + Dy;
            Lift = HostTop * s;
        }
        else
        {
            GroundY = heightAt(X, Z) + Dy;
            if (Host != null) Lift = HostTop;
        }
    }

    /// <summary>Le point qu'on vise et qu'on montre, en mètres vrais : la bouche d'un émetteur, le milieu d'un objet.</summary>
    public double AimY => Smoke ? GroundY + Lift : GroundY + Height * Scale * 0.5;

    /// <summary>Un ajout : la copie de <see cref="From"/>, ou le modèle brut <see cref="Glb"/>.</summary>
    public string From = "", Glb = "";
    /// <summary>Un navire au mouillage posé à la main : sa fiche (MooredNode.AddHull).</summary>
    public string Sheet = "";
    public bool Added => From.Length > 0 || Glb.Length > 0 || Sheet.Length > 0;

    public bool Moved => Math.Abs(X - BaseX) > 1e-3 || Math.Abs(Z - BaseZ) > 1e-3;

    /// <summary>Rien de changé : la retouche n'a plus lieu d'être.</summary>
    public bool Pristine => !Added && !Removed && !Moved && Math.Abs(Yaw - BaseYaw) < 1e-4
                            && Math.Abs(Scale - 1) < 1e-4 && Math.Abs(Dy) < 1e-4;

    public (double, double, double, double, double, bool) State
    {
        get => (X, Z, Yaw, Scale, Dy, Removed);
        set => (X, Z, Yaw, Scale, Dy, Removed) = value;
    }
}

/// <summary>
/// LE REGISTRE DE CE QUI SE RETOUCHE, et les retouches d'une région. Ceux qui
/// bâtissent (la ville, la terre, les figurants) y inscrivent chaque objet en le
/// posant ; la retouche enregistrée, s'il y en a une, est appliquée à
/// l'inscription — si bien qu'un objet retouché naît à la place voulue, sans
/// passer par l'automatique.
/// </summary>
public sealed class EditRegistry
{
    public readonly Edits Edits;
    public readonly string Path, Region;
    public readonly List<Editable> Items = new();
    public readonly Dictionary<string, Editable> ById = new();
    public bool Dirty;

    public EditRegistry(string path, string region)
    {
        Path = path; Region = region;
        try { Edits = Edits.Load(path); }
        catch (Exception ex)
        {
            GD.PushWarning($"[éditeur] {path} illisible ({ex.Message}) — on repart sans retouches, et l'on n'écrira pas par-dessus.");
            Edits = new Edits();
            _unreadable = true;
        }
    }
    readonly bool _unreadable;

    public void Add(Editable e)
    {
        if (Edits.Get(e.Id) is { } ed && !ed.Added)
        {
            e.X = ed.X; e.Z = ed.Z; e.Yaw = ed.Yaw * Math.PI / 180;
            e.Scale = ed.Scale; e.Dy = ed.Dy; e.Removed = ed.Removed;
            if (ed.Removed) { e.X = e.BaseX; e.Z = e.BaseZ; e.Yaw = e.BaseYaw; }
        }
        Items.Add(e);
        ById[e.Id] = e;
        e.Push?.Invoke(e);
    }

    /// <summary>Un ajout, posé par la main ou relu du fichier : il n'a pas de place automatique.</summary>
    public void AddNew(Editable e)
    {
        Items.Add(e);
        ById[e.Id] = e;
        e.Push?.Invoke(e);
    }

    /// <summary>Reposer l'objet et noter l'état voulu ; un objet rendu à l'automatique perd sa retouche, un ajout retiré aussi.</summary>
    public void Commit(Editable e)
    {
        e.Push?.Invoke(e);
        if (e.Pristine || (e.Added && e.Removed)) Edits.Forget(e.Id);
        else Edits.Set(new Edit
        {
            Id = e.Id, X = e.X, Z = e.Z, Yaw = e.Yaw * 180 / Math.PI,
            Scale = e.Scale, Dy = e.Dy, Removed = e.Removed, From = e.From, Glb = e.Glb, Sheet = e.Sheet
        });
        Dirty = true;
    }

    public string NextAddId() => Edits.NextAddId(ById.Keys);

    /// <summary>Écrire. Faux si le fichier d'origine était illisible : on ne l'écrase pas.</summary>
    public bool Save()
    {
        if (_unreadable) return false;
        Edits.Save(Path, Region);
        Dirty = false;
        return true;
    }
}
