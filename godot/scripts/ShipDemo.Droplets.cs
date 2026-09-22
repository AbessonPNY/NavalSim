using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LES GOUTTES SUR L'OBJECTIF, au sortir de l'eau — demandé : « comme une
/// vitre ». Une passe posée sur l'image finie (droplets.gdshader), sur le même
/// calque que l'oculaire de la lunette et par le même tour : la texture d'écran.
///
/// La mouillure est posée à 1 tant que l'œil est SOUS la surface et sèche
/// ensuite en quelques secondes ; les perles s'effacent l'une après l'autre, les
/// plus fines d'abord, et les grosses finissent de glisser.
/// </summary>
public partial class ShipDemo
{
    ColorRect _dropRect = null!;
    double _wet, _sheet, _dropClock;
    /// <summary>Le temps que la nappe met à s'égoutter, en secondes.</summary>
    const double SheetIn = 0.5;
    /// <summary>Le temps de séchage, en secondes.</summary>
    const double DryIn = 7;

    void BuildDroplets(CanvasLayer layer)
    {
        _dropRect = new ColorRect { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _dropRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _dropRect.Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/droplets.gdshader") };
        layer.AddChild(_dropRect);
    }

    /// <summary>
    /// L'OBJECTIF ESSUYÉ D'UN COUP : on change de caméra, donc d'objectif — les
    /// perles de celui qui sortait de l'eau n'ont rien à faire devant la
    /// chambre du capitaine (signalé).
    /// </summary>
    void DryLens() { _wet = 0; _sheet = 0; _dropRect.Visible = false; }

    /// <summary>Une image : l'eau qui reste sur l'objectif, et qui sèche.</summary>
    void DropletTick(double dt, bool under)
    {
        _dropClock += dt;
        // sous l'eau, l'objectif est mouillé d'un coup ; dehors, il sèche
        _wet = under ? 1 : Math.Max(0, _wet - dt / DryIn);
        // la nappe : entière tant qu'on est dedans, égouttée en une demi-seconde dehors
        _sheet = under ? 1 : Math.Max(0, _sheet - dt / SheetIn);
        _dropRect.Visible = !under && (_wet > 0.001 || _sheet > 0.001);
        if (!_dropRect.Visible) return;
        var m = (ShaderMaterial)_dropRect.Material;
        m.SetShaderParameter("u_wet", (float)_wet);
        m.SetShaderParameter("u_sheet", (float)_sheet);
        m.SetShaderParameter("u_time", (float)_dropClock);
        var vp = GetViewport().GetVisibleRect().Size;
        m.SetShaderParameter("u_aspect", vp.Y > 0 ? vp.X / vp.Y : 1.777f);
    }
}
