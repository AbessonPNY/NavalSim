using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// CE QUI TOURNE SUR ELLE SANS SE DÉFORMER — les anneaux.
///
/// Un anneau gyroscopique est une rotation RIGIDE : seize nombres dans une
/// matrice, appliqués par le GPU à l'objet entier. Le cuire dans une texture
/// d'animation (voir characters/, et la section du journal qui le refuse) aurait
/// coûté des mégaoctets pour dire moins bien ce qu'une ligne dit ici — et,
/// surtout, une cuisson ne SAIT PAS ce que fait le navire. Or c'est justement ce
/// qu'on lui demande : rester d'aplomb pendant que la coque roule.
///
/// LE COMPTE EXACT DE CE QU'ON LUI DEMANDE, à chaque image :
///
///   · elle garde son CAP — sinon un anneau qui ceint le navire dans l'axe se
///     mettrait en travers de lui au premier virement ;
///   · elle ignore le ROULIS et le TANGAGE — c'est ce qui la fait lire comme un
///     gyroscope, et non comme une pièce boulonnée au pont ;
///   · elle tourne sur SA propre normale, celle du plan dans lequel elle est
///     modelée. Vertical, incliné, couché : l'inclinaison est celle du maillage,
///     jamais un angle écrit dans une fiche qu'il faudrait tenir d'accord avec
///     Blender.
///
/// LES ANNEAUX SORTENT DU MODÈLE AVANT TOUTE MESURE, et c'est la raison d'être
/// de ce fichier plutôt que de trois lignes ailleurs. Quatre endroits au moins
/// cherchent la coque en prenant LE MAILLAGE LE PLUS VOLUMINEUX — HullScale,
/// MakeProfile, DeckProfile, HullPart. Un anneau qui ceint le navire enferme
/// plus d'air que la coque n'enferme de bois : il serait pris pour elle, et tout
/// ce qui dérive des cotes — l'échelle du modèle, le collier d'écume, le profil
/// de flottaison, la batterie — partirait avec. Ils sont donc retirés de
/// ModelRoot dès le chargement, et rien en aval ne les voit jamais.
/// </summary>
public partial class ShipNode
{
    /// <summary>
    /// Ce qui, dans un .glb, dit « je tourne » : le nom du nœud suffit. Il doit
    /// COMMENCER le mot — « ring » en simple sous-chaîne attraperait mooring,
    /// steering, bearing, spring, et une pièce de coque arrachée au modèle pour
    /// la faire tourner est une panne qui ne ressemble pas à sa cause.
    /// </summary>
    static readonly Regex RingNames = new("(^|[^a-z0-9])(anneau|gyro|ring)", RegexOptions.IgnoreCase);

    /// <summary>Le tour par minute du premier anneau, faute de fiche.</summary>
    const double BaseRpm = 6;

    /// <summary>
    /// LA SPHÈRE D'ÉNERGIE, si le modèle en porte une. Même règle de nom que les
    /// anneaux, et même raison de la sortir du modèle : celle de la Roter Löwe
    /// fait vingt mètres de rayon, donc une boîte trente-cinq fois plus grosse que
    /// celle de la coque — elle serait prise pour la coque par tout ce qui cherche
    /// « le maillage le plus volumineux », y compris la mesure de quille qui règle
    /// l'échouage.
    /// </summary>
    /// LE MOT EST « ENERGIE », PAS « SPHERE ». Le modèle de la Roter Löwe porte
    /// déjà un nœud nommé Sphere — une bille de trois centimètres en métal noir,
    /// une ferrure quelconque. Un motif sur « sphere » l'aurait arrachée à la
    /// coque pour en faire une bulle d'énergie qui grandit avant un saut, et rien
    /// n'aurait signalé qu'une pièce du bord avait disparu.
    static readonly Regex SphereNames = new("(^|[^a-z0-9])(energie|énergie|energy)", RegexOptions.IgnoreCase);

    Node3D? _sphere;
    readonly List<ShaderMaterial> _sphereGlow = new();
    bool _sphereShown;

    /// <summary>Où en est la bulle, de 0 à 1.</summary>
    public double SphereLevel { get; private set; }

    /// <summary>
    /// À PARTIR DE QUELLE CHARGE elle paraît. Elle ne monte pas avec les anneaux
    /// depuis le début : demandé, elle arrive « quand les anneaux vont très vite »,
    /// et leur vitesse suit le CUBE de la charge — au-delà des deux tiers, elle
    /// double tous les quelques secondes. C'est là que la bulle a un sens.
    /// </summary>
    public double SphereFrom = 0.62;

    /// <summary>
    /// Secondes pour qu'elle s'efface. Elle ne suit PAS la retombée des anneaux :
    /// « disparaît très vite à la fin du saut ». Les anneaux mettent 1,8 s à
    /// s'éteindre, elle un tiers de seconde — ce qui reste s'éteint derrière elle,
    /// et non l'inverse.
    /// </summary>
    public double SphereFall = 0.32;

    sealed class Ring
    {
        public Node3D Pivot = null!;
        /// <summary>Ce autour de quoi il TOURNE, dans le repère du modèle.</summary>
        public Vector3 Axis;
        /// <summary>rad/s — le signe donne le sens.</summary>
        public double Rate;
        public double Phase;
        /// <summary>Vrai : il ignore le roulis et le tangage. Faux : il roule avec la coque.</summary>
        public bool Steady;
        /// <summary>Ses matières de lueur, une par maillage : c'est par elles que passe la charge.</summary>
        public readonly List<ShaderMaterial> Glow = new();
    }

    readonly List<Ring> _rings = new();

    // ------------------------------------------------------------------
    //  LA CHARGE — ce qui les allume
    // ------------------------------------------------------------------

    /// <summary>
    /// CE QUE LE JEU DEMANDE : anneaux allumés ou éteints. La lueur ne suit pas
    /// d'un coup — <see cref="RingLevel"/> monte et descend en rampe, parce qu'un
    /// téléporteur qui s'allume instantanément n'a l'air de rien charger.
    /// </summary>
    public bool RingsOrdered { get; set; } = true;

    /// <summary>Où en est la charge, de 0 à 1. Lisible pour caler un son ou un saut dessus.</summary>
    public double RingLevel { get; private set; }

    /// <summary>
    /// Secondes pour monter à pleine charge, et pour retomber. La montée est
    /// écrite par qui commande le saut : c'est SA durée de charge.
    /// </summary>
    public double RingsRise = 3.5, RingsFall = 1.8;

    /// <summary>Combien de fois leur vitesse nominale ils atteignent à pleine charge.</summary>
    public double RingBoost = 6;

    bool _ringsLit;
    double _ringClock;

    /// <summary>Vrai dès qu'un anneau est visible : la démo relève le seuil de lueur là-dessus.</summary>
    public bool RingsLit => _ringsLit;

    /// <summary>Combien d'anneaux elle porte. Zéro : elle n'a pas de téléporteur.</summary>
    public int RingCount => _rings.Count;

    /// <summary>Porte-t-elle la bulle ? Un téléporteur peut s'en passer, c'est un décor.</summary>
    public bool HasSphere => _sphere != null;

    // ------------------------------------------------------------------
    //  CE QU'ELLE ÉCLAIRE
    // ------------------------------------------------------------------

    /* UNE SEULE LAMPE, AU CENTRE DE LA SPHÈRE, et c'est une approximation qu'il
       faut assumer : ce qui brille est une COQUILLE de vingt mètres, pas un point.
       Mais tout le navire est DEDANS — mâts compris, la sphère montant à près de
       vingt-cinq mètres — et depuis l'intérieur d'une coquille lumineuse, une
       source au centre donne presque la même chose : la lumière vient d'en face,
       quel que soit l'endroit du bord où l'on se tient.

       UNE SEULE, AUSSI, PARCE QUE LA MER N'EN TIENT QUE HUIT (NLAMP), partagées
       par toute la flotte et déjà prises par les fanaux. */
    OmniLight3D? _ringLamp;

    /// <summary>Sa portée à pleine charge, en mètres : au-delà de la sphère, pour que l'eau s'allume autour.</summary>
    public double LampRange = 140;
    /// <summary>Et sa force à pleine charge.</summary>
    public double LampEnergy = 9;

    /// <summary>
    /// CE QUE LA MER EN REÇOIT, en fois l'énergie de la lampe — et les deux
    /// nombres n'ont aucune raison d'être le même.
    ///
    /// La lampe éclaire une coque à vingt mètres, où neuf suffit largement ; le
    /// reflet, lui, doit se voir SUR UNE MER DE PLEIN JOUR, contre un soleil qui
    /// vaut cent mille fois un fanal. Un fanal n'est pas visible à midi et c'est
    /// juste ; un téléporteur doit l'être, et c'est ce gain qui le permet sans
    /// aller brûler la coque.
    /// </summary>
    public double LampSeaGain = 12;

    /// <summary>
    /// SUR COMBIEN DE POINTS LA MER LE VOIT. Une lampe ponctuelle ne pose qu'un
    /// point de lumière sur l'eau, et c'est ce qu'on voyait : une étincelle fixe
    /// au milieu du reflet. Une coquille de vingt mètres doit poser une large
    /// tache — on la répartit donc sur un cercle.
    ///
    /// Cinq et pas huit : le tableau de la mer n'en tient que huit pour la flotte
    /// entière, et laisser trois places aux fanaux évite qu'un navire perde ses
    /// feux parce qu'un autre charge son téléporteur.
    /// </summary>
    public int LampPoints = 5;

    /// <summary>Le rayon de l'appareil, en mètres : le plus grand anneau.</summary>
    double _apparatusR;

    // ------------------------------------------------------------------
    //  LE REFLET DANS L'EAU
    // ------------------------------------------------------------------

    /* IL FAUT LE DESSINER, et ce n'est pas un choix. Le miroir de la mer est en
       espace écran : il relit l'image déjà rendue. Or la mer est un matériau
       OPAQUE, donc dessinée AVANT la passe transparente où vivent les anneaux et
       la bulle — son image est celle d'un monde où ils n'existent pas encore.
       Aucune écriture de profondeur ne rattrape un ordre de passe.

       Reste ce que la page faisait déjà : rendre une seconde fois depuis un œil
       en miroir. Ici c'est la GÉOMÉTRIE qu'on retourne plutôt que l'œil — une
       copie de l'appareil, symétrique du plan d'eau, peinte par
       teleport_mirror.gdshader.

       LA COPIE EST HORS DU NAVIRE, sous la scène. Enfant du navire, elle serait
       retournée dans SON repère, qui roule et tangue ; le miroir d'une mer est
       celui du monde, et il ne suit ni la gîte ni l'assiette. */
    /// <summary>Le masque a l eau. Coupe (--reflet 2), le reflet se peint partout : diagnostic.</summary>
    public bool MirrorMask = true;
    /// <summary>Le fondu avec la profondeur (--reflet 3 le coupe seul).</summary>
    public bool MirrorFade = true;
    /// <summary>--reflet 4 : peindre la distance au plan au lieu du reflet.</summary>
    public bool MirrorDebug;

    /// <summary>Le reflet est-il dessine du tout ? (--reflet 0 le coupe)</summary>
    public bool MirrorOn = true;

    /// <summary>
    /// FORCER LA CHARGE, pour un banc : la rampe met vingt secondes, et une
    /// capture ne peut pas les attendre. Rend la main a la rampe avec un negatif.
    /// </summary>
    public double ForcedLevel = -1;

    Node3D? _mirror;
    readonly List<(Node3D Twin, Node3D Of)> _mirrorPairs = new();
    /// <summary>Ses matières, et laquelle suit la bulle plutôt que les anneaux.</summary>
    readonly List<(ShaderMaterial Mat, bool Sphere)> _mirrorMats = new();

    /// <summary>Les matières du reflet : la démo leur pousse la houle qui les ride.</summary>
    public IEnumerable<ShaderMaterial> MirrorMaterials { get { foreach (var m in _mirrorMats) yield return m.Mat; } }

    /* LES OMBRES NE S'ALLUMENT QUE SUR LA FIN. Une lampe de cette portée qui
       ombre coûte son cube entier à chaque image, et l'on sait depuis les fanaux
       qu'un omni rend son cube QUEL QUE SOIT son énergie — le couper ne suffit
       pas, il faut couper l'ombre elle-même. Elles s'allument donc à mi-charge,
       quand la lumière est assez forte pour qu'elles se voient, et pour les
       dernières secondes seulement : c'est là que le gréement doit se découper
       sur le pont. */
    public double LampShadowFrom = 0.45;
    bool _lampShadows;
    /// <summary>Leurs pivots, pour que LocalBounds ne compte pas leur envergure.</summary>
    readonly HashSet<Node3D> _ringPivots = new();

    // ------------------------------------------------------------------
    //  LES SORTIR DU MODÈLE
    // ------------------------------------------------------------------

    /// <summary>
    /// Détacher du modèle tout ce qui porte un nom d'anneau, avec la transformée
    /// qui ramène le PARENT de chaque pièce dans le repère du modèle. Appelée
    /// alors que <c>obj</c> n'a encore reçu ni échelle ni rotation : les
    /// coordonnées rendues sont donc celles de Blender, à l'échelle de Blender.
    ///
    /// UN ANNEAU EMPORTE SES SOUS-PIÈCES — ferrures, géométrie de lueur, demi-
    /// tores — mais PAS un autre anneau. Blender parente volontiers deux objets
    /// l'un à l'autre, et un export où `anneau_2` est enfant de `anneau_1` est
    /// arrivé du premier coup. Pris ensemble, les deux ne forment plus un anneau
    /// du tout : leur direction de moindre variance ne veut plus rien dire, et
    /// l'ensemble se met à BALAYER au lieu de pivoter — ce qui tourne quand même,
    /// donc ce qui a l'air de marcher. On descend donc, et chaque nom d'anneau
    /// rencontré ouvre son propre pivot.
    ///
    /// LE PLUS PROFOND SE DÉTACHE EN PREMIER, sans quoi on retirerait le parent
    /// de l'arbre avant d'avoir pu en sortir l'enfant.
    /// </summary>
    static List<(Node3D Node, Transform3D Parent)> TakeRings(Node3D obj) => Detach(obj, RingNames);

    /// <summary>
    /// ÔTER L APPAREIL D UN MODÈLE QU ON NE PILOTE PAS. Le téléporteur est au
    /// navire du joueur, pas à la fiche : la Roter Löwe le porte dans son .glb, si
    /// bien qu une copie amarrée au quai en héritait — trois anneaux lumineux sur
    /// une coque qui dort, et la sphère avec.
    ///
    /// On les DÉTACHE plutôt que de les cacher : caché, un maillage coûte encore
    /// son entrée dans la scène et sa boîte englobante, et une rade en portait
    /// quarante-cinq.
    /// </summary>
    internal static void StripRings(Node3D obj)
    {
        foreach (var (n, _) in Detach(obj, RingNames)) n.QueueFree();
        foreach (var (n, _) in Detach(obj, SphereNames)) n.QueueFree();
    }

    /// <summary>
    /// Détacher du modèle tout ce qui répond à <paramref name="re"/>. Les anneaux
    /// et la sphère d'énergie s'en servent tous deux : ce sont les mêmes raisons
    /// (protéger les mesures de coque) et la même mécanique.
    /// </summary>
    static List<(Node3D Node, Transform3D Parent)> Detach(Node3D obj, Regex re)
    {
        var found = new List<(Node3D Node, Transform3D Parent, int Depth)>();
        var stack = new Stack<(Node Node, Transform3D Acc, int Depth)>();
        stack.Push((obj, Transform3D.Identity, 0));
        while (stack.Count > 0)
        {
            var (n, acc, d) = stack.Pop();
            if (n != obj && n is Node3D nd && re.IsMatch(nd.Name)) found.Add((nd, acc, d));
            // acc mène du repère PARENT de n au repère du modèle ; pour ses
            // enfants, le repère parent est celui de n lui-même
            var next = n != obj && n is Node3D c3 ? acc * c3.Transform : acc;
            foreach (var kid in n.GetChildren()) stack.Push((kid, next, d + 1));
        }
        foreach (var f in found.OrderByDescending(f => f.Depth))
            f.Node.GetParent()?.RemoveChild(f.Node);
        // rendus dans un ordre STABLE : la vitesse par défaut dépend du rang
        return found.OrderBy(f => f.Depth).ThenBy(f => f.Node.Name.ToString(), StringComparer.Ordinal)
                    .Select(f => (f.Node, f.Parent)).ToList();
    }

    // ------------------------------------------------------------------
    //  LES REMONTER SUR LEUR PIVOT
    // ------------------------------------------------------------------

    /// <summary>
    /// Pendre chaque anneau à un pivot planté au CENTRE de l'anneau, sous le
    /// navire et non sous le modèle.
    ///
    /// Sous le navire, parce qu'un pivot placé dans le modèle hériterait de son
    /// orientation, et qu'on veut précisément la lui retirer. Au centre de
    /// l'anneau, parce qu'un anneau qui tourne autour d'un autre point tourne en
    /// excentrique : il balaie au lieu de pivoter. Le pivot reçoit l'échelle, la
    /// rotation et le décalage que la fiche impose au modèle, si bien que
    /// l'anneau au repos se retrouve exactement là où Blender l'avait mis.
    /// </summary>
    void MountRings(List<(Node3D Node, Transform3D Parent)> found, double k, ModelSpec m)
    {
        if (found.Count == 0) return;
        GD.Print($"[{Spec.Id}] {found.Count} anneau(x) trouvé(s) dans le modèle");
        var ry = new Quaternion(Vector3.Up, (float)m.RotationY);
        var off = new Vector3((float)m.Offset[0], (float)m.Offset[1], (float)m.Offset[2]);

        for (int i = 0; i < found.Count; i++)
        {
            var (nd, parent) = found[i];
            var inModel = parent * nd.Transform;

            // ses sommets, ramenés dans le repère du modèle
            var pts = new List<Vector3>();
            foreach (var (mi, t) in Meshes(nd))
            {
                var src = mi.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (var v in src) pts.Add(parent * (t * v));
            }
            if (pts.Count < 3)
            {
                GD.PushWarning($"[{Spec.Id}] {nd.Name} : pas de sommets, anneau ignoré.");
                continue;
            }

            Vector3 c = Vector3.Zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;
            Vector3 axis = RingAxis(pts, c);

            /* L'ÉPREUVE QUI DIT SI C'EST VRAIMENT UN ANNEAU : son rayon contre sa
               demi-épaisseur. Un anneau est mince, donc élancé — vingt fois, sur
               les deux qu'on a vus. En dessous de trois, la direction de moindre
               variance ne distingue plus rien, l'axe trouvé est arbitraire, et la
               pièce BALAIERA au lieu de pivoter. Cela tourne quand même : sans ce
               mot, la panne ne se verrait qu'à l'œil, par mer plate, de profil. */
            double rmax = 0, emax = 0;
            foreach (var p in pts)
            {
                var d = p - c;
                double h = d.Dot(axis);
                rmax = Math.Max(rmax, (d - axis * (float)h).Length());
                emax = Math.Max(emax, Math.Abs(h));
            }
            double elan = rmax / Math.Max(1e-9, emax);
            if (elan < 3)
                GD.PushWarning(FormattableString.Invariant(
                    $"[{Spec.Id}] {nd.Name} : élancement {elan:F1} — cette pièce ne se lit pas comme un anneau, son axe est arbitraire. Deux anneaux parentés l'un à l'autre ?"));

            var pivot = new Node3D
            {
                Name = $"pivot_{nd.Name}",
                Position = off + ry * (c * (float)k),
                Scale = Vector3.One * (float)k,
                Quaternion = ry
            };
            AddChild(pivot);
            pivot.AddChild(nd);
            // sa place d'origine dans le modèle, ramenée sur le centre de l'anneau
            nd.Transform = new Transform3D(Basis.Identity, -c) * inModel;

            /* LA MATIÈRE DE LUEUR, UNE PAR MAILLAGE ET NON UNE PAR ANNEAU. Elle
               porte le centre et la normale DANS LE REPÈRE DE SON MAILLAGE, et
               deux maillages d'un même anneau n'ont pas le même repère. Un
               override plutôt qu'une retouche : le matériau du .glb reste celui
               de l'auteur, et rien n'est perdu si l'on veut le rendre. */
            /* APRÈS LE REPARENTAGE, DONC DANS LE REPÈRE DU PIVOT, où le centre de
               l'anneau est l'ORIGINE par construction. Écrit avec « parent » dans
               la chaîne, comme au-dessus, on inversait une transformée qui n'est
               plus celle du maillage : le centre et l'axe poussés au shader
               tombaient à côté, et les bandes ne couraient plus le long de
               l'anneau mais en travers.

               La leçon : <c>Meshes(nd)</c> rend la transformée COURANTE, et deux
               lignes plus haut on vient justement de la réécrire. */
            var glow = new List<ShaderMaterial>();
            foreach (var (mi, t) in Meshes(nd))
            {
                var inv = t.AffineInverse();
                var gm = new ShaderMaterial { Shader = LoadShader("res://shaders/ring_glow.gdshader") };
                gm.SetShaderParameter("u_centre", inv * Vector3.Zero);
                gm.SetShaderParameter("u_axis", (inv.Basis * axis).Normalized());
                mi.MaterialOverride = gm;
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;   // de la lumière n'ombre pas
                glow.Add(gm);
            }

            var sp = RingFor(m, nd.Name);
            /* DEUX ANNEAUX À LA MÊME VITESSE se retrouvent toujours dans la même
               figure, et l'ensemble se lit comme une pièce unique. Faute de fiche,
               chacun prend donc la sienne — mais TOUTES VIVES.

               La règle d'avant (−0,618 fois le précédent) allait pour deux et ne
               vaut plus pour huit : le cinquième anneau tournait à un dixième de
               tour par minute, c'est-à-dire à l'arrêt. On garde le nombre d'or
               pour l'irrégularité — sa partie fractionnaire ne boucle jamais, donc
               les anneaux ne se réalignent pas — mais on le fait jouer DANS une
               bande de vitesses, au lieu de l'empiler. Le sens alterne. */
            double frac = (i * 0.6180339887) % 1.0;
            double rpm = sp?.Rpm ?? BaseRpm * (0.55 + 0.9 * frac) * (i % 2 == 0 ? 1 : -1);
            /* ET LES AXES SE CROISENT, faute de fiche : vertical, étrave, travers,
               puis on recommence. Huit anneaux qui tourneraient tous autour de la
               verticale feraient un manège ; alternés, ils font la figure d'orbites
               entrecroisées — celle qu'on cherche. Une fiche qui nomme l'axe reste
               souveraine. */
            string mode = sp?.Spin ?? (i % 3) switch { 0 => "vertical", 1 => "etrave", _ => "travers" };
            Vector3 spin = SpinAxis(axis, mode);
            /* UN ANNEAU QUI TOURNE AUTOUR DE SA NORMALE NE TOURNE PAS : il balaie
               sa figure de depart, et il ne reste que le scintillement de ses
               facettes. La panne est signalee ici parce qu elle ne se voit qu a
               l oeil et qu on la prend pour un defaut de rendu. */
            if (mode != "normale" && Math.Abs(spin.Dot(axis)) > 0.99f)
                GD.PushWarning(FormattableString.Invariant(
                    $"[{Spec.Id}] {nd.Name} : l axe « {mode} » est celui de sa normale — il tournera dans son propre plan, donc sans rien montrer. Prenez un autre axe."));
            var ring = new Ring
            {
                Pivot = pivot,
                Axis = spin,
                Rate = rpm * Math.Tau / 60,
                Steady = sp?.Steady ?? true
            };
            ring.Glow.AddRange(glow);
            _rings.Add(ring);
            if (sp != null && !sp.On) RingsOrdered = false;
            _ringPivots.Add(pivot);
            _apparatusR = Math.Max(_apparatusR, rmax * k);
            GD.Print(FormattableString.Invariant(
                $"[{Spec.Id}] anneau {nd.Name} : normale ({axis.X:F2}, {axis.Y:F2}, {axis.Z:F2}), tourne « {mode} » autour de ({spin.X:F2}, {spin.Y:F2}, {spin.Z:F2}), rayon {rmax * k:F1} m, élancement {elan:F0}, {rpm:F1} tr/min"));
        }
    }

    /// <summary>
    /// AUTOUR DE QUOI L'ANNEAU TOURNE, à partir de la normale de son plan.
    ///
    /// Le piège, et c'est celui qui a été signalé en jeu : faire tourner un
    /// cercle autour de SA PROPRE NORMALE ne se voit pas. La figure balayée est
    /// exactement la figure de départ ; il n'en reste que le scintillement des
    /// facettes, qui se lit comme un défaut et non comme un mouvement. Un anneau
    /// ne devient visiblement mobile qu'en tournant autour d'un DIAMÈTRE.
    ///
    /// LES AXES SONT NOMMÉS ET FIXES, dans le repère du NAVIRE : « vertical »
    /// (0,1,0) fait balayer le cerceau comme un portail qui tourne, « etrave »
    /// (0,0,1) le fait basculer bout sur bout, « travers » (1,0,0) le fait rouler
    /// d'un bord sur l'autre. « normale » rend l'ancien comportement, qui garde
    /// son emploi sur un anneau porteur d'un motif ou d'une lueur qui court.
    ///
    /// NOMMÉS plutôt que x/y/z, parce que l'auteur pense dans les axes de Blender
    /// — Z en haut, Y vers l'avant — et le moteur dans ceux du glTF — Y en haut,
    /// Z vers l'avant. « l'axe Z » ne désigne donc pas la même chose des deux
    /// côtés de l'export, et un mot ne peut pas se tromper de convention. Les
    /// lettres restent acceptées, dans la convention du MOTEUR.
    ///
    /// FIXES, et non déduits du plan de l'anneau : un axe déduit change quand on
    /// repenche la pièce dans Blender, et ce qu'on avait réglé à l'œil se défait
    /// sans qu'on ait touché à la fiche.
    /// </summary>
    static Vector3 SpinAxis(Vector3 n, string mode) => mode switch
    {
        // sa propre normale : il tourne DANS son plan, donc sans rien montrer
        "normale" => n,
        "travers" or "x" => new Vector3(1, 0, 0),
        "etrave" or "étrave" or "z" => new Vector3(0, 0, 1),
        _ => new Vector3(0, 1, 0),
    };

    /// <summary>
    /// Pendre la sphère à son propre nœud, sous le navire.
    ///
    /// Elle ne tourne pas et n'a pas à être tenue d'aplomb : une sphère centrée
    /// sur le navire est la même vue de partout, et lui retirer le roulis ne se
    /// verrait pas. Elle se contente donc de la place que le modèle lui donne,
    /// portée par la coque.
    /// </summary>
    void MountSphere(List<(Node3D Node, Transform3D Parent)> found, double k, ModelSpec m)
    {
        if (found.Count == 0) return;

        var ry = new Quaternion(Vector3.Up, (float)m.RotationY);
        var off = new Vector3((float)m.Offset[0], (float)m.Offset[1], (float)m.Offset[2]);

        var pivot = new Node3D
        {
            Name = "pivot_sphere",
            Position = off,
            Scale = Vector3.One * (float)k,
            Quaternion = ry,
            Visible = false
        };
        AddChild(pivot);
        _sphere = pivot;
        _ringPivots.Add(pivot);            // son envergure n'est pas celle du navire

        foreach (var (nd, parent) in found)
        {
            var inModel = parent * nd.Transform;
            nd.GetParent()?.RemoveChild(nd);
            pivot.AddChild(nd);
            nd.Transform = inModel;        // pas de recentrage : elle ne pivote pas

            foreach (var (mi, t) in Meshes(nd))
            {
                var gm = new ShaderMaterial
                {
                    Shader = LoadShader("res://shaders/energy_sphere.gdshader"),
                    /* APRÈS LES ANNEAUX, qui sont dedans. Deux transparences au même
                       rang se départagent sur le centre de leur objet, et les trois
                       ont le même centre — rien ne les départagerait. */
                    RenderPriority = 1
                };
                mi.MaterialOverride = gm;
                mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _sphereGlow.Add(gm);
            }
        }
        GD.Print($"[{Spec.Id}] sphère d'énergie : {_sphereGlow.Count} maillage(s)");
    }

    /// <summary>
    /// LA LAMPE DU TÉLÉPORTEUR, bâtie une fois et jamais retirée — on ne fait pas
    /// naître une lumière en jeu. Elle est ÉTEINTE et INVISIBLE au repos, ce qui
    /// ne coûte rien, et c'est son énergie qui la fait exister.
    ///
    /// Posée au centre de la sphère s'il y en a une, sinon à celui du premier
    /// anneau : dans les deux cas le centre de l'appareil, qui est aussi le centre
    /// du navire à quelques mètres près.
    /// </summary>
    public void MountLamp()
    {
        /* AU CENTRE DE L APPAREIL, qui est celui des anneaux : leur pivot est
           plante dessus par construction. Le pivot de la sphere, lui, porte le
           decalage du modele et non le centre de la bulle — s en servir mettait
           la lampe a la flottaison, deux metres trop bas et trois trop en
           arriere. A defaut d anneaux, on prend ce qu il y a.

           Appelee APRES les deux montages, pour qu il n y en ait qu une quoi que
           le modele porte. */
        if (_ringLamp != null) return;
        Vector3 centre = _rings.Count > 0 ? _rings[0].Pivot.Position
                       : _sphere != null ? _sphere.Position : Vector3.Zero;
        if (_rings.Count == 0 && _sphere == null) return;
        _ringLamp = new OmniLight3D
        {
            Name = "lampe_anneaux",
            Position = centre,
            LightColor = new Color(0.55f, 0.74f, 1.0f),
            LightEnergy = 0,
            OmniRange = (float)LampRange,
            /* UNE ATTÉNUATION MOLLE (0,6 au lieu de 1) : une coquille lumineuse
               n'a pas la décroissance en carré d'un point, et une chute trop raide
               laisserait la mer noire à trente mètres du bord pendant que le pont
               serait éblouissant. */
            OmniAttenuation = 0.6f,
            ShadowEnabled = false,
            OmniShadowMode = OmniLight3D.ShadowMode.Cube,
            ShadowBias = 0.06f,
            Visible = false
        };
        AddChild(_ringLamp);
    }

    /// <summary>
    /// BÂTIR LA COPIE RETOURNÉE : un jumeau par pivot, portant les mêmes maillages
    /// — partagés, non recopiés — sous une matière de reflet.
    ///
    /// Appelée après les deux montages, comme la lampe. Rien n'est créé en jeu :
    /// les jumeaux existent dès la mise à l'eau et ne coûtent que tant qu'ils sont
    /// visibles.
    /// </summary>
    void MountMirror()
    {
        if (_mirror != null || (_rings.Count == 0 && _sphere == null)) return;
        var sh = LoadShader("res://shaders/teleport_mirror.gdshader");
        if (sh == null) return;

        /* PAS ENCORE PENDU. Il le sera à la première image, au parent du navire —
           voir SyncMirror. Le faire ici obligerait à savoir si le navire est déjà
           dans l arbre au moment où son modèle se charge, ce qui dépend de qui le
           met à l eau ; le reporter à la première image ne dépend de rien. */
        _mirror = new Node3D { Name = $"reflet_{Spec.Id}", Visible = false };
        GD.Print($"[{Spec.Id}] reflet : shader chargé");

        foreach (var r in _rings) Twin(r.Pivot, 0);
        if (_sphere != null) Twin(_sphere, 1);
        GD.Print($"[{Spec.Id}] reflet : {_mirrorPairs.Count} jumeau(x), {_mirrorMats.Count} matière(s)");

        void Twin(Node3D of, float sphere)
        {
            var twin = new Node3D { Name = "r_" + of.Name };
            _mirror.AddChild(twin);
            _mirrorPairs.Add((twin, of));
            /* LA TRANSFORMÉE RENDUE PAR Meshes() CONTIENT DÉJÀ CELLE DU PIVOT —
                elle part de lui. La reposer sur le jumeau, qui porte déjà la même,
                l applique DEUX FOIS : la copie se retrouve tournée deux fois et
                mise au carré de l échelle, donc ailleurs. On retire donc la part du
                pivot pour ne garder que la place du maillage DANS lui. */
            var undo = of.Transform.AffineInverse();
            foreach (var (mi, t) in Meshes(of))
            {
                var gm = new ShaderMaterial { Shader = sh };
                gm.SetShaderParameter("u_sphere", sphere);
                /* LE CENTRE ET L'AXE DE L'ANNEAU, pris sur la matière de l'original :
                   le reflet doit lire le MÊME angle, sinon ses bandes ne seraient pas
                   celles qu'on voit au-dessus. */
                if (mi.MaterialOverride is ShaderMaterial src)
                {
                    gm.SetShaderParameter("u_centre", src.GetShaderParameter("u_centre"));
                    gm.SetShaderParameter("u_axis", src.GetShaderParameter("u_axis"));
                }
                twin.AddChild(new MeshInstance3D
                {
                    Mesh = mi.Mesh,               // partagé : un reflet n'est pas une seconde géométrie
                    Transform = undo * t,
                    MaterialOverride = gm,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                });
                _mirrorMats.Add((gm, sphere > 0.5f));
            }
        }
    }

    /// <summary>
    /// Poser le reflet pour cette image : la copie prend la place du navire,
    /// retournée autour du plan d'eau, et chaque jumeau la pose de son pivot.
    ///
    /// <paramref name="seaY"/> est le niveau de la mer sous le navire, en mètres
    /// monde. C'est le plan du miroir, et il monte et descend avec la houle — un
    /// reflet calé sur le zéro hydrographique décollerait de l'eau à chaque lame.
    /// </summary>
    public void SyncMirror(double seaY, Camera3D cam)
    {
        if (_mirror == null) return;
        /* SOUS LA SCÈNE ET NON SOUS LE NAVIRE : le miroir d une mer est celui du
           MONDE, et il ne suit ni la gîte ni l assiette. Enfant du navire, la copie
           serait retournée dans son repère à lui, qui roule. */
        if (_mirror.GetParent() == null)
        {
            if (GetParent() is not Node p) return;
            p.AddChild(_mirror);
        }
        bool on = MirrorOn && (_ringsLit || _sphereShown);
        if (_mirror.Visible != on) _mirror.Visible = on;
        if (!on) return;

        /* LE MIROIR : y devient 2·seaY − y. En transformée, c'est une échelle
           négative en y suivie d'une montée de deux fois le niveau. Le déterminant
           est négatif, donc les faces s'inversent — les matières du reflet sont en
           cull_disabled, ce qui rend la question sans objet. */
        var m = new Transform3D(
            new Basis(new Vector3(1, 0, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1)),
            new Vector3(0, (float)(2 * seaY), 0));
        _mirror.GlobalTransform = m * GlobalTransform;
        foreach (var (twin, of) in _mirrorPairs) twin.Transform = of.Transform;
        /* LE PLAN D EAU EN ESPACE VUE. Godot rend les grands mondes en
           coordonnees RELATIVES A LA CAMERA : une hauteur de mer absolue ne veut
           rien dire dans le shader. On transporte donc le PLAN lui-meme dans le
           repere de la vue, ou tout est coherent par construction. */
        var w2v = cam.GlobalTransform.AffineInverse();
        var pv = w2v * new Plane(Vector3.Up, (float)seaY);
        var plane = new Vector4(pv.Normal.X, pv.Normal.Y, pv.Normal.Z, pv.D);
        foreach (var (gm, isSphere) in _mirrorMats)
        {
            gm.SetShaderParameter(U.Level, (float)(isSphere ? SphereLevel : RingLevel));
            gm.SetShaderParameter(U.SeaY, (float)seaY);
            gm.SetShaderParameter(U.SeaPlane, plane);
            gm.SetShaderParameter(U.Mask, MirrorMask ? 1f : 0f);
            gm.SetShaderParameter(U.FadeOn, MirrorFade ? 1f : 0f);
            gm.SetShaderParameter(U.Debug, MirrorDebug ? 1f : 0f);
        }
    }

    /// <summary>La copie s en va avec le navire : rien ne doit rester dans la scène.</summary>
    public override void _ExitTree()
    {
        /* EN DIFFÉRÉ : on sort de l'arbre au moment où Godot est justement en
           train d'en retirer des nœuds, et RemoveChild y est refusé — il le dit en
           rouge à chaque fermeture. QueueFree seul suffirait à libérer la copie,
           mais la détacher explicitement dit mieux ce qu'on veut : elle appartient
           au navire, pas à la scène où on l'avait pendue. */
        if (_mirror != null && _mirror.GetParent() is Node p)
            p.CallDeferred(Node.MethodName.RemoveChild, _mirror);
        _mirror?.QueueFree();
        _mirror = null;
    }

    /// <summary>
    /// LE TÉLÉPORTEUR VU PAR LA MER — son reflet sur l'eau et sa lumière DANS
    /// l'eau, par le même tableau que les fanaux (voir FillLamps).
    ///
    /// Il passe AVANT eux et prend donc toujours une place : les huit sont
    /// partagées par la flotte entière, et une sphère de vingt mètres qui ne se
    /// refléterait pas parce qu'un fanal de poupe avait pris le dernier créneau
    /// serait une panne difficile à comprendre.
    /// </summary>
    public int FillRingLamp(Vector4[] lamp, float[] range, Vector3[] col, float[] size, int start)
    {
        if (_ringLamp == null || start >= lamp.Length || RingLevel <= 0.01) return 0;
        /* UNE SEULE, MAIS LARGE. Cinq points répartis sur le cercle donnaient cinq
           ÉTINCELLES, et cela se voyait pour ce que c'était : cinq lampes posées
           sur l'eau. La faute n'était pas dans le nombre mais dans le modèle — la
           mer traitait chaque feu comme un point, alors que celui-ci fait vingt
           mètres de rayon. On lui dit donc sa TAILLE, et son lobe s'étale de
           lui-même en une traînée. */
        var p = _ringLamp.GlobalPosition;
        var c = _ringLamp.LightColor;
        lamp[start] = new Vector4(p.X, p.Y, p.Z, (float)(_ringLamp.LightEnergy * LampSeaGain));
        range[start] = _ringLamp.OmniRange;
        col[start] = new Vector3(c.R, c.G, c.B);
        size[start] = (float)(_apparatusR > 1 ? _apparatusR : 12);
        return 1;
    }

    /// <summary>La ligne de fiche qui parle de cet anneau, s'il y en a une.</summary>
    static RingSpec? RingFor(ModelSpec m, string name)
    {
        foreach (var r in m.Rings)
            if (string.IsNullOrEmpty(r.Match) || name.Contains(r.Match, StringComparison.OrdinalIgnoreCase))
                return r;
        return null;
    }

    /// <summary>
    /// CHARGER UN SHADER EN LE DISANT S'IL MANQUE.
    ///
    /// Un <c>ShaderMaterial</c> dont le shader est nul ne dessine RIEN, et ne se
    /// plaint pas : la pièce disparaît, tout le reste marche, et l'on cherche la
    /// panne dans la logique. Deux fichiers ajoutés hors de l'éditeur ont
    /// exactement ce profil tant que Godot ne les a pas vus passer.
    /// </summary>
    static Shader? LoadShader(string path)
    {
        var sh = GD.Load<Shader>(path);
        if (sh == null)
            GD.PushError($"{path} : shader introuvable — la pièce qui l'attend ne sera PAS dessinée. "
                       + "Godot ne l'a peut-être pas importé ; rouvrir l'éditeur une fois suffit.");
        return sh;
    }

    /// <summary>
    /// Une rampe adoucie aux deux bouts : 3t² − 2t³, la smoothstep.
    /// </summary>


    /// <summary>
    /// La normale de son plan — la direction dans laquelle il est MINCE. On
    /// accumule ici la covariance de ses sommets, et c'est le NOYAU qui en tire
    /// l'axe (<see cref="Rings.Axis"/>), parce que la page doit rendre le même
    /// chiffre et qu'une fonction pure s'éprouve sans moteur :
    /// <c>dotnet run --project core/NavalSim.Lab -- anneaux</c>.
    /// </summary>
    static Vector3 RingAxis(List<Vector3> v, Vector3 c)
    {
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in v)
        {
            double dx = p.X - c.X, dy = p.Y - c.Y, dz = p.Z - c.Z;
            xx += dx * dx; xy += dx * dy; xz += dx * dz;
            yy += dy * dy; yz += dy * dz; zz += dz * dz;
        }
        double n = v.Count;
        var a = Rings.Axis(xx / n, xy / n, xz / n, yy / n, yz / n, zz / n);
        return a.ToGodot();
    }

    // ------------------------------------------------------------------
    //  LES FAIRE TOURNER
    // ------------------------------------------------------------------

    /// <summary>
    /// Une image d'anneaux. Appelée à côté de <see cref="SwingLanterns"/>, pour
    /// tous les navires, et sans frais pour ceux qui n'en portent pas.
    ///
    /// L'ASSIETTE SE RETIRE PAR L'INVERSE. Le pivot est enfant du navire, donc
    /// son orientation dans le monde vaut <c>Q_navire · Q_local</c>. On veut
    /// qu'elle vaille le CAP SEUL ; il suffit donc d'écrire
    /// <c>Q_local = Q_navire⁻¹ · Cap</c>, et le roulis et le tangage s'en vont
    /// d'eux-mêmes, quels qu'ils soient. Aucun angle à extraire, aucun cas
    /// particulier à quatre-vingt-dix degrés, et cela tient encore sur une coque
    /// chavirée.
    ///
    /// LE CAP SE LIT SUR LE VECTEUR D'ÉTRAVE, comme partout ailleurs dans ce
    /// projet — jamais sur un angle d'Euler.
    /// </summary>
    public void SpinRings(double dt)
    {
        if (_rings.Count == 0) return;

        /* LA CHARGE MONTE ET DESCEND EN RAMPE, jamais d'un coup : c'est la rampe
           qui fait lire « il charge » plutôt que « quelqu'un a appuyé ». */
        if (ForcedLevel >= 0) RingLevel = Math.Clamp(ForcedLevel, 0, 1);
        else
        {
            double want = RingsOrdered ? 1 : 0;
            if (RingLevel != want)
            {
                double step = dt / (RingsOrdered ? Math.Max(0.01, RingsRise) : Math.Max(0.01, RingsFall));
                RingLevel = want > RingLevel ? Math.Min(want, RingLevel + step) : Math.Max(want, RingLevel - step);
            }
        }

        /* ÉTEINT NE COÛTE RIEN, et c'est tout l'intérêt de cacher plutôt que de
           laisser la lueur à zéro. Un anneau invisible ne se dessine pas, ne
           tourne pas, ne pousse aucun uniforme et ne compte dans aucune passe ;
           un anneau à zéro coûterait encore deux appels de dessin et sa géométrie
           chaque image, pour ajouter du noir. */
        /* LA BULLE MONTE AVEC LES ANNEAUX MAIS NE REDESCEND PAS AVEC EUX. En
           montant elle SUIT la charge, à partir de SphereFrom, avec une courbe
           douce aux deux bouts ; en descendant elle a sa propre chute, bien plus
           rapide — ce qui a été demandé, et ce qui est juste : la bulle est
           l'effet, les anneaux sont la machine, et l'effet cesse d'abord. */
        double veut = RingLevel <= SphereFrom ? 0
                    : MathX.Smooth01((RingLevel - SphereFrom) / Math.Max(1e-6, 1 - SphereFrom));
        SphereLevel = veut > SphereLevel
            ? veut
            : Math.Max(veut, SphereLevel - dt / Math.Max(0.01, SphereFall));

        bool bulle = SphereLevel > 0.002;
        if (_sphere != null && bulle != _sphereShown)
        {
            _sphereShown = bulle;
            _sphere.Visible = bulle;       // absente, elle ne coûte rien
        }
        if (bulle)
            foreach (var g in _sphereGlow) g.SetShaderParameter(U.Level, (float)SphereLevel);

        bool lit = RingLevel > 0.001;
        if (lit != _ringsLit)
        {
            _ringsLit = lit;
            foreach (var r in _rings) r.Pivot.Visible = lit;
        }
        if (!lit)
        {
            /* ÉTEINTE POUR DE BON : invisible ET sans ombre. Une lampe laissée
               visible à énergie nulle rendrait encore son cube d'ombre — la leçon
               des fanaux, qui coûtait une milliseconde vingt-quatre heures sur
               vingt-quatre. */
            if (_ringLamp != null && _ringLamp.Visible)
            {
                _ringLamp.Visible = false;
                _ringLamp.LightEnergy = 0;
                if (_lampShadows) { _lampShadows = false; _ringLamp.ShadowEnabled = false; }
            }
            return;
        }

        /* LA LAMPE SUIT LA CHARGE AU CARRÉ. Linéaire, elle éblouirait tout le
           long des vingt secondes ; au carré, elle n'existe vraiment que sur la
           fin, comme la bulle — et c'est la fin qui doit être spectaculaire. */
        if (_ringLamp != null)
        {
            double e = RingLevel * RingLevel;
            _ringLamp.Visible = true;
            _ringLamp.LightEnergy = (float)(LampEnergy * e);
            // du bleu au blanc, comme tout le reste de l'appareil
            _ringLamp.LightColor = new Color(0.30f, 0.52f, 1.0f).Lerp(new Color(0.88f, 0.94f, 1.0f), (float)RingLevel);
            bool om = RingLevel > LampShadowFrom;
            if (om != _lampShadows) { _lampShadows = om; _ringLamp.ShadowEnabled = om; }
        }

        _ringClock += dt;
        if (_ringClock > 3600) _ringClock -= 3600;     // les décimales d'un flottant ne durent pas une partie
        foreach (var r in _rings)
            foreach (var g in r.Glow)
            {
                g.SetShaderParameter(U.Level, (float)RingLevel);
                g.SetShaderParameter(U.Time, (float)_ringClock);
            }

        var q = Physics.Body.Quat;
        Vec3d fwd = q.Rotate(new Vec3d(0, 0, 1));
        /* atan2(x, z) et non atan2(−x, z) : ce n'est pas le cap du compas mais
           l'angle de la rotation autour de +y qui amène (0,0,1) sur l'étrave. */
        var level = new Quaternion(Vector3.Up, (float)Math.Atan2(fwd.X, fwd.Z));
        var shipInv = new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W).Normalized().Inverse();
        var model = new Quaternion(Vector3.Up, (float)(Spec.Model?.RotationY ?? 0));

        foreach (var r in _rings)
        {
            /* La phase est repliée sur un tour : une partie qui dure la ferait
               sinon monter à des dizaines de milliers de radians, où un flottant
               n'a plus assez de décimales pour un pas d'image — l'anneau se met
               à saccader au bout d'une heure de jeu. */
            /* ILS S'EMBALLENT. La vitesse ne suit pas la charge tout droit mais
               par son CUBE : trois secondes avant le saut ils tournent encore
               posément, la dernière seconde ils hurlent. Une montée linéaire sur
               vingt secondes ne se voit pas — l'œil ne compare qu'à ce qu'il vient
               de voir, et un dixième de plus par seconde n'est rien. */
            double v = 0.25 + RingLevel * (0.75 + RingBoost * RingLevel * RingLevel);
            r.Phase += r.Rate * v * dt;
            if (r.Phase > Math.Tau) r.Phase -= Math.Tau;
            else if (r.Phase < -Math.Tau) r.Phase += Math.Tau;

            var spin = new Quaternion(r.Axis, (float)r.Phase);
            r.Pivot.Quaternion = (r.Steady ? shipInv * level * model : model) * spin;
        }
    }
}
