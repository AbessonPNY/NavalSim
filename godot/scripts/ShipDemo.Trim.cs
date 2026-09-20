using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE CURSEUR D'ÉCOUTE — la barre de réglage de la page, portée ici.
///
/// Sans elle, un débutant lit « voiles établies », voit un nombre de kilonewtons
/// plausible, et n'apprend jamais que plusieurs fois cette force était à une
/// touche de distance : le modèle ne lui en dit rien d'autre. La page mesure
/// 7,4 kN contre 41,8 kN sur la frégate au plus près — un facteur six, invisible.
///
/// La barre montre OÙ EST l'écoute ; le repère doré montre où elle devrait être
/// pour pousser le plus fort au cap du moment. C'est le même repère que la barre
/// du gouvernail emploie pour l'ordre donné, et la même idée : l'instrument dit
/// l'état, la marque dit l'intention.
/// </summary>
public partial class ShipDemo : Node3D
{
    TrimGauge? _trim;

    void BuildTrim(CanvasLayer layer)
    {
        _trim = new TrimGauge(this) { Size = new Vector2(430, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(_trim);
    }

    /// <summary>Poser le curseur sous les instruments, et le taire quand ils se taisent.</summary>
    void TrimTick()
    {
        if (_trim == null) return;
        _trim.Visible = _info.Visible && !_inTitle;
        if (!_trim.Visible) return;
        _trim.Position = new Vector2(18, _info.Position.Y + _info.Size.Y + 8);
        _trim.QueueRedraw();
    }

    /// <summary>La hauteur que le curseur occupe : la ligne de quête se range dessous.</summary>
    float TrimHeight => _trim != null && _trim.Visible ? _trim.Size.Y + 6 : 0;

    sealed partial class TrimGauge : Control
    {
        readonly ShipDemo _d;
        public TrimGauge(ShipDemo d) { _d = d; }

        static readonly Color Track = new(0.16f, 0.19f, 0.22f, 0.85f);
        static readonly Color Fill = new(0.35f, 0.78f, 0.72f);
        static readonly Color Warn = new(0.95f, 0.62f, 0.24f);
        static readonly Color Mark = new(0.92f, 0.78f, 0.36f);
        static readonly Color Ink = new(0.90f, 0.92f, 0.95f);
        static readonly Color Dim = new(0.62f, 0.66f, 0.70f);

        public override void _Draw()
        {
            var p = _d._ship.Physics;
            var ctrl = _d._ship.Ctrl;
            double max = Math.Max(1e-6, _d._ship.Spec.MaxSheet);
            var font = ThemeDB.FallbackFont;

            float w = 190, h = 9, y = 9;
            DrawRect(new Rect2(0, y, w, h), Track);

            /* CE QU'ELLE PORTE, et non ce qu'on a demandé : ferlée, la barre est
               vide, ce qui est la vérité — il n'y a pas d'écoute à régler. */
            if (ctrl.SailsSet)
            {
                float frac = (float)Math.Clamp(ctrl.Sheet / max, 0, 1);
                DrawRect(new Rect2(0, y, w * frac, h), p.Luffing ? Warn : Fill);
            }

            // le repère du meilleur réglage, quand le vent permet d'en avoir un
            bool known = p.OptSheet != null && ctrl.SailsSet;
            if (known)
            {
                float mx = (float)Math.Clamp(p.OptSheet!.Value / max, 0, 1) * w;
                DrawRect(new Rect2(mx - 1, y - 4, 2, h + 8), Mark);
            }

            // et ce qu'il faut en faire, dit en clair
            double dTrim = known ? ctrl.Sheet - p.OptSheet!.Value : 0;
            string say = !ctrl.SailsSet ? "— voiles ferlées —"
                : p.Luffing ? "ça faseye — border !"
                : !known ? "— pas de vent —"
                : Math.Abs(dTrim) < 0.05
                    ? FormattableString.Invariant($"au mieux · {p.SailDrive / 1000:F0} kN")
                : dTrim > 0 ? "trop choquées — border"
                            : "trop bordées — choquer";
            var col = !ctrl.SailsSet || !known ? Dim
                    : p.Luffing ? Warn
                    : Math.Abs(dTrim) < 0.05 ? Fill : Ink;
            DrawString(font, new Vector2(w + 14, y + h), say, HorizontalAlignment.Left, -1, 14, col);
        }
    }
}
