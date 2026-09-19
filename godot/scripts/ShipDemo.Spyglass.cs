using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LA LUNETTE, sur L — spyglass.js. Deux choses faites à deux endroits.
///
/// Le GROSSISSEMENT est celui de la caméra : le monde est rendu à travers une
/// focale cent fois plus étroite, avec toutes ses passes — rien n'est agrandi
/// après coup, une coque à deux milles est dessinée avec le détail qu'elle a.
/// Le VERRE est une passe posée sur l'image finie (spyglass.gdshader).
///
/// La règle du grossissement est énoncée, pas réglée : le disque occupe 84 % de
/// la hauteur ; à l'œil nu l'écran fait 55°, le disque en couvre donc 46° ; à ×M
/// il doit tenir 46°/M du monde, la focale verticale vaut 55°/M — 0,55° à ×100.
///
/// Deux écarts voulus avec la page, qui n'a ni l'un ni l'autre : la profondeur
/// de champ et le flou de mouvement se coupent tant qu'elle est à l'œil. Réglée
/// pour l'œil nu, la première rendrait flou tout ce qu'on vise à trois
/// kilomètres ; à un demi-degré de champ, le second ferait d'un coup de visée un
/// trait sur tout l'écran.
/// </summary>
public partial class ShipDemo
{
    bool _glassUp;
    double _glassPower = 100, _glassYaw, _glassPitch, _glassClock;
    float _glassFov0;
    ColorRect _glassRect = null!;

    void BuildSpyglass()
    {
        // sous le HUD : la page pose ses instruments par-dessus l'oculaire
        var layer = new CanvasLayer { Layer = -1 };
        _glassRect = new ColorRect { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _glassRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/spyglass.gdshader") };
        _glassRect.Material = m;
        layer.AddChild(_glassRect);
        // les gouttes sur le même calque, posées APRÈS l'oculaire : elles sont sur le verre
        BuildDroplets(layer);
        AddChild(layer);
        // le verre se dessine pendant que le reste se charge : il ne coûte rien à la première visée
        System.Threading.Tasks.Task.Run(() => SpyglassGlass.Texture);
    }

    void ToggleSpyglass()
    {
        _glassUp = !_glassUp;
        if (_glassUp)
        {
            // elle vient à l'œil pointée là où l'on regardait déjà
            _glassFov0 = _cam.Fov;
            var d = -_cam.GlobalBasis.Z;
            _glassYaw = Math.Atan2(d.X, d.Z);
            _glassPitch = Math.Asin(Math.Clamp(d.Y, -1, 1));
            ((ShaderMaterial)_glassRect.Material).SetShaderParameter("u_glass", SpyglassGlass.Texture);
        }
        else
        {
            _cam.Projection = Camera3D.ProjectionType.Perspective;
            _cam.Fov = _glassFov0;
        }
        _glassRect.Visible = _glassUp;
        _dragging = false;
        ApplySettings();                  // les flous suivent
        Say(_glassUp ? $"Lunette ×{Math.Round(_glassPower)}" : "Lunette rangée");
    }

    /// <summary>
    /// Après que la vue a posé la caméra : garder sa position, prendre sa visée et
    /// sa focale. Aucune main ne tient une lunette immobile : deux sinus lents et
    /// un plus vif, quelques centièmes de degré — rien à l'œil nu, une
    /// respiration visible à ×100, et c'est ainsi qu'une lunette trahit son
    /// grossissement.
    /// </summary>
    void AimSpyglass(double dt)
    {
        if (!_glassUp) return;
        _glassClock += dt;
        double t = _glassClock, s = 0.00016;
        double wy = s * (Math.Sin(t * 0.9) + 0.6 * Math.Sin(t * 2.3 + 1.7) + 0.25 * Math.Sin(t * 7.1));
        double wp = s * (Math.Sin(t * 1.1 + 0.4) + 0.5 * Math.Sin(t * 2.9 + 2.1) + 0.25 * Math.Sin(t * 6.3));
        double y = _glassYaw + wy, p = _glassPitch + wp, cp = Math.Cos(p);
        var dir = new Vector3((float)(Math.Sin(y) * cp), (float)Math.Sin(p), (float)(Math.Cos(y) * cp));
        /* UN DEMI-DEGRÉ, QUE GODOT REFUSE : un Camera3D en perspective ne descend
           pas sous 1°. La même optique s'écrit en FRUSTUM — la hauteur du champ au
           plan proche, 2·près·tan(champ/2) —, qui n'a pas de borne. */
        double fov = 55 / _glassPower * Math.PI / 180;
        _cam.Projection = Camera3D.ProjectionType.Frustum;
        _cam.FrustumOffset = Vector2.Zero;
        _cam.Size = (float)(2 * _cam.Near * Math.Tan(fov / 2));
        _cam.LookAt(_cam.GlobalPosition + dir, Vector3.Up);
    }

    /* Le glisser vise, cent fois plus doucement à ×100 ; la molette est le tube,
       de ×10 à ×100. Rend vrai si la lunette a pris l'événement. */
    bool GlassMouse(InputEvent e)
    {
        if (!_glassUp) return false;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
            // un cran de molette de navigateur vaut cent : exp(∓0,12) par cran
            else if (mb.ButtonIndex == MouseButton.WheelUp) Tube(-100);
            else if (mb.ButtonIndex == MouseButton.WheelDown) Tube(100);
            return true;
        }
        if (e is InputEventMouseMotion mm)
        {
            if (!_dragging) return true;
            double k = 0.0045 * (10 / _glassPower);
            _glassYaw -= mm.Relative.X * k;
            _glassPitch = Math.Clamp(_glassPitch - mm.Relative.Y * k, -0.6, 0.6);
            return true;
        }
        return false;
    }

    void Tube(double deltaY)
    {
        _glassPower = Math.Clamp(_glassPower * Math.Exp(-deltaY * 0.0012), 10, 100);
        Say($"Lunette ×{Math.Round(_glassPower)}");
    }
}
