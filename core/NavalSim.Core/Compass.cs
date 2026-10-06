namespace NavalSim.Core;

/// <summary>
/// LE CAP AU COMPAS ET LE LACET DU JEU, dans un sens et dans l'autre. Le nord est
/// +z et l'EST EST −x (CLAUDE.md, « Repères ») : une inversion de signe ici
/// retournerait la carte entière. La formule était réécrite partout où l'on lit
/// un cap ou en pose un ; elle n'est plus qu'ici.
/// </summary>
public static class Compass
{
    /// <summary>Le cap en degrés, de 0 à 360, lu sur le vecteur d'étrave (pas sur des angles d'Euler).</summary>
    public static double HeadingDeg(Vec3d fwd) => (Math.Atan2(-fwd.X, fwd.Z) * 180 / Math.PI + 360) % 360;

    /// <summary>Le lacet (radians, autour de +y) d'une coque qui porte ce cap.</summary>
    public static double YawOf(double headingDeg) => -headingDeg * Math.PI / 180;

    /// <summary>Le relèvement vrai de B vu de A, en degrés de 0 à 360 (mètres du monde).</summary>
    public static double BearingDeg(double ax, double az, double bx, double bz)
        => (Math.Atan2(-(bx - ax), bz - az) * 180 / Math.PI + 360) % 360;

    /* LES TRENTE-DEUX QUARTS, comme un pilote de 1690 les disait : on ne gouvernait
       pas au degré mais au quart (11° 1/4). Les huit vents, les huit demi-vents,
       puis les seize quarts, chacun nommé d'après le vent principal le plus proche
       — « nord-est quart est », non « est-nord-est quart nord ». */
    static readonly string[] Abbr =
    {
        "N", "N ¼ NE", "NNE", "NE ¼ N", "NE", "NE ¼ E", "ENE", "E ¼ NE",
        "E", "E ¼ SE", "ESE", "SE ¼ E", "SE", "SE ¼ S", "SSE", "S ¼ SE",
        "S", "S ¼ SO", "SSO", "SO ¼ S", "SO", "SO ¼ O", "OSO", "O ¼ SO",
        "O", "O ¼ NO", "ONO", "NO ¼ O", "NO", "NO ¼ N", "NNO", "N ¼ NO"
    };

    /// <summary>Le quart le plus proche de ce cap, de 0 (nord) à 31.</summary>
    public static int Point(double headingDeg) => (int)System.Math.Round(((headingDeg % 360 + 360) % 360) / 11.25) % 32;

    /// <summary>Le quart abrégé (« NE ¼ E »).</summary>
    public static string RumbShort(double headingDeg) => Abbr[Point(headingDeg)];

    /// <summary>Le quart en toutes lettres (« nord-est quart est »).</summary>
    public static string Rumb(double headingDeg)
    {
        string s = Abbr[Point(headingDeg)].Replace(" ¼ ", " quart ");
        var words = s.Split(' ');
        for (int i = 0; i < words.Length; i++)
            if (words[i] != "quart") words[i] = Spell(words[i]);
        return string.Join(" ", words);
    }

    static string Spell(string w)
    {
        // lettre à lettre : « NNE » se dit nord-nord-est, « SO » sud-ouest
        var parts = new System.Collections.Generic.List<string>();
        foreach (char c in w)
            parts.Add(c switch { 'N' => "nord", 'S' => "sud", 'E' => "est", 'O' => "ouest", _ => c.ToString() });
        return string.Join("-", parts);
    }

    /// <summary>Le cap en quadrant, « N 61° E », comme on le lit sur une rose graduée.</summary>
    public static string Quadrantal(double headingDeg)
    {
        double h = (headingDeg % 360 + 360) % 360;
        string ns = h <= 90 || h >= 270 ? "N" : "S";
        string eo = h < 180 ? "E" : "O";
        double a = ns == "N" ? (h <= 90 ? h : 360 - h) : System.Math.Abs(180 - h);
        int d = (int)System.Math.Round(a);
        if (d == 0) return ns;
        if (d == 90) return eo;
        return $"{ns} {d}° {eo}";
    }
}
