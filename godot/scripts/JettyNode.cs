using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE PONTON AUTOUR DUQUEL UN PORT EST BÂTI.
///
/// IL EST CONSTRUIT, PAS CHARGÉ, tant que personne ne fournit de modèle — et
/// cet ordre-là compte : un repli écrit après l'asset est un repli que personne
/// ne regarde jamais, et ce projet a déjà payé pour le savoir.
///
/// CE QUI LE FAIT LIRE comme un ponton plutôt que comme une planche sur l'eau,
/// c'est qu'il se tient sur des JAMBES dans de l'eau d'une vraie profondeur. Le
/// tablier est de niveau, parce qu'un tablier l'est ; les pieux ne le sont pas,
/// parce que le fond ne l'est pas — chaque paire est coupée au fond qu'elle
/// touche, lu par la même HeightAt que la quille talonne. L'ouvrage prend donc
/// de la jambe à mesure qu'il marche vers le large, et c'est toute sa
/// silhouette : une rangée de poteaux égaux se lit comme une clôture.
///
/// Bâti dans le repère du PORT, et posé à <c>port − origine</c> comme les
/// carreaux de terre et les villes : l'origine flottante ne le concerne pas.
/// </summary>
public partial class JettyNode : Node3D
{
    readonly World _world;
    readonly Dictionary<string, Node3D> _built = new();

    /// <summary>Les matériaux que <see cref="SkyNode.PushTo"/> doit tenir à jour.</summary>
    public readonly List<ShaderMaterial> Hazed = new();

    /// <summary>Au-delà, on ne bâtit pas : un port qu'on ne voit pas n'a pas besoin de son quai.</summary>
    public double Range = 4500;

    StandardMaterial3D _deckMat = null!, _pileMat = null!, _bittMat = null!, _moleMat = null!;

    public JettyNode(World world) { _world = world; }

    public override void _Ready()
    {
        ShaderMaterial Haze() => new() { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
        StandardMaterial3D Tone(uint rgb)
        {
            var m = new StandardMaterial3D
            {
                AlbedoColor = Color.Color8((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb),
                Roughness = 0.88f,
                NextPass = Haze()
            };
            Hazed.Add((ShaderMaterial)m.NextPass);
            return m;
        }
        _deckMat = Tone(0x8a7a5e);          // bordages blanchis de soleil
        _pileMat = Tone(0x4f4334);          // goudronnés, et mouillés au pied
        _bittMat = Tone(0x6b5a3f);
        _moleMat = Tone(0x8b8372);          // pierre sèche, blanchie de sel
    }

    /// <summary>
    /// LE MÔLE d'un port qui en demande un, bâti des MÊMES quatre nombres que
    /// <c>HeightAt</c> lit : centre, rayon, épaisseur du mur et demi-angle de la
    /// passe. C'est la règle du plan de formes appliquée à la maçonnerie — ce qui
    /// arrête la coque est ce que l'œil voit l'arrêter, et ni l'un ni l'autre n'a
    /// eu à être écrit deux fois.
    ///
    /// Bâti SEULEMENT là où il dépasse vraiment du sol qui le porte. L'anneau
    /// continue dans la colline derrière le port, où un mur de trois mètres est
    /// simplement enterré ; l'y dessiner poserait un bandeau de pierre à flanc de
    /// coteau. Chaque tronçon demande donc au relief ce qu'il y a dessous et
    /// s'efface quand la terre est plus haute — ce qui lui donne du même coup ses
    /// racines sur la plage, pour rien.
    /// </summary>
    void Mole(Isle isl, Node3D g)
    {
        if (isl.Port.Harbour is not Harbour H) return;
        const int N = 132;
        double R0 = H.R, R1 = H.R + H.Wall;
        var pos = new List<Vector3>();
        var idx = new List<int>();
        int v = 0;
        for (int i = 0; i < N; i++)
        {
            double a0 = (double)i / N * Math.Tau, a1 = (double)(i + 1) / N * Math.Tau;
            double mid = (a0 + a1) * 0.5;
            double da = mid - H.Ang;
            while (da > Math.PI) da -= Math.Tau;
            while (da < -Math.PI) da += Math.Tau;
            if (Math.Abs(da) < H.Gap) continue;                     // la passe
            double t = Math.Min(1, (Math.Abs(da) - H.Gap) / 0.10);  // musoirs adoucis
            double top = H.Top * t;

            // le terrain sous ce tronçon : enterré, on ne le dessine pas
            double cx = H.Cx + Math.Cos(mid) * (R0 + R1) * 0.5;
            double cz = H.Cz + Math.Sin(mid) * (R0 + R1) * 0.5;
            double ground = _world.IslandHeight(cx, cz);
            if (ground > top - 0.15) continue;

            /* Cinq points par tronçon : le pied extérieur, l'arête extérieure,
               l'arase, l'arête intérieure et le pied intérieur. Un môle est un
               tas de blocs, donc ses flancs FUIENT — c'est ce qui le distingue
               d'un mur, et c'est aussi ce qui le fait tenir. */
            double foot = Math.Max(-9, ground) - 0.4;
            (double R, double Y)[] prof = { (R1 + 7, foot), (R1, top), (R0, top), (R0 - 7, foot) };
            foreach (double ang in new[] { a0, a1 })
            {
                double c = Math.Cos(ang), s2 = Math.Sin(ang);
                foreach (var (r, y) in prof) pos.Add(new Vector3((float)(c * r), (float)y, (float)(s2 * r)));
            }
            int b0 = v;
            for (int k = 0; k < 3; k++)
            {
                idx.Add(b0 + k); idx.Add(b0 + 4 + k); idx.Add(b0 + 4 + k + 1);
                idx.Add(b0 + k); idx.Add(b0 + 4 + k + 1); idx.Add(b0 + k + 1);
            }
            v += 8;
        }
        if (pos.Count == 0) return;

        var vert = new float[pos.Count * 3];
        for (int k = 0; k < pos.Count; k++)
        {
            vert[k * 3] = pos[k].X; vert[k * 3 + 1] = pos[k].Y; vert[k * 3 + 2] = pos[k].Z;
        }
        var nrm = new float[vert.Length];
        NavalSim.Core.SailCloth.ComputeNormals(vert, nrm, idx.ToArray());
        var normals = new Vector3[pos.Count];
        for (int k = 0; k < pos.Count; k++)
            normals[k] = new Vector3(nrm[k * 3], nrm[k * 3 + 1], nrm[k * 3 + 2]);

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = pos.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = normals;
        arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);

        g.AddChild(new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = _moleMat,
            Position = new Vector3((float)(H.Cx - isl.X), 0, (float)(H.Cz - isl.Z)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            ExtraCullMargin = 200
        });
    }

    /// <summary>Bâtir le quai d'un port, une fois.</summary>
    void Build(Isle isl)
    {
        var p = isl.Port;
        var g = new Node3D();
        AddChild(g);
        _built[isl.Key] = g;
        Mole(isl, g);

        double ca = Math.Cos(p.Ang), sa = Math.Sin(p.Ang);
        double r0 = p.ShoreR - 6, r1 = p.ShoreR + p.Reach;
        double len = r1 - r0, W = Berth.Width, hw = W * 0.5, deckY = Berth.DeckY;

        // le repère du ponton : la racine à l'origine, +x vers le large
        var frame = new Node3D
        {
            Position = new Vector3((float)(ca * r0), 0, (float)(sa * r0)),
            Rotation = new Vector3(0, (float)-p.Ang, 0)
        };
        g.AddChild(frame);

        Node3D Put(Mesh m, Material mat, Vector3 at, Vector3? rot = null, Vector3? scale = null)
        {
            var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = at };
            if (rot is Vector3 r) mi.Rotation = r;
            if (scale is Vector3 s) mi.Scale = s;
            frame.AddChild(mi);
            return mi;
        }

        // ---- le tablier, une longue caisse de niveau d'un bout à l'autre
        Put(new BoxMesh { Size = new Vector3((float)len, 0.28f, (float)W) }, _deckMat,
            new Vector3((float)(len * 0.5), (float)deckY, 0));

        /* LE BORDAGE COURT EN TRAVERS. Un ponton est ponté d'un bau à l'autre,
           si bien que les planches sont courtes et qu'on remplace celle qui
           pourrit ; posées en long, il en faudrait d'aussi longues que le quai.
           C'est aussi la direction qui le fait lire d'un coup d'œil, les lignes
           d'équerre avec le chemin qu'on suit. */
        int planks = (int)(len / 0.42);
        if (planks > 0)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new BoxMesh { Size = new Vector3(0.30f, 0.06f, (float)(W * 0.98)) },
                InstanceCount = planks
            };
            for (int i = 0; i < planks; i++)
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity,
                    new Vector3((float)(0.30 + i * 0.42), (float)(deckY + 0.17), 0)));
            frame.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _deckMat });
        }

        // ---- les jambes, chacune coupée au fond qu'elle touche
        // une paire tous les quatre mètres : la travée qu'un bau franchit en bois
        int bays = Math.Max(3, (int)Math.Round(len / 4.0));
        var pileMesh = new CylinderMesh { TopRadius = 0.19f, BottomRadius = 0.22f, Height = 1, RadialSegments = 7 };
        for (int i = 0; i <= bays; i++)
        {
            double along = (double)i / bays * len;
            foreach (int side in new[] { -1, 1 })
            {
                double wx = isl.X + ca * (r0 + along) - sa * side * hw;
                double wz = isl.Z + sa * (r0 + along) + ca * side * hw;
                /* Le vrai fond sous cette jambe, par la même HeightAt que la
                   coque talonne. Rien n'est posé à l'œil : marchez vers le
                   large et l'eau se creuse sous le quai parce que le fond le
                   dit. */
                double bed = Math.Min(-0.4, _world.HeightAt(wx, wz));
                double h = deckY - bed + 0.2;
                Put(pileMesh, _pileMat,
                    new Vector3((float)along, (float)(bed + h * 0.5 - 0.1), (float)(side * hw)),
                    null, new Vector3(1, (float)h, 1));

                // une contrefiche de la tête vers l'extérieur : c'est elle qui raidit
                if (i > 0 && h > 2.2)
                    Put(pileMesh, _pileMat,
                        new Vector3((float)(along - 2.0), (float)(deckY - 0.9), (float)(side * (hw + 0.42))),
                        new Vector3(0, (float)(side * 0.16), (float)Math.Atan2(4.0, 1.6)),
                        new Vector3(0.55f, (float)Math.Sqrt(4.0 * 4.0 + 1.6 * 1.6), 0.55f));
            }
        }

        /* LA PASSERELLE D'EMBARQUEMENT, et c'est ce qui fait d'un appontement un
           quai de commerce : sans elle on voit bien un navire à côté d'un
           ponton, et rien qui dise par où les caisses passent. Posée et non
           attachée à la coque — la rattacher demanderait de la redessiner à
           chaque image pendant que le navire évite sur ses amarres, pour une
           planche qu'on regarde deux secondes en chargeant. */
        double gLen = 5.4, gAt = len * 0.72, gw = 1.7;
        var gang = new Node3D
        {
            Position = new Vector3((float)gAt, (float)(deckY + 0.05), (float)hw),
            Rotation = new Vector3(0.22f, 0, 0)          // elle descend vers l'eau
        };
        frame.AddChild(gang);
        gang.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3((float)gw, 0.14f, (float)gLen) },
            MaterialOverride = _deckMat,
            Position = new Vector3(0, 0, (float)(gLen * 0.5))
        });
        // les lisses : ce sont elles qui la font lire comme une passerelle
        var railMesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, (float)(gLen * 0.96)) };
        var stanMesh = new BoxMesh { Size = new Vector3(0.07f, 0.62f, 0.07f) };
        foreach (int sx in new[] { -1, 1 })
        {
            gang.AddChild(new MeshInstance3D
            {
                Mesh = railMesh, MaterialOverride = _pileMat,
                Position = new Vector3((float)(sx * gw * 0.45), 0.62f, (float)(gLen * 0.5))
            });
            for (int i = 0; i < 3; i++)
                gang.AddChild(new MeshInstance3D
                {
                    Mesh = stanMesh, MaterialOverride = _pileMat,
                    Position = new Vector3((float)(sx * gw * 0.45), 0.31f, (float)(gLen * (0.18 + i * 0.32)))
                });
        }

        /* Et du fret sur le quai, à côté d'elle. Trois fûts et deux caisses ne
           coûtent rien et disent ce que la passerelle ne peut pas dire seule :
           que ce quai sert à charger. */
        var barrel = new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0.42f, Height = 0.95f, RadialSegments = 9 };
        var crate = new BoxMesh { Size = new Vector3(1.05f, 0.85f, 1.05f) };
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)isl.Key.GetHashCode();
        (double At, int Sx, bool Cask)[] fret =
        {
            (gAt - 4.2, -1, true), (gAt - 5.1, -1, true), (gAt - 4.6, 1, false),
            (gAt - 6.4, -1, false), (gAt - 2.6, -1, true)
        };
        foreach (var (at, sx, cask) in fret)
            Put(cask ? barrel : crate, _bittMat,
                new Vector3((float)at, (float)(deckY + 0.17 + (cask ? 0.475 : 0.425)), (float)(sx * (hw - 1.15))),
                new Vector3(0, rng.Randf() * 1.2f, 0));

        /* LES BITTES au musoir, et elles sont la raison d'être de tout
           l'ouvrage : un ponton existe pour qu'on puisse s'y amarrer. Deux au
           bout, là où une coque à couple prend ses bouts — et une à la racine,
           pour qu'une garde puisse revenir vers la plage : une seule paire au
           bout la laisserait éviter sur deux amarres. */
        var bitt = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.19f, Height = 1.0f, RadialSegments = 8 };
        foreach (int side in new[] { -1, 1 })
            Put(bitt, _bittMat, new Vector3((float)(len - 1.6), (float)(deckY + 0.65), (float)(side * (hw - 0.45))));
        Put(bitt, _bittMat, new Vector3(2.2f, (float)(deckY + 0.65), (float)(hw - 0.45)));
    }

    /// <summary>Bâtir ce qui est à portée, poser tout le monde contre l'origine.</summary>
    public void Update(Vec3d centre, Vec3d origin)
    {
        foreach (var isl in _world.Isles)
        {
            double dx = isl.X - centre.X, dz = isl.Z - centre.Z;
            bool near = dx * dx + dz * dz < Range * Range;
            if (near && !_built.ContainsKey(isl.Key)) Build(isl);
            if (!_built.TryGetValue(isl.Key, out var g)) continue;
            g.Visible = near;
            if (near) g.Position = new Vector3((float)(isl.X - origin.X), 0, (float)(isl.Z - origin.Z));
        }
    }
}
