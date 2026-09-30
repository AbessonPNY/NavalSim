using Godot;
using System;
using System.Collections.Generic;
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

    sealed class Ring
    {
        public Node3D Pivot = null!;
        /// <summary>La normale de son plan, dans le repère du MODÈLE.</summary>
        public Vector3 Axis;
        /// <summary>rad/s — le signe donne le sens.</summary>
        public double Rate;
        public double Phase;
        /// <summary>Vrai : il ignore le roulis et le tangage. Faux : il roule avec la coque.</summary>
        public bool Steady;
    }

    readonly List<Ring> _rings = new();
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
    /// On ne DESCEND PAS dans un anneau reconnu : il part entier, avec ses
    /// sous-pièces (un anneau modelé en deux demi-tores, ses ferrures, la
    /// géométrie de sa lueur). Le découper en morceaux qui tourneraient chacun
    /// autour de leur propre centre l'ouvrirait en fleur.
    /// </summary>
    static List<(Node3D Node, Transform3D Parent)> TakeRings(Node3D obj)
    {
        var found = new List<(Node3D, Transform3D)>();
        var stack = new Stack<(Node Node, Transform3D Acc)>();
        stack.Push((obj, Transform3D.Identity));
        while (stack.Count > 0)
        {
            var (n, acc) = stack.Pop();
            if (n != obj && n is Node3D nd && RingNames.IsMatch(nd.Name))
            {
                found.Add((nd, acc));
                continue;
            }
            // acc mène du repère PARENT de n au repère du modèle ; pour ses
            // enfants, le repère parent est celui de n lui-même
            var next = n != obj && n is Node3D c3 ? acc * c3.Transform : acc;
            foreach (var kid in n.GetChildren()) stack.Push((kid, next));
        }
        foreach (var (nd, _) in found) nd.GetParent()?.RemoveChild(nd);
        return found;
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

            var sp = RingFor(m, nd.Name);
            /* DEUX ANNEAUX À LA MÊME VITESSE SE RETROUVENT toujours dans la même
               figure, et l'ensemble se lit comme une pièce unique. Faute de fiche,
               chacun tourne à −0,618 fois le précédent : le sens s'inverse, et le
               rapport n'étant pas une fraction simple, ils ne se réalignent
               jamais tout à fait. */
            double rpm = sp?.Rpm ?? BaseRpm * Math.Pow(-0.618, i);
            _rings.Add(new Ring
            {
                Pivot = pivot,
                Axis = axis,
                Rate = rpm * Math.Tau / 60,
                Steady = sp?.Steady ?? true
            });
            _ringPivots.Add(pivot);
            GD.Print(FormattableString.Invariant(
                $"[{Spec.Id}] anneau {nd.Name} : axe ({axis.X:F2}, {axis.Y:F2}, {axis.Z:F2}), {rpm:F1} tr/min"));
        }
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
        return new Vector3((float)a.X, (float)a.Y, (float)a.Z);
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
            r.Phase += r.Rate * dt;
            if (r.Phase > Math.Tau) r.Phase -= Math.Tau;
            else if (r.Phase < -Math.Tau) r.Phase += Math.Tau;

            var spin = new Quaternion(r.Axis, (float)r.Phase);
            r.Pivot.Quaternion = (r.Steady ? shipInv * level * model : model) * spin;
        }
    }
}
