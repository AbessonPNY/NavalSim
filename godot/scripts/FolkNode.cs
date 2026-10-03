using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// DU MONDE SUR LA PLAGE — des figurants semés au bord de l'eau.
///
/// Ils partagent tous le même maillage et la même matière, donc Godot les
/// instancie : vingt hommes coûtent à peu près ce qu'un seul coûte à dessiner.
/// Ce qui les distingue tient en deux nombres par instance — leur cap, et leur
/// DÉPHASAGE dans l'animation. Sans ce dernier, vingt silhouettes qui respirent
/// au même rythme se lisent comme un seul objet dupliqué vingt fois, ce qui est
/// pire que de n'en mettre aucun.
///
/// POSÉS À <c>centre − origine</c> comme les villes et les pontons : l'origine
/// flottante glisse sous eux, et leurs coordonnées d'instance restent petites.
///
/// LE SEMIS EST DÉTERMINISTE, tiré d'une graine prise sur le lieu : une plage
/// doit retrouver ses gens à la même place d'une partie à l'autre, sans qu'on
/// ait à les écrire quelque part.
/// </summary>
public partial class FolkNode : Node3D
{
    readonly World _world;
    readonly List<(Vec3d At, Node3D Node)> _sites = new();

    /// <summary>Les matériaux que <see cref="SkyNode.PushTo"/> doit tenir à jour.</summary>
    public readonly List<ShaderMaterial> Hazed = new();

    /// <summary>Au-delà, on ne les montre pas : une plage à trois milles n'a pas de badauds lisibles.</summary>
    public double Range = 3000;

    public FolkNode(World world) { _world = world; }

    /// <summary>Le registre de l'éditeur : chaque figurant s'y inscrit en se posant.</summary>
    public EditRegistry? Editor;

    /* CE QUI FAIT UNE PLAGE, et non un pré ni un rocher. Trois bornes, toutes
       mesurées sur le relief plutôt que dessinées à la main :

         · assez près de l'eau pour qu'on y vienne, assez loin pour n'y être pas ;
         · assez haut pour ne pas être dans la vague, assez bas pour être du sable
           et non de la falaise ;
         · et une pente douce, parce qu'on ne flâne pas sur un talus.

       Ce sont les mêmes épreuves que le semis des maisons, avec d'autres bornes :
       une maison se met à l'abri, un badaud descend voir la mer. */
    const double PresMin = 4, PresMax = 34;
    const double HautMin = 0.4, HautMax = 3.0;
    const double PenteMax = 0.22;

    /// <summary>
    /// Semer <paramref name="combien"/> figurants autour d'un point du monde. On
    /// tire au hasard dans un disque et l'on REJETTE ce qui ne va pas, plutôt que
    /// de chercher une formule : le relief est peint à la main, aucune formule ne
    /// dira où est sa plage. Un plafond d'essais évite de tourner sans fin sur une
    /// côte qui n'en a pas — et l'on dit alors combien on a pu en poser.
    /// </summary>
    public void Plant(Vat.Figure f, string nom, double cx, double cz, double rayon, int combien, uint graine)
    {
        var g = new Node3D();
        AddChild(g);
        _sites.Add((new Vec3d(cx, 0, cz), g));

        uint s = graine | 1;
        double Rnd() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216.0; }

        const double Taille = 1.75;
        double k = f.Height > 0.01 ? Taille / f.Height : 1;
        double pieds = f.Floor * k;

        int pose = 0;
        for (int essai = 0; essai < combien * 400 && pose < combien; essai++)
        {
            double a = Rnd() * Math.PI * 2, r = Math.Sqrt(Rnd()) * rayon;
            double x = cx + Math.Cos(a) * r, z = cz + Math.Sin(a) * r;

            double sol = _world.HeightAt(x, z);
            if (sol < HautMin || sol > HautMax) continue;
            double bord = _world.ShoreDistance(x, z);
            if (bord < PresMin || bord > PresMax) continue;
            // la pente, lue sur quatre mètres : un talus se voit mal sur un seul
            double pente = Math.Max(
                Math.Abs(_world.HeightAt(x + 2, z) - _world.HeightAt(x - 2, z)),
                Math.Abs(_world.HeightAt(x, z + 2) - _world.HeightAt(x, z - 2))) / 4;
            if (pente > PenteMax) continue;

            var mat = (ShaderMaterial)f.Material.Duplicate();
            var haze = HazePass.New();
            mat.NextPass = haze;
            Hazed.Add(haze);

            double yaw = Rnd() * Math.PI * 2;
            var mi = new MeshInstance3D
            {
                Mesh = f.Mesh,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On
            };
            mi.SetInstanceShaderParameter("u_phase", (float)Rnd());
            g.AddChild(mi);
            /* RETOUCHABLE COMME UNE MAISON : posé par la même fonction, qu'il ait
               bougé ou non. Ses pieds sont SOUS son origine (le point le plus bas
               de son animation), d'où le relèvement. */
            var e = new Editable
            {
                Id = $"figurant:{nom}:{pose}", Label = "un figurant", BaseX = x, BaseZ = z, BaseYaw = yaw,
                X = x, Z = z, Yaw = yaw, Radius = 0.6, Height = Taille,
                Family = "figurant", FamilyLabel = "un figurant", Lift = -pieds,
                MakeVisual = () =>
                {
                    var c = new MeshInstance3D
                    {
                        Mesh = f.Mesh, MaterialOverride = mat, Scale = Vector3.One * (float)k,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.On
                    };
                    c.SetInstanceShaderParameter("u_phase", (float)GD.Randf());
                    return c;
                }
            };
            e.Push = ed =>
            {
                double y = _world.HeightAt(ed.X, ed.Z);
                ed.GroundY = y + ed.Dy;
                mi.Position = new Vector3((float)(ed.X - cx), (float)(y - pieds + ed.Dy), (float)(ed.Z - cz));
                mi.Rotation = new Vector3(0, (float)ed.Yaw, 0);
                mi.Scale = Vector3.One * (float)(k * ed.Scale);
                mi.Visible = !ed.Removed;
            };
            if (Editor != null) Editor.Add(e); else e.Push(e);
            pose++;
        }
        if (pose < combien)
            GD.PushWarning($"{nom} : {pose} figurant(s) sur {combien} — la côte n'offre pas assez de plage.");
        else GD.Print($"{nom} : {pose} figurant(s) sur la plage");
    }

    /// <summary>Les poser contre l'origine du moment, et cacher ceux qui sont loin.</summary>
    public void Update(Vec3d centre, Vec3d origin)
    {
        foreach (var (at, node) in _sites)
        {
            double dx = at.X - centre.X, dz = at.Z - centre.Z;
            node.Visible = dx * dx + dz * dz < Range * Range;
            if (node.Visible)
                node.Position = new Vector3((float)(at.X - origin.X), 0, (float)(at.Z - origin.Z));
        }
    }
}
