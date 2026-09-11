using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// Une coque à l'écran, et le pont entre le solveur et le graphe de scène.
///
/// ELLE NE DÉCIDE DE RIEN. Le solveur vit dans le noyau, en doubles, sans
/// connaître Godot ; ce nœud lit <c>Body.Pos</c> et <c>Body.Quat</c> une fois par
/// image et les recopie dans une transformée. C'est la FRONTIÈRE dont il a été
/// question depuis le début : le seul endroit où l'on passe du double au
/// flottant 32 bits, une fois par image et par objet, là où cela ne coûte rien.
///
/// Le maillage sort du même <see cref="HullLines"/> que la grille de sondes, ce
/// qui est l'invariant le plus important du projet : ce qu'on voit est ce qui
/// flotte.
/// </summary>
public partial class ShipNode : Node3D
{
    public ShipSpec Spec { get; private set; } = null!;
    public HullLines Lines { get; private set; } = null!;
    public ShipPhysics Physics { get; private set; } = null!;
    public Controls Ctrl { get; } = new();

    MeshInstance3D _hull = null!;
    Node3D _rig = null!;

    public void Build(ShipSpec spec)
    {
        Spec = spec;
        Lines = new HullLines(spec);
        Physics = new ShipPhysics(spec, Lines);

        _hull = new MeshInstance3D
        {
            Mesh = ToArrayMesh(Lines.BuildGeometry()),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.22f, 0.26f, 0.31f),
                Roughness = 0.72f,
                // la coque est une nappe fermée par deux culs : on voit son
                // intérieur quand elle gîte, donc on dessine les deux faces
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            }
        };
        AddChild(_hull);

        BuildRig();
    }

    /// <summary>
    /// Des mâts, et ils ne sont pas décoratifs : une coque seule ne montre pas
    /// son roulis. Le pont suit la tonture et l'œil n'a aucune verticale à quoi
    /// comparer l'inclinaison, si bien qu'un navire qui roule de dix degrés a
    /// l'air d'une coque posée à plat sur une mer penchée. Un espar vertical
    /// règle cela d'un trait.
    ///
    /// Ils sortent de la fiche — <c>rig.masts</c> — donc un bâtiment sans
    /// gréement n'en porte pas, ce qui est le comportement voulu.
    /// </summary>
    void BuildRig()
    {
        _rig = new Node3D();
        AddChild(_rig);

        var timber = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.69f, 0.55f, 0.36f),
            Roughness = 0.85f
        };

        foreach (var m in Spec.Masts)
        {
            if (m.Height <= 0) continue;
            double deckY = Lines.DeckY(m.Z / Spec.L + 0.5);
            var mast = new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = (float)(Spec.L * 0.006),
                    BottomRadius = (float)(Spec.L * 0.010),
                    Height = (float)m.Height,
                    RadialSegments = 8
                },
                MaterialOverride = timber,
                Position = new Vector3(0, (float)(deckY + m.Height * 0.5), (float)m.Z)
            };
            _rig.AddChild(mast);

            // une vergue, en travers, pour que le lacet se lise aussi
            if (m.Boom > 0)
                _rig.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh
                    {
                        TopRadius = (float)(Spec.L * 0.004),
                        BottomRadius = (float)(Spec.L * 0.004),
                        Height = (float)m.Boom,
                        RadialSegments = 6
                    },
                    MaterialOverride = timber,
                    Position = new Vector3(0, (float)(deckY + m.Height * 0.72), (float)m.Z),
                    RotationDegrees = new Vector3(0, 0, 90)
                });
        }
    }

    static ArrayMesh ToArrayMesh(in HullMesh hm)
    {
        var verts = new Vector3[hm.Positions.Length / 3];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = new Vector3(hm.Positions[i * 3], hm.Positions[i * 3 + 1], hm.Positions[i * 3 + 2]);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (int idx in hm.Indices) st.AddVertex(verts[idx]);
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>
    /// LA FRONTIÈRE. Double vers flottant, une fois par image.
    /// </summary>
    public void SyncTransform()
    {
        var b = Physics.Body;
        Position = new Vector3((float)b.Pos.X, (float)b.Pos.Y, (float)b.Pos.Z);
        Quaternion = new Quaternion(
            (float)b.Quat.X, (float)b.Quat.Y, (float)b.Quat.Z, (float)b.Quat.W).Normalized();
    }

    /// <summary>Sa gîte et son assiette, en degrés, lues sur ses propres axes.</summary>
    public (double Heel, double Trim, double Heading) Attitude()
    {
        var q = Physics.Body.Quat;
        Vec3d up = q.Rotate(new Vec3d(0, 1, 0));
        Vec3d fwd = q.Rotate(new Vec3d(0, 0, 1));
        Vec3d right = q.Rotate(new Vec3d(-1, 0, 0));

        /* LA GÎTE SE MESURE CONTRE LA VERTICALE DU MONDE, jamais contre ses
           propres axes — et l'avoir écrite autrement donnait un instrument qui
           affichait zéro quoi qu'elle fasse.

           La faute : `up · right`. Les deux sont tournés par le MÊME quaternion,
           donc ils restent orthonormés et leur produit scalaire vaut zéro par
           construction, pour toute orientation. Le cadran était donc
           mathématiquement incapable d'afficher autre chose, ce qui est pire
           qu'un cadran faux : il a l'air de fonctionner.

           Ce qu'il faut lire est la composante VERTICALE de son vecteur tribord.
           Elle descend quand elle donne de la bande à tribord, et c'est cela que
           l'œil appelle gîter. `atan2` plutôt qu'`asin` pour que la lecture reste
           juste au-delà de quatre-vingt-dix degrés, une coque chavirée n'étant
           pas un cas qu'on peut écarter dans ce projet. */
        double heel = Math.Atan2(-right.Y, up.Y) * 180 / Math.PI;

        // L'assiette, elle, était juste : `fwd.Y` est DÉJÀ pris contre la
        // verticale du monde, puisque c'est sa composante en y.
        double trim = Math.Asin(Math.Clamp(fwd.Y, -1, 1)) * 180 / Math.PI;

        /* LE CAP SE LIT SUR LE VECTEUR D'ÉTRAVE, pas sur l'angle d'Euler. Le
           prendre sur Euler donnait un signe inverse, qui annulait exactement une
           erreur de sens du gouvernail : les deux fautes se masquaient
           mutuellement. Et l'EST EST LE −X du monde, par la même conséquence qui
           met tribord en −x local. */
        double heading = Math.Atan2(-fwd.X, fwd.Z) * 180 / Math.PI;
        if (heading < 0) heading += 360;

        return (heel, trim, heading);
    }
}
