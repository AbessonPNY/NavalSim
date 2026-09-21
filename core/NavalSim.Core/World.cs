using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NavalSim.Core;

/// <summary>Comment le gris devient des mètres — la section <c>relief</c> de la fiche.</summary>
public sealed class ReliefSpec
{
    public string Image = "";
    public double West, East, South, North;
    public double Sea = 128, MaxHeight = 1500, MaxDepth = 400, Curve = 2;
}

/// <summary>Un port de la fiche, tel qu'elle l'écrit (world/README.md).</summary>
public sealed class PortSpec
{
    public string Key = "", Name = "";
    public double Lat, Lon, Quay;
    public bool Start, Mole;
}

/// <summary>Un modèle posé sur la terre : world/assets.</summary>
public sealed class AssetSpec
{
    public string Name = "", Glb = "";
    public double Lat, Lon, Yaw, Scale = 1;
    public double? Y;
}

/// <summary>
/// Un lieu habité que la fiche déclare, là où il n'y a pas de port pour en
/// porter un — Kingston, sur la rive d'en face, n'a pas de ponton mais a une
/// ville. Un port, lui, bâtit la sienne tout seul.
/// </summary>
public sealed class TownSpec
{
    public string Key = "", Name = "";
    public double Lat, Lon, Radius = 600;
    public int Houses = 240;
}

/// <summary>
/// UN ATTERRAGE : où l'on arrive au large d'une région en venant d'ailleurs, et
/// d'où l'on en part. Un point d'eau libre, pas un port — une traversée finit en
/// vue de terre, et c'est au capitaine d'entrer.
/// </summary>
public sealed class ApproachSpec
{
    public string Name = "";
    public double Lat, Lon;
    /// <summary>Le cap, en degrés vrais, où la coque arrive : vers la terre.</summary>
    public double Heading;
}

/// <summary>La fiche de région entière : world/caraibes.json.</summary>
public sealed class RegionSpec
{
    /// <summary>Le nom du fichier sans son extension : c'est ce qu'une traversée désigne.</summary>
    public string Key = "";
    public string Name = "";
    public readonly List<ApproachSpec> Approaches = new();
    public double Scale = 0.1, Vertical = 0.25, HarbourDepth = 11;
    public double OriginLat = 17.9375, OriginLon = -76.8411;
    public ReliefSpec Relief = new();
    public readonly List<PortSpec> Ports = new();
    public readonly List<AssetSpec> Assets = new();
    public readonly List<TownSpec> Towns = new();

    public static RegionSpec FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var s = new RegionSpec();
        if (r.TryGetProperty("name", out var n)) s.Name = n.GetString() ?? "";
        if (r.TryGetProperty("scale", out var sc)) s.Scale = sc.GetDouble();
        if (r.TryGetProperty("vertical", out var v)) s.Vertical = v.GetDouble();
        if (r.TryGetProperty("harbourDepth", out var hd)) s.HarbourDepth = hd.GetDouble();
        if (r.TryGetProperty("origin", out var o))
        {
            if (o.TryGetProperty("lat", out var la)) s.OriginLat = la.GetDouble();
            if (o.TryGetProperty("lon", out var lo)) s.OriginLon = lo.GetDouble();
        }
        if (r.TryGetProperty("relief", out var e))
        {
            var E = s.Relief;
            if (e.TryGetProperty("image", out var im)) E.Image = im.GetString() ?? "";
            if (e.TryGetProperty("west", out var x)) E.West = x.GetDouble();
            if (e.TryGetProperty("east", out x)) E.East = x.GetDouble();
            if (e.TryGetProperty("south", out x)) E.South = x.GetDouble();
            if (e.TryGetProperty("north", out x)) E.North = x.GetDouble();
            if (e.TryGetProperty("sea", out x)) E.Sea = x.GetDouble();
            if (e.TryGetProperty("maxHeight", out x)) E.MaxHeight = x.GetDouble();
            if (e.TryGetProperty("maxDepth", out x)) E.MaxDepth = x.GetDouble();
            if (e.TryGetProperty("curve", out x)) E.Curve = x.GetDouble();
        }
        if (r.TryGetProperty("ports", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray())
                s.Ports.Add(new PortSpec
                {
                    Key = Str(p, "key"), Name = Str(p, "name"),
                    Lat = Num(p, "lat"), Lon = Num(p, "lon"), Quay = Num(p, "quay"),
                    Start = Bool(p, "start"), Mole = Bool(p, "mole")
                });
        if (r.TryGetProperty("towns", out var tw) && tw.ValueKind == JsonValueKind.Array)
            foreach (var t in tw.EnumerateArray())
                s.Towns.Add(new TownSpec
                {
                    Key = Str(t, "key"), Name = Str(t, "name"),
                    Lat = Num(t, "lat"), Lon = Num(t, "lon"),
                    Radius = t.TryGetProperty("radius", out var rr) ? rr.GetDouble() : 600,
                    Houses = t.TryGetProperty("houses", out var hh) ? hh.GetInt32() : 240
                });
        if (r.TryGetProperty("approaches", out var ap) && ap.ValueKind == JsonValueKind.Array)
            foreach (var a in ap.EnumerateArray())
                s.Approaches.Add(new ApproachSpec
                {
                    Name = Str(a, "name"), Lat = Num(a, "lat"), Lon = Num(a, "lon"), Heading = Num(a, "heading")
                });
        if (r.TryGetProperty("assets", out var az) && az.ValueKind == JsonValueKind.Array)
            foreach (var a in az.EnumerateArray())
                s.Assets.Add(new AssetSpec
                {
                    Name = Str(a, "name"), Glb = Str(a, "glb"),
                    Lat = Num(a, "lat"), Lon = Num(a, "lon"), Yaw = Num(a, "yaw"),
                    Scale = a.TryGetProperty("scale", out var k) ? k.GetDouble() : 1,
                    Y = a.TryGetProperty("y", out var y) ? y.GetDouble() : null
                });
        return s;

        static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        static double Num(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        static bool Bool(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
    }
}

/// <summary>Le bassin dragué autour d'un mouillage.</summary>
public readonly record struct Basin(double X, double Z, double R);

/// <summary>Le môle d'un port fermé : son anneau, sa passe, et le point abrité.</summary>
public readonly record struct Harbour(double Cx, double Cz, double R, double Wall,
                                      double Top, double Gap, double Ang, double Px, double Pz);

/// <summary>Les ouvrages d'un port : la racine du ponton, sa tête, son bassin, son môle.</summary>
public sealed class PortWorks
{
    public string Name = "";
    public double Ang, ShoreR, Reach, Sx, Sz, Hx, Hz;
    public Basin Basin;
    public Harbour? Harbour;
}

/// <summary>
/// Un port, sous la forme que le reste du jeu connaissait déjà sous le nom
/// d'<c>isles</c> : le marché, les rumeurs, les rencontres et les quêtes les
/// lisent sans rien changer.
/// </summary>
public sealed class Isle
{
    public string Key = "", Name = "";
    public double X, Z, R = 600, RShore;
    public bool Start;
    public double Lat, Lon;
    public PortWorks Port = new();
}

/// <summary>
/// LE MONDE HORS DU NAVIRE : où est la terre, et où elle est, elle.
///
/// Une vraie mer, LUE DANS UNE IMAGE. Les Caraïbes de 1690 — la Jamaïque et
/// Port-Royal, la côte sud de Cuba, Hispaniola et la Tortue, la Terre-Ferme de
/// Portobelo à Curaçao — dans leurs formes réelles, distances et tailles
/// divisées par dix.
///
/// Fonction PURE de la position, et en mètres VRAIS du monde, jamais dans les
/// coordonnées locales du rendu : l'origine flottante fait glisser le zéro local
/// à mesure qu'elle navigue. Le zéro du monde est Port-Royal.
///
/// Ce qu'une île répondait par son rayon — à quelle distance est le rivage, où
/// est-il — l'image le répond maintenant : <see cref="ShoreDistance"/> et
/// <see cref="NearestShore"/>.
///
/// C'est lui, l'<see cref="IGround"/> que le solveur attendait : la même
/// HeightAt sert au dessin de la côte, aux sondes du fond et à l'échouage, si
/// bien qu'elle ne peut pas talonner sur un haut-fond qu'on ne voit pas.
/// </summary>
public sealed class World : IGround
{
    public readonly RegionSpec Region;
    public readonly ReliefSpec Relief;
    public readonly Geo Geo;
    public readonly int ImgW, ImgH;
    readonly byte[] _img;

    /// <summary>Un navire de 2 000 tonneaux tire 6 à 8 m — voir <see cref="Dredge"/>.</summary>
    public readonly double HarbourDepth;
    /// <summary>La profondeur que veut dire « de l'eau profonde ».</summary>
    public const double Deep = 70;

    /// <summary>Mètres de jeu par pixel : le champ de distance et les carreaux de terre s'en servent.</summary>
    public readonly double Px;

    readonly double[] _lut = new double[256];
    public readonly List<Isle> Isles = new();
    public double LongestLeg { get; private set; }
    /// <summary>Jusqu'où la carte peut s'ouvrir : toute l'image depuis son milieu.</summary>
    public double Extent { get; private set; }

    // le champ de distance signé, au quart de la résolution de l'image
    int _dfW, _dfH;
    const int DfK = 4;
    float[] _df = Array.Empty<float>();

    /// <param name="grey">L'image, un octet par pixel, lignes de haut en bas.</param>
    /// <param name="warn">Ce qui se dit à la console quand un port n'a pas de rivage.</param>
    public World(RegionSpec region, int w, int h, byte[] grey, Action<string>? warn = null)
    {
        Region = region;
        Relief = region.Relief;
        _img = grey; ImgW = w; ImgH = h;
        HarbourDepth = region.HarbourDepth;
        Geo = new Geo(region.OriginLat, region.OriginLon, region.Scale);

        Px = (Relief.North - Relief.South) * Core.Geo.MPerMin * 60 * region.Scale / h;
        for (int v = 0; v < 256; v++) _lut[v] = Decode(v);
        BuildDistanceField();

        foreach (var p in region.Ports)
        {
            var isle = MakePort(p, warn);
            if (isle != null) Isles.Add(isle);
        }
        Measure();
    }

    /// <summary>Le gris BRUT d'un pixel — ce que la carte marine relit pour se dessiner.</summary>
    public byte GreyByte(int i, int j) =>
        _img[Math.Clamp(j, 0, ImgH - 1) * ImgW + Math.Clamp(i, 0, ImgW - 1)];

    /// <summary>La hauteur qu'un gris ENTIER vaut, prise dans la table — pour la carte.</summary>
    public double Lut(int v) => _lut[v < 0 ? 0 : v > 255 ? 255 : v];

    /// <summary>
    /// LE GRIS EN MÈTRES, la seule formule — tools/region-heightmap.js écrit avec
    /// son inverse. La hauteur va comme le carré de l'écart au gris du rivage :
    /// les premiers mètres, ceux qui échouent un navire, ont beaucoup de nuances,
    /// et les sommets peu.
    /// </summary>
    public double Decode(double v)
    {
        var E = Relief;
        if (v >= E.Sea) return E.MaxHeight * Math.Pow((v - E.Sea) / (255 - E.Sea), E.Curve);
        return -E.MaxDepth * Math.Pow((E.Sea - v) / E.Sea, E.Curve);
    }

    /// <summary>Des mètres du monde aux pixels de l'image (fractionnaires, centres à .5).</summary>
    public (double I, double J) PixelAt(double x, double z)
    {
        var g = Geo.Fix(x, z);
        var E = Relief;
        return ((g.Lon - E.West) / (E.East - E.West) * ImgW,
                (E.North - g.Lat) / (E.North - E.South) * ImgH);
    }

    /// <summary>Le gris en un point, bilinéaire entre centres de pixels. Hors de l'image, la haute mer.</summary>
    public double Grey(double x, double z)
    {
        var (pi, pj) = PixelAt(x, z);
        double fi = pi - 0.5, fj = pj - 0.5;
        int i = (int)Math.Floor(fi), j = (int)Math.Floor(fj);
        if (i < 0 || j < 0 || i >= ImgW - 1 || j >= ImgH - 1) return 0;
        double a = fi - i, b = fj - j;
        int k = j * ImgW + i;
        return (_img[k] * (1 - a) + _img[k + 1] * a) * (1 - b)
             + (_img[k + ImgW] * (1 - a) + _img[k + ImgW + 1] * a) * b;
    }

    /// <summary>
    /// La hauteur de la terre en un point, en mètres au-dessus du niveau de la
    /// mer. Négative au large, si bien que la même fonction sert au rivage, aux
    /// hauts-fonds et à qui veut savoir s'il flotte encore ici. Les ouvrages du
    /// port en font partie — LE MÔLE EST DE LA TERRE : un mur qui est de la terre
    /// est un mur que l'échouage connaît déjà.
    /// </summary>
    public double HeightAt(double x, double z) => Mole(x, z, Dredge(x, z, IslandHeight(x, z)));

    /// <summary>Le relief seul, ouvrages exclus — ce contre quoi un môle se mesure.</summary>
    public double IslandHeight(double x, double z) => Decode(Grey(x, z));

    /// <summary>
    /// LE BASSIN EST PROFOND JUSQU'AU QUAI. Autour d'un mouillage le fond est
    /// tenu à <see cref="HarbourDepth"/>, et il rencontre le rivage en mur de
    /// quai : la profondeur est atteinte dès qu'il y a un mètre d'eau, plutôt
    /// qu'au bas d'une plage. La terre n'est jamais creusée. Lu par
    /// <see cref="HeightAt"/>, donc les sondes, les pieds du ponton et le fond
    /// voient tous le même plancher. (Port-Royal était exactement cela : de
    /// l'eau profonde tout contre, des navires de toute taille à quai.)
    /// </summary>
    double Dredge(double x, double z, double h)
    {
        if (h >= 0) return h;
        foreach (var isl in Isles)
        {
            var B = isl.Port.Basin;
            if (B.R <= 0 || Math.Sqrt((x - B.X) * (x - B.X) + (z - B.Z) * (z - B.Z)) > B.R) continue;
            double u = Math.Min(1, -h / 1.0);
            h = Math.Min(h, -HarbourDepth * u * u * (3 - 2 * u));
        }
        return h;
    }

    /// <summary>
    /// Le mur d'enceinte d'un port qui en demande un (<c>"mole": true</c>), et sa
    /// passe. Un port naturel — Kingston derrière les Palisadoes — n'en a pas.
    /// </summary>
    double Mole(double x, double z, double h)
    {
        foreach (var isl in Isles)
        {
            if (isl.Port.Harbour is not Harbour H) continue;
            double dx = x - H.Cx, dz = z - H.Cz;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < H.R || d > H.R + H.Wall) continue;
            double a = Math.Atan2(dz, dx) - H.Ang;
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            if (Math.Abs(a) < H.Gap) continue;                 // la passe
            double t = Math.Min(1, (Math.Abs(a) - H.Gap) / 0.10);
            if (h < H.Top * t) h = H.Top * t;
        }
        return h;
    }

    /// <summary>
    /// CE QUI RESTE DE LA MER derrière un môle, de zéro à un — le jumeau de
    /// Naval.SHELTER_GLSL, que lisent le shader de la mer, cet échantillonneur et
    /// la passe d'écume. Seuls les ports à môle abritent quelque chose.
    /// </summary>
    public double Shelter(double x, double z)
    {
        double f = 1;
        foreach (var isl in Isles)
        {
            if (isl.Port.Harbour is not Harbour H) continue;
            double d = Math.Sqrt((x - H.Cx) * (x - H.Cx) + (z - H.Cz) * (z - H.Cz));
            if (d > H.R + H.Wall) continue;
            double dp = Math.Sqrt((x - H.Px) * (x - H.Px) + (z - H.Pz) * (z - H.Pz));
            double u = Math.Min(1, dp / (1.6 * H.R));
            double s = 1 - u * u * (3 - 2 * u) * 0.88;
            if (s < f) f = s;
        }
        return f;
    }

    /// <summary>Y a-t-il assez d'eau ici pour une coque qui tire <paramref name="draft"/> mètres ?</summary>
    public bool Navigable(double x, double z, double draft) => HeightAt(x, z) < -(draft + 1.5);

    /// <summary>
    /// À QUELLE DISTANCE EST LE RIVAGE, négative à terre — pour les mouettes, les
    /// dauphins, les rencontres. Un champ de distance signé, calculé une fois
    /// depuis l'image au quart de sa résolution (mailles de 180 m au départ :
    /// largement assez pour « à moins de deux kilomètres d'une côte », pas pour
    /// un mouillage, qui lit <see cref="HeightAt"/>).
    /// </summary>
    void BuildDistanceField()
    {
        int w = (int)Math.Ceiling((double)ImgW / DfK), h = (int)Math.Ceiling((double)ImgH / DfK);
        var landAt = new byte[w * h];
        var seaAt = new byte[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                int pi = Math.Min(ImgW - 1, i * DfK + (DfK >> 1)), pj = Math.Min(ImgH - 1, j * DfK + (DfK >> 1));
                bool l = _img[pj * ImgW + pi] >= Relief.Sea;
                landAt[j * w + i] = (byte)(l ? 1 : 0);
                seaAt[j * w + i] = (byte)(l ? 0 : 1);
            }
        var toSea = Edt(seaAt, w, h);
        var toLand = Edt(landAt, w, h);
        double cell = Px * DfK;
        var sd = new float[w * h];
        for (int k = 0; k < w * h; k++)
            sd[k] = (float)(landAt[k] != 0 ? -(Math.Sqrt(toSea[k]) - 0.5) * cell
                                           : (Math.Sqrt(toLand[k]) - 0.5) * cell);
        _dfW = w; _dfH = h; _df = sd;
    }

    public double ShoreDistance(double x, double z)
    {
        var (pi, pj) = PixelAt(x, z);
        double fi = pi / DfK - 0.5, fj = pj / DfK - 0.5;
        int i = Math.Max(0, Math.Min(_dfW - 2, (int)Math.Floor(fi)));
        int j = Math.Max(0, Math.Min(_dfH - 2, (int)Math.Floor(fj)));
        double a = Math.Max(0, Math.Min(1, fi - i)), b = Math.Max(0, Math.Min(1, fj - j));
        int k = j * _dfW + i;
        double v = (_df[k] * (1 - a) + _df[k + 1] * a) * (1 - b)
                 + (_df[k + _dfW] * (1 - a) + _df[k + _dfW + 1] * a) * b;
        // hors de l'image : aussi loin que le bord le dit, et plus loin encore
        double outv = Math.Max(Math.Max(0, -Math.Min(0, fi)),
                      Math.Max(fi - (_dfW - 1), Math.Max(-Math.Min(0, fj), fj - (_dfH - 1))));
        return v + outv * DfK * Px;
    }

    /// <summary>
    /// Le point de rivage le plus proche, en descendant le champ de distance : où
    /// une volée de mouettes a son perchoir, où la cargaison d'une épave échoue.
    /// </summary>
    public (double X, double Z) NearestShore(double x, double z)
    {
        double px = x, pz = z;
        for (int n = 0; n < 6; n++)
        {
            double d = ShoreDistance(px, pz), e = 30;
            double gx = ShoreDistance(px + e, pz) - ShoreDistance(px - e, pz);
            double gz = ShoreDistance(px, pz + e) - ShoreDistance(px, pz - e);
            double l = Math.Sqrt(gx * gx + gz * gz);
            if (l == 0) l = 1;
            px -= gx / l * d; pz -= gz / l * d;
            if (Math.Abs(d) < 20) break;
        }
        return (px, pz);
    }

    /// <summary>
    /// OÙ EST LE PORT : la fiche donne un lieu et le relèvement que regarde le
    /// quai. Le rivage est cherché le long de ce relèvement, et le ponton y court
    /// jusqu'à ce qu'il y ait assez d'eau sous lui et PAS PLUS LOIN — c'est une
    /// jetée, pas une chaussée.
    ///
    /// Neuf mètres à la tête, et chaque borne a été trouvée à l'essai : une coque
    /// est À COUPLE, donc en deçà de la tête, et une houle significative de 1,6 m
    /// fait descendre une coque amarrée d'un bon mètre sous son tirant moyen
    /// plusieurs fois par minute. Un tirant d'eau statique n'est pas une garde.
    /// </summary>
    Isle? MakePort(PortSpec P, Action<string>? warn)
    {
        var G = Geo.ToXZ(P.Lat, P.Lon);
        double b = P.Quay * Math.PI / 180;
        double dx = -Math.Sin(b), dz = Math.Cos(b);                 // l'est est −x
        double At(double s) => IslandHeight(G.X + dx * s, G.Z + dz * s);

        double? s0 = null;
        if (At(0) >= 0)
        {
            for (double s = 0; s < 3000; s += 3) if (At(s) < 0) { s0 = s; break; }
        }
        else
        {
            for (double s = 0; s > -3000; s -= 3) if (At(s) >= 0) { s0 = s + 3; break; }
        }
        if (s0 == null)
        {
            warn?.Invoke($"[monde] {P.Name} : pas de rivage à 3 km dans le relèvement {P.Quay}° — port ignoré");
            return null;
        }

        double x = G.X + dx * s0.Value, z = G.Z + dz * s0.Value;    // le rivage, racine du ponton
        double reach = 20;
        for (double s = 20; s <= 150; s += 2)
        {
            reach = s;
            if (IslandHeight(x + dx * s, z + dz * s) <= -9) break;
        }
        double ang = Math.Atan2(dz, dx);
        var port = new PortWorks
        {
            Name = P.Name, Ang = ang, ShoreR = 0, Reach = reach,
            Sx = x - dx * 6, Sz = z - dz * 6,
            Hx = x + dx * reach, Hz = z + dz * reach,
            Basin = new Basin(x + dx * reach * 0.5, z + dz * reach * 0.5, Math.Max(180, reach + 90))
        };
        if (P.Mole)
        {
            double Rb = 170, gap = Math.Asin(Math.Min(0.95, 65 / Rb));
            double cx = x + dx * Rb * 0.80, cz = z + dz * Rb * 0.80;
            port.Harbour = new Harbour(cx, cz, Rb, 18, 3.4, gap, ang,
                                       cx + dx * (Rb + 9), cz + dz * (Rb + 9));
        }
        return new Isle
        {
            Key = P.Key, Name = P.Name, X = x, Z = z, R = 600, RShore = 0,
            Start = P.Start, Lat = P.Lat, Lon = P.Lon, Port = port
        };
    }

    void Measure()
    {
        LongestLeg = 0;
        for (int i = 0; i < Isles.Count; i++)
            for (int j = i + 1; j < Isles.Count; j++)
            {
                double dx = Isles[i].X - Isles[j].X, dz = Isles[i].Z - Isles[j].Z;
                LongestLeg = Math.Max(LongestLeg, Math.Sqrt(dx * dx + dz * dz));
            }
        var E = Relief;
        var a = Geo.ToXZ(E.North, E.West);
        var c = Geo.ToXZ(E.South, E.East);
        Extent = Math.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Z - c.Z) * (a.Z - c.Z)) / 2;
    }

    public Isle? ByKey(string key) => Isles.Find(i => i.Key == key);
    public Isle? StartPort => Isles.Find(i => i.Start) ?? (Isles.Count > 0 ? Isles[0] : null);

    /// <summary>Les ports à portée d'un point du monde.</summary>
    public List<Isle> Near(double x, double z, double range)
    {
        var outp = new List<Isle>();
        foreach (var isl in Isles)
        {
            double dx = isl.X - x, dz = isl.Z - z;
            if (Math.Sqrt(dx * dx + dz * dz) < range + isl.R) outp.Add(isl);
        }
        return outp;
    }

    /// <summary>
    /// Distance euclidienne au carré EXACTE jusqu'à la cellule marquée la plus
    /// proche (Felzenszwalb et Huttenlocher), en cellules. Deux passes
    /// séparables, chacune une enveloppe inférieure de paraboles : c'est ce qui
    /// la rend linéaire, là où une propagation par vagues serait approchée.
    /// </summary>
    public static double[] Edt(byte[] target, int W, int H)
    {
        const double INF = 1e20;
        int n = Math.Max(W, H);
        var f = new double[n];
        var d = new double[n];
        var z = new double[n + 1];
        var v = new int[n];
        var outv = new double[W * H];
        for (int k = 0; k < W * H; k++) outv[k] = target[k] != 0 ? 0 : INF;

        void Pass(int len, int off, int stride)
        {
            for (int q = 0; q < len; q++) f[q] = outv[off + q * stride];
            int k = 0; v[0] = 0; z[0] = -INF; z[1] = INF;
            for (int q = 1; q < len; q++)
            {
                double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = INF;
            }
            k = 0;
            for (int q = 0; q < len; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
            }
            for (int q = 0; q < len; q++) outv[off + q * stride] = d[q];
        }

        for (int i = 0; i < W; i++) Pass(H, i, W);
        for (int j = 0; j < H; j++) Pass(W, j * W, 1);
        return outv;
    }
}
