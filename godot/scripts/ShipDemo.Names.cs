using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES NOMS DE LA RADE (MooredNode.NameFor) — et l'essai des lettres (--nom-essai).
/// Les navires qui naviguent se nomment seuls (ShipNode.Name.cs) ; ceux d'une rade
/// ne sont pas des ShipNode, et c'est la démo qui sait quelles nations un port admet.
/// </summary>
public partial class ShipDemo
{
    PortNations? _mooredNations;

    /* À GRAINE : le port et le rang du poste. Une rade qu'on quitte et qu'on retrouve,
       ou qu'on recharge, montre les mêmes navires — sous les mêmes noms. Sa nation est
       celle de la fiche, sinon une de celles que le port admet (pavillons-des-ports.json),
       comme pour les départs de ses horaires. */
    string? MooredName(string port, int slot, ShipSpec spec)
    {
        uint seed = 2166136261;
        foreach (char c in port) seed = (seed ^ c) * 16777619;
        seed = (seed ^ (uint)slot) * 16777619;
        var rng = new Random(unchecked((int)seed));
        _mooredNations ??= LoadPortNations();
        string? nat = spec.Raw.Nation is { Length: > 0 } id ? id
                    : _nations.Draw(rng.NextDouble, x => _mooredNations.Admits(port, x.Id))?.Id;
        return NameBoard.Draw(nat, rng.NextDouble);
    }

    /// <summary>--nom-essai "Texte" : le nom peint en image (user://nom-essai.png), à la proportion d'un tableau de 4 × 1.</summary>
    void NameProof(string text)
    {
        string path = ProjectSettings.GlobalizePath("user://nom-essai.png");
        GD.Print(NameBoard.Proof(text, 4f, path) ? $"[noms] « {text} » écrit dans {path}" : $"[noms] « {text} » : aucune lettre rendue");
    }
}
