using Godot;

namespace NavalSim;

/// <summary>
/// CE QUE LES PANNEAUX ONT EN COMMUN — l'étiquette (texte, taille, couleur, place)
/// et le cadre des panneaux du bord droit. Le chantier, le comptoir, la cale, la
/// flotte et le menu se construisaient chacun les leurs.
/// </summary>
public partial class ShipDemo
{
    static readonly StringName FontSize = "font_size", FontColor = "font_color";

    /// <summary>
    /// Une étiquette. <paramref name="expand"/> la laisse prendre la place libre de
    /// sa rangée ; <paramref name="minW"/> lui en réserve une ; alignée à gauche,
    /// à droite (<paramref name="right"/>) ou au centre (<paramref name="centre"/>).
    /// </summary>
    static Label MkLabel(string text, int size, Color col, bool expand = false, float minW = 0,
                         bool right = false, bool centre = false)
    {
        var l = new Label
        {
            Text = text,
            SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
            HorizontalAlignment = centre ? HorizontalAlignment.Center : right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(minW, 0)
        };
        l.AddThemeFontSizeOverride(FontSize, size);
        l.AddThemeColorOverride(FontColor, col);
        return l;
    }

    /// <summary>Le cadre des panneaux posés à droite de l'écran : fond d'encre, filet d'or.</summary>
    static StyleBoxFlat SidePanelStyle() => new()
    {
        BgColor = new Color(0.04f, 0.06f, 0.09f, 0.78f),
        BorderColor = new Color(0.55f, 0.44f, 0.20f, 0.8f),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 12
    };
}
