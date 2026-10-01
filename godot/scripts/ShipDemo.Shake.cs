using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA CAMÉRA QUI TREMBLE quand une soute saute.
///
/// LA LUMIÈRE D'ABORD, LE COUP ENSUITE. L'éclair est instantané ; ce qui secoue
/// l'œil est l'onde de pression, qui voyage à la vitesse du son — comme le
/// fracas, que SoundNode retarde déjà de la même façon. À trois cents mètres on
/// voit sauter le navire, et une seconde plus tard la caméra prend le coup en
/// même temps que l'oreille. Une secousse immédiate se lirait comme un effet de
/// montage ; décalée, comme une distance.
///
/// FORTE, PUIS AMORTIE : une « secousse » montée d'un coup à son arrivée et qui
/// retombe en une seconde environ, rendue par le CARRÉ de son niveau — un choc
/// franc et une traîne courte, plutôt qu'un tremblement mou qui dure. Le
/// mouvement lui-même est une somme de sinus de fréquences sans commune mesure :
/// lisse, jamais périodique, et sans rien à allouer.
/// </summary>
public partial class ShipDemo
{
    /// <summary>La vitesse du son dans l'air, en m/s : le même retard que le fracas.</summary>
    const double SoundSpeed = 343;
    /// <summary>Au plus fort : trois degrés de travers et trente centimètres de saut.</summary>
    const double ShakeAngle = 0.052, ShakeShift = 0.30;
    /// <summary>Ce qu'elle perd par seconde, en part de son plein.</summary>
    const double ShakeDecay = 0.9;
    /* LE PALIER DE L'ONDE, en mètres : jusque-là elle secoue presque à plein.
       Quatre-vingts mètres donnent 0,57 à la caméra orbitale d'une Roter Löwe
       (une soixantaine de mètres) et 0,17 à quatre cents — réglé à l'œil, et
       c'est le nombre à toucher si elle secoue trop ou pas assez. */
    const double ShockNear = 80;

    double _trauma;
    readonly List<(double At, double Amp)> _shocks = new();
    Transform3D _shakeBase, _shakeLeft;
    bool _shaken;

    /// <summary>
    /// Une onde qui part de <paramref name="at"/> (repère local de la scène),
    /// <paramref name="after"/> secondes après maintenant. <paramref name="power"/> :
    /// 1 pour la soute d'un trois-mâts de trente mètres, vue de son bord.
    /// </summary>
    void Shock(Vec3d at, double power, double after = 0)
    {
        if (_cam == null) return;
        var c = _cam.GlobalPosition;
        double dx = at.X - c.X, dy = at.Y - c.Y, dz = at.Z - c.Z;
        double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        /* L'onde s'affaiblit comme l'inverse de la distance, avec un palier
           (ShockNear) : à bord, c'est la pleine secousse ; à un mille, plus rien
           qu'on sente — ce qu'on entend encore, mais qui ne secoue plus. */
        double amp = Math.Clamp(power * ShockNear / (d + ShockNear), 0, 1);
        if (amp < 0.03) return;
        _shocks.Add((_t + after + d / SoundSpeed, amp));
    }

    /// <summary>Une image de secousse, APRÈS que la vue a posé la caméra.</summary>
    void ShakeCamera(double dt)
    {
        for (int i = _shocks.Count - 1; i >= 0; i--)
            if (_shocks[i].At <= _t) { _trauma = Math.Min(1, Math.Max(_trauma, _shocks[i].Amp) + 0.15 * _shocks[i].Amp); _shocks.RemoveAt(i); }

        /* RENDRE CE QU'ON A PRIS, si la vue ne l'a pas reposée : toutes le font à
           chaque image, mais une secousse qui s'ajoute à elle-même partirait à la
           dérive au premier écran qui ne le ferait pas. */
        if (_shaken && _cam.Transform.IsEqualApprox(_shakeLeft)) _cam.Transform = _shakeBase;
        _shaken = false;
        if (_trauma <= 0) return;

        _trauma = Math.Max(0, _trauma - ShakeDecay * dt);
        double s = _trauma * _trauma;
        double t = _t;
        static double N(double t, double a, double b, double c) =>
            (Math.Sin(t * a) + 0.6 * Math.Sin(t * b + 1.3) + 0.35 * Math.Sin(t * c + 2.1)) / 1.95;

        /* À LA LUNETTE, l'angle se divise par le grossissement : trois degrés à
           cent fois, c'est cinq champs entiers, et l'on ne verrait plus rien que
           du flou. Ce qui reste — un vingtième de champ — se lit très bien. */
        double ang = ShakeAngle * s * (_glassUp ? 3.0 / _glassPower : 1.0);
        var rot = new Basis(Vector3.Up, (float)(ang * N(t, 37, 59, 83)))
                * new Basis(Vector3.Right, (float)(ang * N(t, 43, 67, 97)))
                * new Basis(Vector3.Back, (float)(0.6 * ang * N(t, 31, 53, 71)));
        var shift = new Vector3((float)N(t, 29, 47, 61), (float)N(t, 41, 63, 89), 0) * (float)(ShakeShift * s);

        _shakeBase = _cam.Transform;
        _cam.Transform = _shakeBase * new Transform3D(rot, shift);
        _shakeLeft = _cam.Transform;
        _shaken = true;
    }
}
