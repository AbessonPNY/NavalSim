
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NavalSim.Core;

// ---- la fiche telle qu'elle est ecrite sur le disque ------------------------
// Les noms suivent le JSON ; c'est ShipSpec qui en DERIVE ce que le solveur
// consomme, exactement comme du cote JavaScript.

public sealed class HullShape
{
    [JsonPropertyName("length")]         public double Length { get; set; }
    [JsonPropertyName("beam")]           public double Beam { get; set; }
    [JsonPropertyName("freeboardMid")]   public double FreeboardMid { get; set; }
    [JsonPropertyName("sheerBow")]       public double SheerBow { get; set; }
    [JsonPropertyName("sheerStern")]     public double SheerStern { get; set; }
    [JsonPropertyName("keelDepth")]      public double KeelDepth { get; set; }
    [JsonPropertyName("keelExtra")]      public double KeelExtra { get; set; }
    [JsonPropertyName("dragAft")]        public double DragAft { get; set; }
    [JsonPropertyName("forefootLift")]   public double ForefootLift { get; set; }
    [JsonPropertyName("counterLift")]    public double CounterLift { get; set; }
    [JsonPropertyName("waterlinePower")] public double WaterlinePower { get; set; }
    [JsonPropertyName("waterlineFull")]  public double WaterlineFull { get; set; }
    [JsonPropertyName("transomWidth")]   public double TransomWidth { get; set; }
    [JsonPropertyName("sectionTuck")]    public double SectionTuck { get; set; }
    [JsonPropertyName("sectionPower")]   public double SectionPower { get; set; }
}

public sealed class CogSpec
{
    [JsonPropertyName("yFracKeel")]   public double YFracKeel { get; set; }
    [JsonPropertyName("zFracLength")] public double ZFracLength { get; set; }
}

public sealed class HydroSpec
{
    [JsonPropertyName("resistance")]  public double Resistance { get; set; }
    [JsonPropertyName("lateralGrip")] public double LateralGrip { get; set; }
    [JsonPropertyName("lateralQuad")] public double LateralQuad { get; set; }
    [JsonPropertyName("heaveDamp")]   public double HeaveDamp { get; set; }
}

public sealed class EngineSpec
{
    [JsonPropertyName("topSpeed")]   public double TopSpeed { get; set; }
    [JsonPropertyName("sternPower")] public double? SternPower { get; set; }
}

public sealed class RudderSpec
{
    [JsonPropertyName("power")]     public double Power { get; set; }
    [JsonPropertyName("maxAngle")]  public double MaxAngle { get; set; }
    [JsonPropertyName("postZFrac")] public double PostZFrac { get; set; }
    [JsonPropertyName("postY")]     public double PostY { get; set; }
}

public sealed class MastSpec
{
    [JsonPropertyName("zFrac")]     public double ZFrac { get; set; }
    [JsonPropertyName("height")]    public double Height { get; set; }
    [JsonPropertyName("boom")]      public double Boom { get; set; }
    [JsonPropertyName("tackAbove")] public double TackAbove { get; set; }

    /// <summary>Position absolue, derivee de zFrac multiplie par L au chargement.</summary>
    [JsonIgnore] public double Z { get; set; }
}

public sealed class JibSpec
{
    [JsonPropertyName("footFrac")]  public double FootFrac { get; set; }
    [JsonPropertyName("tackAbove")] public double TackAbove { get; set; }
    [JsonPropertyName("clewAbove")] public double ClewAbove { get; set; }
    [JsonPropertyName("headDrop")]  public double HeadDrop { get; set; }
}

public sealed class RigSpec
{
    [JsonPropertyName("type")]     public string Type { get; set; } = "gaff";
    [JsonPropertyName("sailArea")] public double SailArea { get; set; }
    [JsonPropertyName("ceHeight")] public double CeHeight { get; set; }
    [JsonPropertyName("ceZ")]      public double CeZ { get; set; }
    [JsonPropertyName("maxSheet")] public double MaxSheet { get; set; }
    [JsonPropertyName("belly")]    public double Belly { get; set; }
    [JsonPropertyName("masts")]    public List<MastSpec> Masts { get; set; } = new();
    [JsonPropertyName("jib")]      public JibSpec? Jib { get; set; }
}

/// <summary>
/// Le modele importe, quand la fiche en designe un. Une fiche sans modele
/// (frigate, schooner) porte <c>null</c> et garde sa coque procedurale, batie
/// par <see cref="HullLines"/> -- c'est le repli, et il est ecrit avant l'asset
/// parce qu'un repli ecrit apres est un repli que personne ne regarde.
/// </summary>
public sealed class ModelSpec
{
    [JsonPropertyName("glb")]        public string Glb { get; set; } = "";
    /// <summary>Sur quel axe du modele court sa longueur.</summary>
    [JsonPropertyName("lengthAxis")] public string LengthAxis { get; set; } = "z";
    [JsonPropertyName("offset")]     public double[] Offset { get; set; } = { 0, 0, 0 };
    [JsonPropertyName("rotationY")]  public double RotationY { get; set; }
}

public sealed class ShipJson
{
    [JsonPropertyName("id")]   public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("note")] public string? Note { get; set; }
    [JsonPropertyName("hull")] public HullShape Hull { get; set; } = new();
    [JsonPropertyName("displacementTonnes")] public double DisplacementTonnes { get; set; }
    [JsonPropertyName("cog")]    public CogSpec Cog { get; set; } = new();
    [JsonPropertyName("hydro")]  public HydroSpec Hydro { get; set; } = new();
    [JsonPropertyName("engine")] public EngineSpec Engine { get; set; } = new();
    [JsonPropertyName("rudder")] public RudderSpec Rudder { get; set; } = new();
    [JsonPropertyName("rig")]    public RigSpec Rig { get; set; } = new();
    [JsonPropertyName("model")]  public ModelSpec? Model { get; set; }
}

/// <summary>
/// Un batiment, comme donnee.
///
/// Le JSON enonce ce qu'un architecte naval enonce : dimensions principales,
/// coefficients qui donnent leur forme aux lignes, un deplacement en tonneaux, un
/// plan de voilure. Tout ce dont le solveur a besoin en est DERIVE ici, de sorte
/// qu'un navire plus grand est plus grand a tous egards -- resistance, gouverne,
/// inertie -- sans un seul nombre regle a la main par batiment.
///
/// Les coefficients hydro sont par unite de SURFACE et non des forces absolues :
///   resistance  : N par (m/s) au carre par m2 de maitre-couple (bau x creux)
///   lateralGrip : N par (m/s) par m2 de plan lateral (longueur x creux)
/// C'est ce qui permet au meme jeu de valeurs de servir une goelette de 24 m et
/// une fregate de 60.
/// </summary>
public sealed class ShipSpec
{
    public ShipJson Raw { get; }
    public string Id { get; }
    public string Name { get; }
    public string Note { get; }

    public HullShape Hull { get; }
    public double L { get; }        // longueur
    public double B { get; }        // bau
    public double Keel { get; }     // creux de quille
    public double D { get; }        // creux sur quille : la hauteur que la grille couvre
    public double DeckMid { get; }

    public double MassKg { get; }
    public double Tonnes { get; }
    public Vec3d Cog { get; }

    // ---- hydrodynamique derivee ----
    public double Drag { get; }
    public double LateralLinear { get; }
    public double LateralQuad { get; }
    public double HeaveDamp { get; }

    public double TopSpeed { get; }
    public double MaxThrust { get; }
    public double SternPower { get; }

    public double RudderK { get; }
    public double RudderMax { get; }
    public double RudderZ { get; }
    public double RudderY { get; }

    public RigSpec Rig { get; }
    public double SailArea { get; }
    public double CeHeight { get; }
    public double CeZ { get; }
    public double MaxSheet { get; }
    public IReadOnlyList<MastSpec> Masts { get; }
    public double JibFootZ { get; }

    public ModelSpec? Model { get; }

    // renseigne une fois la grille de sondes batie
    public double HullVolume { get; private set; }
    public double FillFraction { get; private set; }

    public ShipSpec(ShipJson json)
    {
        Raw = json;
        Id = json.Id;
        Name = json.Name;
        Note = json.Note ?? "";

        var h = json.Hull;
        Hull = h;
        L = h.Length;
        B = h.Beam;
        Keel = h.KeelDepth;
        D = h.FreeboardMid + h.KeelDepth + h.KeelExtra;
        DeckMid = h.FreeboardMid;

        // le deplacement est l'ENTREE ; la fraction immergee en decoule
        MassKg = json.DisplacementTonnes * 1000.0;
        Tonnes = json.DisplacementTonnes;

        // centre de gravite, en fractions pour qu'il suive l'echelle du navire
        Cog = new Vec3d(0,
            json.Cog.YFracKeel * Keel,
            json.Cog.ZFracLength * L);

        double midshipArea = B * Keel;      // m2, fixe la resistance de route
        double lateralArea = L * Keel;      // m2, la prise de quille et le bras du gouvernail
        var hy = json.Hydro;
        Drag = hy.Resistance * midshipArea;
        LateralLinear = hy.LateralGrip * lateralArea;
        LateralQuad = hy.LateralQuad * lateralArea;
        HeaveDamp = hy.HeaveDamp;

        // la machine est dimensionnee sur sa vitesse annoncee contre cette resistance
        TopSpeed = json.Engine.TopSpeed;
        MaxThrust = Drag * TopSpeed * TopSpeed;

        /* Ce qu'elle peut lever en marche arriere, en fraction NEGATIVE de sa
           poussee. Une helice viree a l'envers travaille contre son propre remous
           et les avirons d'un navire ne valent pas ses voiles : aucun batiment ne
           recule aussi fort qu'il pousse. Borne, parce qu'une valeur positive ici
           la ferait avancer sur l'ordre « en arriere ». */
        double sp = json.Engine.SternPower ?? -0.6;
        SternPower = Math.Max(-1, Math.Min(0, sp));

        RudderK = json.Rudder.Power * lateralArea;
        RudderMax = json.Rudder.MaxAngle;
        RudderZ = json.Rudder.PostZFrac * L;
        RudderY = json.Rudder.PostY;

        var r = json.Rig;
        Rig = r;
        SailArea = r.SailArea;
        CeHeight = r.CeHeight;
        CeZ = r.CeZ;
        MaxSheet = r.MaxSheet;

        foreach (var m in r.Masts) m.Z = m.ZFrac * L;
        Masts = r.Masts;
        JibFootZ = r.Jib != null ? r.Jib.FootFrac * L : 0;

        Model = json.Model;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Lit une fiche depuis du texte JSON. Godot lit le fichier lui-meme
    /// (FileAccess), donc le noyau n'a pas a connaitre le systeme de fichiers.
    /// </summary>
    public static ShipSpec FromJson(string text)
    {
        var json = JsonSerializer.Deserialize<ShipJson>(text, JsonOpts)
                   ?? throw new InvalidDataException("fiche navire illisible");
        return new ShipSpec(json);
    }

    /// <summary>
    /// Appele une fois la grille de sondes batie et le vrai volume de coque connu.
    /// Un navire qui ne peut pas flotter sur son tonnage annonce est une erreur de
    /// FICHE et non de physique -- donc on le dit clairement plutot que de le
    /// laisser couler en silence.
    /// </summary>
    public double CheckFlotation(double hullVolume, double rho, Action<string>? warn = null)
    {
        double fill = MassKg / (rho * hullVolume);
        HullVolume = hullVolume;
        FillFraction = fill;
        if (fill >= 1)
            warn?.Invoke($"[{Id}] {Tonnes} t depasse la poussee de {hullVolume:F0} m3 de coque "
                       + "-- elle coulera. Reduire displacementTonnes.");
        else if (fill > 0.75)
            warn?.Invoke($"[{Id}] tres chargee : {fill * 100:F0} % du volume de coque deplace. "
                       + "Peu de reserve de flottabilite.");
        return fill;
    }
}
