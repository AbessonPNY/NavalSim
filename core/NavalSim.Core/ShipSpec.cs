
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
    [JsonPropertyName("speedLimit")] public double? SpeedLimit { get; set; }
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
    /// <summary>Gréement carré : la hauteur de chaque vergue, en fraction du mât, de bas en haut.</summary>
    [JsonPropertyName("yards")]     public List<double> Yards { get; set; } = new();
    /// <summary>L'envergure de la basse vergue, en fraction du bau.</summary>
    [JsonPropertyName("yardSpan")]  public double YardSpan { get; set; }

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
    [JsonPropertyName("minSheet")] public double? MinSheet { get; set; }
    [JsonPropertyName("belly")]    public double Belly { get; set; }
    [JsonPropertyName("masts")]    public List<MastSpec> Masts { get; set; } = new();
    [JsonPropertyName("jib")]      public JibSpec? Jib { get; set; }
    [JsonPropertyName("lateen")]   public LateenSpec? Lateen { get; set; }
}

/// <summary>
/// La voile latine d'artimon : une voile EN LONG, prise sur la voilure totale.
/// Sa surface, son centre de voilure (hauteur et station, comme ceux du
/// gréement), et jusqu'où l'équipage peut l'écarter de l'axe (radians).
/// </summary>
public sealed class LateenSpec
{
    [JsonPropertyName("area")]     public double Area { get; set; }
    [JsonPropertyName("ceHeight")] public double CeHeight { get; set; }
    [JsonPropertyName("ceZ")]      public double CeZ { get; set; }
    [JsonPropertyName("maxSheet")] public double? MaxSheet { get; set; }
}

/// <summary>
/// Le modele importe, quand la fiche en designe un. Une fiche sans modele
/// (schooner, chaloupe) porte <c>null</c> -- ou un bloc model sans glb -- et garde sa coque procedurale, batie
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
    /// <summary>
    /// L'echelle imposee. Absente, le modele est ramene a la longueur que le
    /// solveur fait flotter, mesuree sur sa COQUE et non sur l'objet entier.
    /// </summary>
    [JsonPropertyName("scale")]      public double? Scale { get; set; }
    /// <summary>
    /// Ce que la fiche tient hors du gréement, par morceau de nom : une pièce qui
    /// a la forme d'un espar sans en être un (une fenêtre de château, un fanal).
    /// </summary>
    [JsonPropertyName("rigIgnore")]  public List<string> RigIgnore { get; set; } = new();
    /// <summary>La force des fenêtres de nuit : 1 par défaut, 0 pour ne rien allumer.</summary>
    [JsonPropertyName("nightGlow")]  public double? NightGlow { get; set; }
    /// <summary>Le gain de pente du relief tiré de la rugosité (voir ReliefMap) ; absent, aucun.</summary>
    [JsonPropertyName("relief")]     public double? Relief { get; set; }
    /// <summary>
    /// CE QUI EST DEDANS — des morceaux de nom de maillage, sans égard à la casse.
    /// Ce qui correspond est à l'INTÉRIEUR de la coque, et les feux du pont ne
    /// l'éclairent pas : un fanal est dehors, et ce qui est dehors n'entre pas.
    ///
    /// Le moteur ne peut pas le deviner. Une coque est une boîte ouverte dont
    /// l'intérieur est modelé vu du dedans, si bien qu'une cloison présente ses
    /// dos aux lumières du dehors et ne leur fait aucune ombre ; et une vitre de
    /// poupe, qui ne porte volontairement pas d'ombre pour que le soleil entre,
    /// laisse aussi entrer le fanal pendu deux mètres derrière. On le DIT donc,
    /// plutôt que de multiplier les cartes d'ombres.
    ///
    /// Absente, la liste vaut « cabine, cabin, chambre, bureau », ce qui couvre
    /// les modèles écrits jusqu'ici. Une liste VIDE veut dire « rien n'est
    /// dedans », et le bord entier reprend la lumière des fanaux.
    /// </summary>
    [JsonPropertyName("inside")]     public string[]? Inside { get; set; }
}

/// <summary>
/// Un feu du bord. Une fiche en nomme autant qu'elle en porte, chacun à sa place
/// dans SON repère : x/z en mètres ou xFrac/zFrac en fractions du bau et de la
/// longueur ; y absent veut dire « sur le pont à cette station ». Une liste VIDE
/// veut dire aucun feu ; pas de liste, le feu de poupe par défaut.
/// </summary>
public sealed class LanternSpec
{
    [JsonPropertyName("x")]     public double? X { get; set; }
    [JsonPropertyName("xFrac")] public double? XFrac { get; set; }
    [JsonPropertyName("y")]     public double? Y { get; set; }
    [JsonPropertyName("z")]     public double? Z { get; set; }
    [JsonPropertyName("zFrac")] public double? ZFrac { get; set; }
    [JsonPropertyName("above")] public double Above { get; set; }
    [JsonPropertyName("size")]  public double? Size { get; set; }
    /// <summary>« 0xffb24a » ou un nombre, comme la page l'accepte.</summary>
    [JsonPropertyName("color")] public JsonElement? Color { get; set; }
    /// <summary>« candle » : une bougie, sans repère de loin.</summary>
    [JsonPropertyName("kind")]  public string? Kind { get; set; }
    /// <summary>Le nom d'un objet du .glb où la pendre (pendule : pas encore porté).</summary>
    [JsonPropertyName("hang")]  public string? Hang { get; set; }
}

/// <summary>
/// Un point de vue DEPUIS le navire (camera.decks) : l'œil dans son repère —
/// x/z en mètres ou xFrac/zFrac en fractions du bau et de la longueur, y en mètres
/// au-dessus de la flottaison —, puis où il regarde (yaw en degrés, 0 l'étrave, 180
/// la poupe, 90 bâbord ; pitch en degrés), sa focale et son plan proche.
/// </summary>
public sealed class DeckView
{
    [JsonPropertyName("name")]  public string Name { get; set; } = "À bord";
    [JsonPropertyName("x")]     public double? X { get; set; }
    [JsonPropertyName("xFrac")] public double? XFrac { get; set; }
    [JsonPropertyName("y")]     public double? Y { get; set; }
    [JsonPropertyName("z")]     public double? Z { get; set; }
    [JsonPropertyName("zFrac")] public double? ZFrac { get; set; }
    [JsonPropertyName("yaw")]   public double Yaw { get; set; }
    [JsonPropertyName("pitch")] public double Pitch { get; set; }
    [JsonPropertyName("fov")]   public double? Fov { get; set; }
    [JsonPropertyName("near")]  public double? Near { get; set; }
    /// <summary>
    /// SERVIR UNE PIÈCE : le groupe (+1 tribord, −1 bâbord, ∓2 et ∓3 la chasse
    /// par bord). L'œil se place alors DANS L'AXE du canon et derrière lui, et
    /// regarde où il pointe — la fiche n'a plus de coordonnées à tenir à jour
    /// quand le modèle bouge.
    /// </summary>
    [JsonPropertyName("gun")]   public int? Gun { get; set; }

    /// <summary>
    /// UNE VUE ENFERMÉE — une chambre, une soute, un entrepont. Le dehors s'y
    /// entend à travers un bordé de chêne : les bruits du large y sont étouffés et
    /// leurs aigus mangés. C'est la FICHE qui le dit, parce qu'elle seule sait
    /// laquelle de ses vues a un toit ; le deviner sur le nom d'un pont serait
    /// juste jusqu'au premier navire qui nomme les siens autrement.
    /// </summary>
    [JsonPropertyName("closed")] public bool Closed { get; set; }
}

/// <summary>Les distances de prise de vue, propres à chaque navire, et ses vues à bord.</summary>
public sealed class CameraSpec
{
    [JsonPropertyName("helmHeight")] public double? HelmHeight { get; set; }
    [JsonPropertyName("helmZFrac")]  public double? HelmZFrac { get; set; }
    [JsonPropertyName("chaseDist")]  public double ChaseDist { get; set; }
    [JsonPropertyName("chaseHigh")]  public double ChaseHigh { get; set; }
    [JsonPropertyName("orbitDist")]  public double OrbitDist { get; set; }
    [JsonPropertyName("decks")]      public List<DeckView>? Decks { get; set; }
}

/// <summary>
/// Ses couleurs, en hex sRGB comme la page les lit ("0xece4d2"), et les images
/// peintes sur sa toile, par SORTE de voile : un motif appartient aux basses
/// voiles et aux huniers, il n'a rien à faire sur un foc.
/// </summary>
public sealed class AppearanceSpec
{
    [JsonPropertyName("hull")]      public string Hull { get; set; } = "0x3a2a1c";
    [JsonPropertyName("spar")]      public string Spar { get; set; } = "0xa8875a";
    /* LA TOILE N'EST PAS BLANCHE. Elle était déclarée à 0xf2ebdc, soit 0,89 en
       linéaire contre 0,15 pour un bordé : à lumière égale elle rendait six fois
       ce que rend la coque, et de nuit elle restait le seul objet clair de
       l'image (signalé, deux captures). Le lin écru, salé et passé au soleil d'un
       navire d'époque est nettement plus sombre que le blanc d'une voile de
       régate moderne — c'est l'ÉTOFFE qui était fausse, pas l'éclairage. */
    [JsonPropertyName("canvas")]    public string Canvas { get; set; } = "0xd8cdb4";
    [JsonPropertyName("canvasMap")] public Dictionary<string, string>? CanvasMap { get; set; }
    /// <summary>Le pavillon qu'elle arbore ; « jolly » : la tête de mort, et c'est une déclaration.</summary>
    [JsonPropertyName("ensign")]    public string? Ensign { get; set; }
    /// <summary>L'image qui flotte ; le camp reste dit par <see cref="Ensign"/>.</summary>
    [JsonPropertyName("ensignMap")] public string? EnsignMap { get; set; }
    /// <summary>Les impacts peints : des variantes, chacune une suite de stades de l'éraflure à la plaie ouverte.</summary>
    [JsonPropertyName("impactMaps")] public List<List<string>>? ImpactMaps { get; set; }
}

/// <summary>
/// LES AVIRONS D'UNE EMBARCATION, telle que la fiche les écrit. pairs : combien
/// de paires ; period : le temps d'un coup entier, en secondes ; length : la
/// longueur d'un aviron, en mètres (absente, 1,7 fois son bau).
/// </summary>
public sealed class OarsJson
{
    [JsonPropertyName("pairs")]  public int? Pairs { get; set; }
    [JsonPropertyName("period")] public double? Period { get; set; }
    [JsonPropertyName("length")] public double? Length { get; set; }
}

/// <summary>Ce que le solveur et le modèle en tirent.</summary>
public sealed class OarsSpec
{
    public int Pairs = 2;
    public double Period = 2.2;
    public double Length = 3.4;
}

public sealed class ShipJson
{
    [JsonPropertyName("id")]   public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("note")] public string? Note { get; set; }
    /// <summary>La fiche de la chaloupe qu'elle porte, s'il y en a une.</summary>
    [JsonPropertyName("boat")] public string? Boat { get; set; }
    [JsonPropertyName("hull")] public HullShape Hull { get; set; } = new();
    [JsonPropertyName("displacementTonnes")] public double DisplacementTonnes { get; set; }
    [JsonPropertyName("cog")]    public CogSpec Cog { get; set; } = new();
    [JsonPropertyName("hydro")]  public HydroSpec Hydro { get; set; } = new();
    [JsonPropertyName("engine")] public EngineSpec Engine { get; set; } = new();
    [JsonPropertyName("rudder")] public RudderSpec Rudder { get; set; } = new();
    [JsonPropertyName("oars")] public OarsJson? Oars { get; set; }
    /// <summary>
    /// A-T-ELLE UNE ANCRE ? Vrai sauf mention contraire. Une chaloupe n'en porte
    /// pas — elle n'a ni écubier, ni cabestan, ni le poids qu'il faudrait pour
    /// tenir, et on pouvait pourtant mouiller avec (signalé).
    /// </summary>
    [JsonPropertyName("anchor")] public bool? Anchor { get; set; }
    [JsonPropertyName("rig")]    public RigSpec Rig { get; set; } = new();
    [JsonPropertyName("model")]  public ModelSpec? Model { get; set; }
    [JsonPropertyName("appearance")] public AppearanceSpec Appearance { get; set; } = new();
    [JsonPropertyName("lanterns")]   public List<LanternSpec>? Lanterns { get; set; }
    [JsonPropertyName("flags")]      public List<FlagSpec>? Flags { get; set; }
    [JsonPropertyName("camera")]     public CameraSpec Camera { get; set; } = new();
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
    /// <summary>L'identifiant de la chaloupe qu'elle porte, vide si elle n'en porte pas.</summary>
    public string Boat { get; }

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
    /// <summary>
    /// Le garde-fou du solveur, en m/s : au-delà, la vitesse est rabattue. 40
    /// par défaut, celui de la page — un navire d'époque n'en approche pas, et
    /// l'élan de débogage (B) s'y arrête. Une coque moderne peut le relever.
    /// </summary>
    public double SpeedLimit { get; }

    /// <summary>
    /// SES AVIRONS, si elle en a — une chaloupe, une yole, une galère. Nul
    /// autrement, et le solveur n'en sait alors rien.
    /// </summary>
    public OarsSpec? Oars { get; }

    /// <summary>A-t-elle une ancre à mouiller ? Une embarcation n'en a pas.</summary>
    public bool HasAnchor { get; }

    public double RudderK { get; }
    public double RudderMax { get; }
    public double RudderZ { get; }
    public double RudderY { get; }

    public RigSpec Rig { get; }
    public double SailArea { get; }
    /// <summary>La voilure carrée : le total, moins la latine s'il y en a une.</summary>
    public double SquareArea { get; }
    public double LateenArea { get; }
    /// <summary>
    /// La butée de brasseyage, radians : un carré ne se brasse pas plus près de
    /// l'axe — les vergues viennent sur les haubans. 0 : pas de butée.
    /// </summary>
    public double MinSheet { get; }
    public double LateenCeHeight { get; }
    public double LateenCeZ { get; }
    public double LateenMax { get; }
    public double CeHeight { get; }
    public double CeZ { get; }
    public double MaxSheet { get; }
    public IReadOnlyList<MastSpec> Masts { get; }
    public double JibFootZ { get; }

    public ModelSpec? Model { get; }
    public AppearanceSpec Appearance { get; }
    /// <summary>Ses feux, ou <c>null</c> : le feu de poupe par défaut.</summary>
    public List<LanternSpec>? Lanterns { get; }
    /// <summary>Ses pavillons ; absents, un seul en tête du plus grand mât.</summary>
    public List<FlagSpec>? Flags { get; }
    public CameraSpec Camera { get; }
    /// <summary>
    /// Ses vues à bord, dans l'ordre où C les parcourt. Sans camera.decks, la
    /// passerelle d'autrefois, tirée de helmHeight et helmZFrac : les vieilles
    /// fiches ne cassent pas.
    /// </summary>
    public List<DeckView> Decks { get; }

    // renseigne une fois la grille de sondes batie
    public double HullVolume { get; private set; }
    public double FillFraction { get; private set; }

    public ShipSpec(ShipJson json)
    {
        Raw = json;
        Id = json.Id;
        Name = json.Name;
        Note = json.Note ?? "";
        /* LA CHALOUPE QU'ELLE PORTE — l'identifiant d'une autre fiche. UNE CHALOUPE
           EST UN NAVIRE : rien d'autre n'est nécessaire, ni classe, ni cas
           particulier ; celui qui la met à l'eau la met à l'eau comme une conserve
           et en prend la barre comme d'une autre coque. */
        Boat = json.Boat ?? "";

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
        SpeedLimit = Math.Max(1, json.Engine.SpeedLimit ?? 40);

        /* SES AVIRONS. La longueur par défaut vaut 1,7 fois son bau, comme la
           page : un aviron qui ne déborde pas assez ne fait pas de bras de
           levier, et c'est le levier qui la fait tourner. */
        if (json.Oars is OarsJson oj)
            Oars = new OarsSpec
            {
                Pairs = Math.Max(1, oj.Pairs ?? 2),
                Period = Math.Max(0.4, oj.Period ?? 2.2),
                Length = oj.Length ?? 1.7 * json.Hull.Beam
            };

        /* SON ANCRE : celle qu'on mouille. Absente de la fiche, elle en a une —
           tous les bâtiments en portent ; ce sont les embarcations qui font
           exception, et elles le DISENT. */
        HasAnchor = json.Anchor ?? true;

        RudderK = json.Rudder.Power * lateralArea;
        RudderMax = json.Rudder.MaxAngle;
        RudderZ = json.Rudder.PostZFrac * L;
        RudderY = json.Rudder.PostY;

        var r = json.Rig;
        Rig = r;
        SailArea = r.SailArea;
        LateenArea = r.Lateen != null ? Math.Max(0, Math.Min(r.SailArea, r.Lateen.Area)) : 0;
        SquareArea = SailArea - LateenArea;
        LateenCeHeight = r.Lateen?.CeHeight ?? 0;
        LateenCeZ = r.Lateen?.CeZ ?? 0;
        LateenMax = r.Lateen?.MaxSheet ?? 1.45;
        CeHeight = r.CeHeight;
        CeZ = r.CeZ;
        MaxSheet = r.MaxSheet;
        MinSheet = Math.Max(0, r.MinSheet ?? 0);

        foreach (var m in r.Masts) m.Z = m.ZFrac * L;
        Masts = r.Masts;
        JibFootZ = r.Jib != null ? r.Jib.FootFrac * L : 0;

        Model = json.Model;
        Appearance = json.Appearance ?? new();
        Lanterns = json.Lanterns;
        Flags = json.Flags;
        Camera = json.Camera ?? new();
        Decks = Camera.Decks is { Count: > 0 } d
            ? d
            : new List<DeckView> { new() { Name = "Passerelle", X = 0, Y = Camera.HelmHeight, ZFrac = Camera.HelmZFrac } };
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
