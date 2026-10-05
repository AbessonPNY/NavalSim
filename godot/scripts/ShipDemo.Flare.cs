using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE REFLET DE LENTILLE, alimenté chaque image (LensFlareEffect) : où est le soleil
/// sur l'image, et combien en laisser passer. Rien sous l'eau (la passe sous-marine a
/// ses rais), rien la nuit, rien quand le soleil est derrière l'objectif.
/// </summary>
public partial class ShipDemo
{
    LensFlareEffect _flare = null!;

    void FlareTick(bool under)
    {
        float amount = _settings.LensFlare;
        var sd = _sky.Core.SunDir.ToGodot();
        if (amount <= 0 || under || sd.Y < -0.02f) { _flare.Enabled = false; return; }
        var eye = _cam.GlobalPosition;
        var far = eye + sd * 5000f;
        if (_cam.IsPositionBehind(far)) { _flare.Enabled = false; return; }
        var vp = GetViewport().GetVisibleRect().Size;
        var px = _cam.UnprojectPosition(far);
        var uv = new Vector2(px.X / vp.X, px.Y / vp.Y);
        // le soleil sort du cadre : le reflet s'éteint avec lui (on ne peut plus le sonder)
        float edge = Math.Min(Math.Min(uv.X, 1 - uv.X), Math.Min(uv.Y, 1 - uv.Y));
        if (edge <= 0) { _flare.Enabled = false; return; }
        /* CE QUI LE VOILE AVANT L'OBJECTIF : le ciel couvert (Sunlit, la part du soleil
           qui perce), la brume de surface, l'orage — et le soleil bas, rouge et éteint
           par l'épaisseur d'air, qui n'en fait presque plus. */
        double fog = _seaFog?.Amount ?? 0;
        double low = MathX.Smooth01((sd.Y + 0.02) / 0.12);
        double k = amount * _sky.Sunlit * (1 - 0.85 * fog) * (1 - _sky.Core.Storm) * low
                 * Math.Min(1.0, _sky.Core.SunIntensity) * MathX.Smooth01(edge / 0.04);
        if (k <= 0.002) { _flare.Enabled = false; return; }
        var c = _sky.Core.SunColor;
        double m = Math.Max(1e-6, Math.Max(c.R, Math.Max(c.G, c.B)));
        _flare.SunUv = uv;
        _flare.Strength = (float)k;
        _flare.SunColour = new Color((float)(c.R / m), (float)(c.G / m), (float)(c.B / m));
        _flare.Enabled = true;
    }
}
