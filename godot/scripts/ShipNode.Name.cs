using Godot;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// SON NOM — celui de sa fiche si elle est <c>"unique"</c>, sinon un nom de baptême tiré
/// à la mise à l'eau dans la liste de sa nation (ships/noms.json), et peint sur les guides
/// <c>nom</c> de son modèle (<see cref="NameBoard"/>).
///
/// LE TIRAGE ATTEND LA FIN DE L'IMAGE. Une coque naît sous un pavillon tiré au hasard
/// (SpawnFleet → Colours), puis celui qui la met à l'eau le change souvent dans la même
/// image : la rade le restreint à ses nations, l'escarmouche lui donne son camp, le forban
/// hisse le noir. Nommée tout de suite, elle aurait pris le nom d'une autre nation. Le nom
/// se fixe donc en différé, sur le pavillon qu'elle a quand tout est dit ; lu avant, il
/// est tiré d'avance et retiré si le pavillon a changé entre-temps. Après, plus rien ne le
/// change : le pirate qui se démasque garde le nom de marchand sous lequel il naviguait.
/// </summary>
public partial class ShipNode
{
    /// <summary>Son nom de baptême ; nul pour une fiche unique, qui garde le sien.</summary>
    public string? Christened { get; private set; }

    bool _namePinned, _nameSettled;
    string? _nameNation;
    List<NameBoard.Guide>? _nameGuides;
    List<Decal>? _nameDecals;

    /// <summary>Son nom, tel qu'on le dit : « Speedwell », « La Bonne Espérance », « HMS Roebuck ».</summary>
    public string Called
    {
        get
        {
            if (Christened == null && !_nameSettled) AutoName();
            return Christened ?? Spec.Label;
        }
    }

    /// <summary>Son nom et ce qu'elle est, pour une liste ou une annonce : « Speedwell, sloop ».</summary>
    public string Styled => Called is var c && Christened != null && Spec.Type.Length > 0 ? $"{c}, {Spec.Type}" : c;

    /// <summary>« le Speedwell », « la Bonne Espérance ».</summary>
    public string TheName => ShipNames.Le(Called);
    /// <summary>« du Speedwell », « de la Bonne Espérance ».</summary>
    public string OfName => ShipNames.Du(Called);

    /// <summary>
    /// LUI DONNER UN NOM, et qu'on n'y touche plus : la reprise d'une partie, une rade qui
    /// revient la même. Nul : retour au tirage.
    /// </summary>
    public void Christen(string? name)
    {
        _namePinned = name != null;
        _nameSettled = true;
        if (name == null) { _nameSettled = false; AutoName(); return; }
        SetName(name);
    }

    void SetName(string? name)
    {
        if (Christened == name) { PaintName(); return; }
        if (Christened != null) NameBoard.Release(Christened);
        Christened = name;
        if (name != null) NameBoard.Hold(name);
        PaintName();
    }

    /// <summary>La liste où le tirer : le pavillon noir passe devant, puis celui qu'elle porte, puis celui de sa fiche.</summary>
    string? NameNation() => UnderBlack ? ShipNames.Pirate : _nation?.Id ?? Spec.Raw.Nation;

    void AutoName()
    {
        if (_namePinned || Spec == null || Spec.Unique || SailsOnly) return;
        var nat = NameNation();
        if (Christened != null && nat == _nameNation) return;
        _nameNation = nat;
        SetName(NameBoard.Draw(nat) ?? Christened);
    }

    /// <summary>À la fin de l'image de sa mise à l'eau : le nom de la nation qu'elle a vraiment.</summary>
    void SettleName()
    {
        if (!IsInstanceValid(this) || _nameSettled) return;
        AutoName();
        _nameSettled = true;
    }

    /// <summary>Appelé à la fin de Build.</summary>
    void BuildName()
    {
        if (SailsOnly) return;
        if (_nameGuides is { Count: > 0 } g && ModelRoot != null && _nameDecals == null)
        {
            _nameDecals = NameBoard.Mount(ModelRoot, g, ModelRoot.Scale.X);
            GD.Print($"[{Spec.Id}] {g.Count} marque(s) de nom dans le modèle");
        }
        if (Spec.Unique) PaintName();
        else Callable.From(SettleName).CallDeferred();
    }

    void PaintName()
    {
        if (_nameDecals is { Count: > 0 } d) NameBoard.Paint(d, Christened ?? Spec.Label, Spec.Model?.Lettering);
    }

    /// <summary>Son nom rendu au registre, quand elle quitte la scène (dans _ExitTree).</summary>
    void ReleaseName()
    {
        // elle garde son nom pour qui en parle encore (« … saute »), elle ne le retient plus
        if (Christened != null && !_nameReleased) NameBoard.Release(Christened);
        _nameReleased = true;
        _nameSettled = _namePinned = true;     // une coque retirée ne se renomme pas en différé
    }

    bool _nameReleased;
}
