using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE SAUT PAR LES ANNEAUX — poser une cible sur la carte, charger, partir.
///
/// La règle est dans le noyau (<see cref="Teleport"/>) parce qu'elle vaut pour
/// les deux moteurs ; ici il n'y a que la mise en scène et le déplacement.
///
/// LA CIBLE SE POSE À ⇧CLIC SUR LA CARTE OUVERTE. Le clic simple est la plume,
/// le droit une note, la molette la loupe, le milieu le déplacement — ⇧ était ce
/// qui restait, et c'est aussi ce que font les logiciels de carte pour « poser un
/// point » plutôt que « dessiner ».
///
/// LE SAUT NE TOUCHE PAS À L'ORIGINE DU MONDE, et c'est la même raison que pour
/// <c>BackToBerth</c> : changer l'origine emmène tout le monde avec elle, si bien
/// que le pirate qu'on fuyait sauterait aussi. On ne déplace donc QUE la coque,
/// et le glissement de fin d'image recentre l'ensemble — c'est un pur changement
/// de repère, il ne déplace personne.
/// </summary>
public partial class ShipDemo
{
    /// <summary>La cible posée sur la carte, en mètres VRAIS du monde.</summary>
    (double X, double Z)? _jumpTarget;

    /// <summary>La charge en cours, en secondes ; négatif, aucune.</summary>
    double _jumpCharge = -1;

    /// <summary>Vrai pendant la charge — le HUD et le son peuvent s'y accrocher.</summary>
    public bool Jumping => _jumpCharge >= 0;

    /// <summary>
    /// Poser ou retirer la cible. Reposer au même endroit l'efface : c'est le
    /// geste qu'on fait sans y penser quand on veut annuler.
    /// </summary>
    void SetJumpTarget(double x, double z)
    {
        if (_jumpTarget is { } old && Math.Abs(old.X - x) < 200 && Math.Abs(old.Z - z) < 200)
        {
            _jumpTarget = null;
            if (Jumping) CancelJump("La cible est levée");
            else Say("Cible levée");
            return;
        }
        _jumpTarget = (x, z);
        /* ON JUGE LE POINT TOUT DE SUITE, pas au moment du saut. Poser une cible à
           terre et ne l'apprendre que vingt secondes plus tard, anneaux allumés,
           serait une punition pour une faute qu'on pouvait signaler aussitôt. */
        var v = Teleport.Check(_world!, x, z, _ship.Physics.Draft, _ship.Spec.L);
        var fix = _world!.Geo.Fix(x, z);
        Say(v == Teleport.Verdict.Bon
            ? $"Cible : {Geo.Format(fix.Lat, true)} {Geo.Format(fix.Lon, false)}"
            : Teleport.Say(v));
    }

    /// <summary>Armer le saut, ou l'interrompre s'il charge déjà.</summary>
    void ArmJump()
    {
        if (_inTitle || _ship == null || _world == null) return;
        if (Jumping) { CancelJump("Les anneaux retombent"); return; }
        if (_jumpTarget is not { } t) { Say(Teleport.Say(Teleport.Verdict.SansCible)); return; }

        var v = Teleport.Check(_world, t.X, t.Z, _ship.Physics.Draft, _ship.Spec.L);
        if (v != Teleport.Verdict.Bon) { Say(Teleport.Say(v)); return; }

        _jumpCharge = 0;
        /* LES ANNEAUX METTENT LA DURÉE DE LA CHARGE À S'ALLUMER : leur rampe EST
           la jauge. Rien à afficher, rien à tenir d'accord — ce qu'on voit tourner
           de plus en plus vite est exactement ce qui reste à attendre. */
        _ship.RingsRise = Teleport.Charge;
        _ship.RingsOrdered = true;
        Say(Teleport.Say(v));
        JournalLog("Les anneaux s'éveillent.");
    }

    void CancelJump(string mot)
    {
        _jumpCharge = -1;
        if (_ship != null) _ship.RingsOrdered = false;
        Say(mot);
    }

    /// <summary>
    /// Une image de charge. Appelée avec le pas de la simulation, comme tout ce
    /// qui compte du temps ici.
    /// </summary>
    void JumpTick(double dt)
    {
        if (!Jumping || _ship == null || _world == null) return;
        _jumpCharge += dt;

        /* LA CIBLE EST REJUGÉE PENDANT LA CHARGE. Elle ne bouge pas, mais la marée
           du moteur, un banc qui découvre ou un changement de région pourraient la
           rendre fausse entre l'ordre et le départ. Vingt secondes, c'est long. */
        if (_jumpTarget is not { } t) { CancelJump("La cible est levée"); return; }
        if (_jumpCharge < Teleport.Charge) return;

        var v = Teleport.Check(_world, t.X, t.Z, _ship.Physics.Draft, _ship.Spec.L);
        if (v != Teleport.Verdict.Bon) { CancelJump(Teleport.Say(v)); return; }
        DoJump(t.X, t.Z);
    }

    /// <summary>
    /// Le saut lui-même. La coque SEULE change de place — voir l'en-tête du
    /// fichier — et elle arrive stoppée : porter son erre d'un bout à l'autre de
    /// la carte n'aurait aucun sens, et arriver toute toile dehors dans un endroit
    /// qu'on ne connaît pas est la meilleure façon de s'y jeter à la côte.
    /// </summary>
    void DoJump(double x, double z)
    {
        var o = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        var from = TruePos();
        b.Pos = new Vec3d(x - o.X, b.Pos.Y, z - o.Z);
        b.Vel = Vec3d.Zero;
        b.AngVel = Vec3d.Zero;
        _ship.SyncTransform();
        _ship.Ctrl.SailsSet = false;
        _ship.Ctrl.Throttle = 0;
        /* L'ESTIME EST RECALÉE : on sait exactement où l'on arrive, puisqu'on a
           désigné le point soi-même. Sans cela le point estimé resterait au départ,
           avec toute l'erreur accumulée, et la carte mentirait d'un bout de mer. */
        _reck?.Fix(x, z);
        _jumpCharge = -1;
        _jumpTarget = null;
        _ship.RingsOrdered = false;

        double miles = Math.Sqrt((x - from.X) * (x - from.X) + (z - from.Z) * (z - from.Z)) / 1852;
        var fix = _world!.Geo.Fix(x, z);
        Say($"Le navire a sauté — {Geo.Format(fix.Lat, true)} {Geo.Format(fix.Lon, false)}");
        JournalLog(FormattableString.Invariant(
            $"Les anneaux nous ont portés à {miles:F0} milles, par {Geo.Format(fix.Lat, true)} {Geo.Format(fix.Lon, false)}."));
    }

    /// <summary>La cible, pour la carte — bleue si elle est bonne, barrée sinon.</summary>
    (double X, double Z, double R, string Name)? JumpPlace()
    {
        if (_jumpTarget is not { } t || _ship == null || _world == null) return null;
        var v = Teleport.Check(_world, t.X, t.Z, _ship.Physics.Draft, _ship.Spec.L);
        string nom = v != Teleport.Verdict.Bon ? "impossible"
                   : Jumping ? FormattableString.Invariant($"{Teleport.Charge - _jumpCharge:F0} s")
                   : "saut";
        return (t.X, t.Z, Math.Max(10, _ship.Spec.L * Teleport.Clearance), nom);
    }
}
