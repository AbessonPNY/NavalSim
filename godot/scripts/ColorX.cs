using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LES COULEURS ÉCRITES COMME LA PAGE LES ÉCRIT — « 0xccdcff » dans une fiche, ou
/// 0xccdcff dans le code : une couleur sRGB, huit bits par canal.
/// </summary>
public static class ColorX
{
    /// <summary>« 0xccdcff » ou « ccdcff » en entier.</summary>
    public static int ParseHex(string s) =>
        Convert.ToInt32(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, 16);

    /// <summary>0xRRGGBB en couleur (sRGB, comme elle est écrite).</summary>
    public static Color Rgb(int rgb) => Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    public static Color Rgb(uint rgb) => Rgb((int)rgb);

    /// <summary>« 0xccdcff » en couleur (sRGB).</summary>
    public static Color Hex(string s) => Rgb(ParseHex(s));
}
