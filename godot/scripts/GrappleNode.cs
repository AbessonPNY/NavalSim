using Godot;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES FILINS D'ABORDAGE, DESSINÉS — un tube par bout, et le crochet au vol.
///
/// PAS LE CORDAGE PARTAGÉ, et ce n'est pas un oubli. Celui-ci simule des chaînes
/// de Verlet et les peint par un shader qui lit une texture de points : c'est ce
/// qu'il faut pour des bouts rompus qui PENDENT et se balancent. Un filin
/// d'abordage est tendu entre deux points connus — il n'a ni mou à simuler, ni
/// forme à trouver. Un cylindre orienté suffit, et coûte le dixième.
///
/// UNE RÉSERVE BÂTIE AU DÉMARRAGE, comme partout ici : on ne fait pas naître de
/// géométrie pendant un abordage, qui est le moment où l'image a le moins de
/// marge. Les tubes inutilisés sont cachés, ce qui ne coûte rien.
/// </summary>
public partial class GrappleNode : Node3D
{
    /// <summary>Le plafond : quatre crochets par volée, deux volées en l'air au pire.</summary>
    const int Max = 12;

    readonly List<MeshInstance3D> _ropes = new();
    readonly List<MeshInstance3D> _hooks = new();

    /// <summary>La matière du chanvre, que le ciel doit tenir à jour comme le reste.</summary>
    public readonly List<ShaderMaterial> Hazed = new();

    public override void _Ready()
    {
        /* UN CYLINDRE DE RAYON 1 ET DE HAUTEUR 1, mis à l'échelle par bout : une
           seule géométrie pour tous les filins, et l'échelle fait le reste. Godot
           dresse ses cylindres sur +y, donc on les couche en les tournant. */
        var tube = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 6, Rings = 1 };
        var hook = new BoxMesh { Size = new Vector3(1, 1, 1) };

        var chanvre = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.34f, 0.28f, 0.19f),
            Roughness = 0.95f, Metallic = 0,
            /* PAS D'OMBRE. Un bout de deux centimètres qui porte une ombre coûte une
               carte d'ombre entière pour un trait qu'on ne verrait pas ; et la leçon
               des fanaux vaut ici aussi. */
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel
        };
        var fer = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.22f, 0.24f), Roughness = 0.45f, Metallic = 0.85f
        };

        for (int i = 0; i < Max; i++)
        {
            var r = new MeshInstance3D { Mesh = tube, MaterialOverride = chanvre, Visible = false,
                                         CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            var h = new MeshInstance3D { Mesh = hook, MaterialOverride = fer, Visible = false,
                                         CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(r); AddChild(h);
            _ropes.Add(r); _hooks.Add(h);
        }
    }

    /// <summary>
    /// Une image de filins. <paramref name="origin"/> est l'origine flottante : les
    /// corps vivent en mètres LOCAUX, donc il n'y a rien à retrancher ici — mais le
    /// nœud est posé à zéro et suit la scène, comme les navires.
    ///
    /// UN CROCHET QUI VOLE N'EST PAS ENCORE UN FILIN. Tant qu'il vole, on tend le
    /// bout jusqu'où il en est de sa course, et le fer est à son bout : on VOIT le
    /// crochet partir, et l'on voit ceux qui manquent retomber. C'est ce qui fait la
    /// différence entre une volée et un interrupteur.
    /// </summary>
    public void Sync(Grapple g)
    {
        int n = 0;
        foreach (var l in g.Lines)
        {
            if (n >= Max) break;
            var a = l.From.Body.Pos + l.From.Body.Quat.Rotate(l.A);
            var b = l.To.Body.Pos + l.To.Body.Quat.Rotate(l.B);

            // en vol : le bout ne va que jusqu'où le crochet en est
            double k = l.Fly > 0 ? 1.0 - l.Fly / Grapple.Vol : 1.0;
            var pa = a.ToGodot();
            var pb = b.ToGodot();
            var tip = pa.Lerp(pb, (float)k);

            /* LE FILIN PEND UN PEU quand il a du mou, et se tend quand il tire. Un
               trait parfaitement droit se lit comme une barre de fer ; ce demi-mètre
               de flèche suffit à le rendre au chanvre. On le rend en COUCHANT le
               tube sur la corde et en abaissant son milieu — pas en le découpant :
               une flèche de cette taille ne demande pas de segments. */
            float len = pa.DistanceTo(tip);
            if (len < 0.05f) { _ropes[n].Visible = false; _hooks[n].Visible = false; n++; continue; }
            var mid = (pa + tip) * 0.5f;
            float mou = l.Fly > 0 ? 0f : (float)Mathf.Max(0, l.Len - len) * 0.25f;
            mid.Y -= Mathf.Min(mou, 0.8f);

            var r = _ropes[n];
            r.Visible = true;
            r.Position = mid;
            r.LookAtFromPosition(mid, tip, Vector3.Up);
            // le cylindre est dressé sur +y : on le couche sur l'axe de visée (−z)
            r.RotateObjectLocal(Vector3.Right, Mathf.Pi * 0.5f);
            float ep = l.Strain > 1 ? 0.045f : 0.035f;     // il grossit quand il tire
            r.Scale = new Vector3(ep, len, ep);

            var h = _hooks[n];
            h.Visible = true;
            h.Position = tip;
            h.Scale = new Vector3(0.28f, 0.28f, 0.45f);
            h.Basis = r.Basis;
            n++;
        }
        for (; n < Max; n++) { _ropes[n].Visible = false; _hooks[n].Visible = false; }
    }
}
