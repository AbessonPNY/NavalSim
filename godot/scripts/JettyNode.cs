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

    /* UN FIGURANT SUR LE QUAI, chargé UNE FOIS pour tous les pontons : maillage
       et matière partagés, donc Godot les instancie et quatorze ports coûtent un
       appel de dessin. Ce qui les distingue est leur déphasage, posé par
       instance — deux hommes qui respirent au même rythme se lisent comme un
       seul objet dupliqué. */
    Vat.Figure? _figure;
    bool _figureCherchee;


    public JettyNode(World world) { _world = world; }

    public override void _Ready()
    {
        ShaderMaterial Haze() => HazePass.New();
        StandardMaterial3D Tone(uint rgb)
        {
            var m = new StandardMaterial3D
            {
                AlbedoColor = ColorX.Rgb(rgb),
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

        // ---- la charpente : tablier, bordage, jambes et bittes, la même que celle des pontons de la fiche
        Timber(frame, len, W, isl.X + ca * r0, isl.Z + sa * r0, p.Ang);

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
           que ce quai sert à charger.

           RIEN AU DÉBARCADÈRE, pour cette raison exactement : il ne sert pas à
           charger des fûts. Un quai nu se lit tout de suite comme un endroit où
           l'on aborde et d'où l'on repart, et c'est ce qu'on veut qu'il dise.

           ET SEULEMENT LE FRET. Ceci a d'abord été écrit `if (isl.Wild) return;`
           juste au-dessus, ce qui emportait aussi LES BITTES — vingt lignes plus
           bas, et dont le commentaire dit qu'elles sont la raison d'être de tout
           l'ouvrage. Un débarcadère sans bitte n'est pas plus sauvage : c'est une
           planche sur l'eau à laquelle on ne peut pas s'amarrer. */
        if (!isl.Wild)
        {
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
        }

        /* ET QUELQU'UN QUI ATTEND. Un quai désert se lit comme un décor ; un
           homme dessus se lit comme un port. Il est posé comme le fret — sur le
           tablier, d'un bord —, mais aux deux tiers du musoir plutôt qu'au pied
           de la passerelle : on le veut visible de la mer, c'est de là qu'on
           arrive.

           PAS AU DÉBARCADÈRE : un enclos à cochons n'a pas de badaud, et c'est
           la même raison qui lui refuse son fret. */
        if (!isl.Wild) Figurant(frame, len, deckY, hw, isl.Key);

    }

    /// <summary>
    /// LA CHARPENTE D'UN PONTON — tablier, bordage, jambes et bittes —, la même pour le
    /// quai d'un port et pour un ponton que la fiche pose. <paramref name="x0"/>,
    /// <paramref name="z0"/> : sa racine en mètres VRAIS (les jambes y lisent le fond) ;
    /// <paramref name="ang"/> : la direction du large. Une rangée de jambes tous les
    /// cinq mètres en travers : un tablier de quatorze mètres sur deux rangées
    /// seulement aurait des baux qu'aucun bois ne franchit.
    /// </summary>
    void Timber(Node3D frame, double len, double W, double x0, double z0, double ang)
    {
        double ca = Math.Cos(ang), sa = Math.Sin(ang), hw = W * 0.5, deckY = Berth.DeckY;
        int nRows = W <= 8 ? 2 : (int)Math.Ceiling(W / 5.0) + 1;
        var rows = new double[nRows];
        for (int r = 0; r < nRows; r++) rows[r] = -hw + W * r / (nRows - 1);
        // LE MODÈLE, s'il est là : ses pièces assemblées ; sinon le dessin du code, ci-dessous
        if (PartsFor(W) is { } parts) { TimberFromParts(frame, len, W, x0, z0, ang, rows, parts); return; }
        void Put(Mesh m, Material mat, Vector3 at, Vector3? rot = null, Vector3? scale = null)
        {
            var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = at };
            if (rot is Vector3 rr) mi.Rotation = rr;
            if (scale is Vector3 s) mi.Scale = s;
            frame.AddChild(mi);
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
            foreach (double side in rows)
            {
                bool outer = Math.Abs(Math.Abs(side) - hw) < 1e-6;
                double wx = x0 + ca * along - sa * side;
                double wz = z0 + sa * along + ca * side;
                /* Le vrai fond sous cette jambe, par la même HeightAt que la
                   coque talonne. Rien n'est posé à l'œil : marchez vers le
                   large et l'eau se creuse sous le quai parce que le fond le
                   dit. */
                double bed = Math.Min(-0.4, _world.HeightAt(wx, wz));
                double h = deckY - bed + 0.2;
                Put(pileMesh, _pileMat,
                    new Vector3((float)along, (float)(bed + h * 0.5 - 0.1), (float)side),
                    null, new Vector3(1, (float)h, 1));

                // une contrefiche de la tête vers l'extérieur : c'est elle qui raidit
                if (outer && i > 0 && h > 2.2)
                    Put(pileMesh, _pileMat,
                        new Vector3((float)(along - 2.0), (float)(deckY - 0.9), (float)(side + Math.Sign(side) * 0.42)),
                        new Vector3(0, (float)(Math.Sign(side) * 0.16), (float)Math.Atan2(4.0, 1.6)),
                        new Vector3(0.55f, (float)Math.Sqrt(4.0 * 4.0 + 1.6 * 1.6), 0.55f));
            }
        }

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

    /* ------------------------------------------------------------------ */
    /*  LE PONTON EN .glb : ses pièces                                     */
    /* ------------------------------------------------------------------ */

    /// <summary>Les pièces d'un ponton modelé (tools/jetty-glb.js) : chacune ses maillages, dans le repère du fichier.</summary>
    sealed class Parts
    {
        public readonly List<(Mesh Mesh, Transform3D Xf)> Bay = new(), Pile = new(), Bitt = new();
        public double BayL = 4, NominalW;
    }
    readonly Dictionary<string, Parts?> _parts = new();

    /// <summary>
    /// LES PIÈCES DU PONTON DE CETTE LARGEUR — world/models/ponton-petit.glb (7 m) ou
    /// ponton-grand.glb (14 m), lues une fois. On les retrouve par le NOM du nœud
    /// (travee, pieu, bitte, ou un parent qui le porte), pas par les matières, qui
    /// restent libres dans Blender. Absent, illisible, ou sans travée : rien, et le
    /// ponton garde le dessin du code.
    /// </summary>
    Parts? PartsFor(double W)
    {
        bool small = W <= 10;
        /* LE MODÈLE DU JOUEUR D'ABORD (props/ponton.glb, props/ponton_large.glb), puis
           les pièces écrites par tools/jetty-glb.js. Le premier qui existe l'emporte. */
        string[] tries = small ? new[] { "props/ponton.glb", "world/models/ponton-petit.glb" }
                               : new[] { "props/ponton_large.glb", "world/models/ponton-grand.glb" };
        string rel = tries[^1];
        foreach (var t in tries) if (System.IO.File.Exists(Assets.Path(t))) { rel = t; break; }
        if (_parts.TryGetValue(rel, out var have)) return have;
        Parts? outp = null;
        string path = Assets.Path(rel);
        if (System.IO.File.Exists(path))
        {
            if (Assets.LoadGlb(path) is Node3D root)
            {
                var p = new Parts { NominalW = small ? Berth.Width : 2 * Berth.Width };
                var haze = HazePass.New();
                Hazed.Add(haze);
                var done = new HashSet<Material>();
                Aabb? bay = null;
                var stack = new Stack<(Node N, Transform3D Xf, string Role)>();
                stack.Push((root, Transform3D.Identity, ""));
                while (stack.Count > 0)
                {
                    var (n, xf, role) = stack.Pop();
                    string nm = n.Name.ToString().ToLowerInvariant();
                    if (nm.Contains("travee") || nm.Contains("travée")) role = "travee";
                    else if (nm.Contains("pieu")) role = "pieu";
                    else if (nm.Contains("bitte")) role = "bitte";
                    if (n is MeshInstance3D mi && mi.Mesh != null && role.Length > 0)
                    {
                        // la brume par-dessus, comme tout ce qui est à terre
                        for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                            if (mi.GetActiveMaterial(i) is BaseMaterial3D bm && done.Add(bm))
                            {
                                var last = (Material)bm;
                                while (last.NextPass != null) last = last.NextPass;
                                last.NextPass = haze;
                            }
                        (role == "travee" ? p.Bay : role == "pieu" ? p.Pile : p.Bitt).Add((mi.Mesh, xf));
                        if (role == "travee") { var b = xf * mi.Mesh.GetAabb(); bay = bay is Aabb a0 ? a0.Merge(b) : b; }
                    }
                    foreach (var c in n.GetChildren())
                        stack.Push((c, c is Node3D c3 ? xf * c3.Transform : xf, role));
                }
                // la longueur d'une travée se lit sur elle, le chapeau qui déborde à l'origine compris
                if (bay is Aabb ba) p.BayL = Math.Max(0.5, ba.End.X);
                /* UN TRONÇON D'UNE PIÈCE, sans noms : tout le modèle est la travée, jambes
                   comprises. On ne sait ni son échelle ni son sens : il est couché dans
                   l'axe du ponton (sa plus grande longueur en plan va vers le large), mis
                   à la largeur nominale sans être déformé, posé le dessus du tablier à
                   hauteur de bordage. Ses jambes sont les siennes : elles ne se
                   recoupent pas au fond — elles s'y enfoncent ou s'arrêtent dans l'eau. */
                if (p.Bay.Count == 0) WholeModule(root, p, rel);
                if (p.Bay.Count > 0) outp = p;
                else GD.PushWarning($"[ponton] {rel} : ni pièce « travee », ni maillage — dessiné par le code.");
            }
            else GD.PushWarning($"[ponton] {rel} illisible — dessiné par le code.");
        }
        return _parts[rel] = outp;
    }

    /// <summary>
    /// TOUT LE MODÈLE COMME UNE TRAVÉE — un tronçon d'une pièce (props/ponton.glb).
    /// On ne sait ni son échelle ni son sens, alors on les LIT sur lui :
    ///
    ///   · le sens : sa plus grande longueur en plan va vers le large ;
    ///   · le TABLIER : la surface tournée vers le haut la plus étendue (les
    ///     planches), à la hauteur du bordage du code ;
    ///   · l'échelle : en TRAVERS, la largeur du ponton ; en long et en hauteur,
    ///     celle où ce qui dépasse du tablier (le garde-corps) fait 1,10 m — mis à
    ///     quatorze mètres de large sans être déformé, il faisait des garde-corps de
    ///     près de trois mètres ;
    ///   · les PIEDS (demandé : « j'ai oublié d'allonger les pieds ») : tout ce qui
    ///     est sous le tablier, passé une épaisseur de charpente, est étiré vers le
    ///     bas jusqu'à neuf mètres sous l'eau. Le fond les cache là où il est plus
    ///     haut ; un ponton de la fiche s'arrête par quatre mètres d'eau.
    ///
    /// Le maillage est RECUIT une fois, dans le repère du ponton : positions,
    /// normales et tangentes (sa carte de relief en a besoin) — la travée se pose
    /// ensuite comme une pièce nommée.
    /// </summary>
    void WholeModule(Node3D root, Parts p, string rel)
    {
        var meshes = new List<(Mesh Mesh, Transform3D Xf)>();
        Aabb? box = null;
        var stack = new Stack<(Node N, Transform3D Xf)>();
        stack.Push((root, Transform3D.Identity));
        while (stack.Count > 0)
        {
            var (n, xf) = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null)
            {
                meshes.Add((mi.Mesh, xf));
                var bx = xf * mi.Mesh.GetAabb();
                box = box is Aabb a0 ? a0.Merge(bx) : bx;
            }
            foreach (var c in n.GetChildren()) stack.Push((c, c is Node3D c3 ? xf * c3.Transform : xf));
        }
        if (box is not Aabb bb || meshes.Count == 0) return;

        // le tablier : la cote où les faces tournées vers le haut ont le plus de surface
        const int Bins = 40;
        var hist = new double[Bins];
        foreach (var (mesh, xf) in meshes)
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arr = mesh.SurfaceGetArrays(s);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                int nt = idx != null ? idx.Length / 3 : v.Length / 3;
                for (int t = 0; t < nt; t++)
                {
                    Vector3 A = xf * v[idx != null ? idx[t * 3] : t * 3], B = xf * v[idx != null ? idx[t * 3 + 1] : t * 3 + 1],
                            C = xf * v[idx != null ? idx[t * 3 + 2] : t * 3 + 2];
                    var nrm = (B - A).Cross(C - A);
                    float area = nrm.Length() * 0.5f;
                    if (area <= 0 || Math.Abs(nrm.Y) / (2 * area) < 0.9f) continue;
                    int k = Math.Clamp((int)(((A.Y + B.Y + C.Y) / 3 - bb.Position.Y) / bb.Size.Y * Bins), 0, Bins - 1);
                    hist[k] += area;
                }
            }
        int best = 0;
        for (int k = 1; k < Bins; k++) if (hist[k] > hist[best]) best = k;
        double deckU = bb.Position.Y + (best + 0.5) / Bins * bb.Size.Y;      // dans les unités du fichier
        double railU = bb.End.Y - deckU, legU = deckU - bb.Position.Y;

        bool turn = bb.Size.Z > bb.Size.X;
        double acrossU = turn ? bb.Size.X : bb.Size.Z;
        double sAcross = p.NominalW / Math.Max(1e-4, acrossU);
        double sReal = railU > 0.02 * bb.Size.Y ? 1.10 / railU : sAcross;
        sReal = Math.Clamp(sReal, 0.25 * sAcross, sAcross);

        // le repère : couché (z du fichier vers +x), mis à l'échelle, le tablier à hauteur de bordage
        var rot = turn ? new Basis(Vector3.Up, Mathf.Pi / 2) : Basis.Identity;
        var scale = Basis.FromScale(new Vector3((float)sReal, (float)sReal, (float)sAcross));
        var m = new Transform3D(scale * rot, Vector3.Zero);
        var tb = m * bb;
        double deckTop = Berth.DeckY + 0.20;
        var shift = new Vector3(-tb.Position.X, (float)(deckTop - deckU * sReal), -(tb.Position.Z + tb.Size.Z * 0.5f));
        var place = new Transform3D(Basis.Identity, shift) * m;

        // les pieds : sous le tablier, passé trente pour cent de leur hauteur (la charpente), étirés jusqu'à −9 m
        double bottom = deckTop - legU * sReal, cut = deckTop - 0.3 * (deckTop - bottom);
        const double Floor = -9.0;
        double f = bottom < cut ? (cut - Floor) / (cut - bottom) : 1;
        Vector3 Warp(Vector3 q) => q.Y < cut ? new Vector3(q.X, (float)(cut - (cut - q.Y) * f), q.Z) : q;

        var haze = HazePass.New();
        Hazed.Add(haze);
        foreach (var (mesh, xf) in meshes) p.Bay.Add((Bake(mesh, place * xf, Warp, haze), Transform3D.Identity));
        p.BayL = tb.Size.X;
        GD.Print(FormattableString.Invariant(
            $"[ponton] {rel} : un tronçon d'une pièce, {tb.Size.X:F1} m de long sur {p.NominalW:F0} m, garde-corps {railU * sReal:F2} m, pieds jusqu'à {Floor:F0} m"));
    }

    /// <summary>Recuire un maillage dans un autre repère, déformé : positions, normales, tangentes ; ses matières, la brume par-dessus.</summary>
    static ArrayMesh Bake(Mesh src, Transform3D t, Func<Vector3, Vector3> warp, ShaderMaterial haze)
    {
        var outMesh = new ArrayMesh();
        var nb = t.Basis.Inverse().Transposed();
        for (int s = 0; s < src.GetSurfaceCount(); s++)
        {
            var a = src.SurfaceGetArrays(s);
            var v = a[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            for (int i = 0; i < v.Length; i++) v[i] = warp(t * v[i]);
            a[(int)Mesh.ArrayType.Vertex] = v;
            if (a[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
            {
                var n = a[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for (int i = 0; i < n.Length; i++) n[i] = (nb * n[i]).Normalized();
                a[(int)Mesh.ArrayType.Normal] = n;
            }
            if (a[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil)
            {
                var tg = a[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
                for (int i = 0; i + 3 < tg.Length; i += 4)
                {
                    var d = (t.Basis * new Vector3(tg[i], tg[i + 1], tg[i + 2])).Normalized();
                    tg[i] = d.X; tg[i + 1] = d.Y; tg[i + 2] = d.Z;
                }
                a[(int)Mesh.ArrayType.Tangent] = tg;
            }
            outMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, a);
            if (src.SurfaceGetMaterial(s) is BaseMaterial3D bm)
            {
                var own = (BaseMaterial3D)bm.Duplicate();
                own.NextPass = haze;               // l'air devant, comme tout ce qui est à terre
                outMesh.SurfaceSetMaterial(s, own);
            }
            else if (src.SurfaceGetMaterial(s) is Material other) outMesh.SurfaceSetMaterial(s, other);
        }
        return outMesh;
    }

    /// <summary>
    /// LE PONTON ASSEMBLÉ DE SES PIÈCES : des travées étirées pour tomber juste sur
    /// la longueur, mises à la largeur voulue ; une jambe à chaque nœud, du fond
    /// jusque sous les longerons ; les bittes sur le bordage. Les jambes lisent le
    /// fond comme celles du code, par la même HeightAt que la coque talonne.
    /// </summary>
    void TimberFromParts(Node3D frame, double len, double W, double x0, double z0, double ang, double[] rows, Parts p)
    {
        double ca = Math.Cos(ang), sa = Math.Sin(ang), deckY = Berth.DeckY, hw = W * 0.5;
        double sz = W / p.NominalW;
        int bays = Math.Max(1, (int)Math.Round(len / p.BayL));
        double stretch = len / (bays * p.BayL);
        void Put(List<(Mesh Mesh, Transform3D Xf)> part, Transform3D at)
        {
            foreach (var (mesh, xf) in part)
                frame.AddChild(new MeshInstance3D { Mesh = mesh, Transform = at * xf });
        }
        for (int k = 0; k < bays; k++)
            Put(p.Bay, new Transform3D(Basis.FromScale(new Vector3((float)stretch, 1, (float)sz)),
                                       new Vector3((float)(k * p.BayL * stretch), 0, 0)));
        // les jambes : du fond (dix centimètres dedans) jusque sous les longerons
        double under = deckY - 0.36;
        for (int i = 0; i <= bays; i++)
        {
            double along = (double)i / bays * len;
            foreach (double side in rows)
            {
                double wx = x0 + ca * along - sa * side, wz = z0 + sa * along + ca * side;
                double bed = Math.Min(-0.4, _world.HeightAt(wx, wz)) - 0.1;
                Put(p.Pile, new Transform3D(Basis.FromScale(new Vector3(1, (float)(under - bed), 1)),
                                            new Vector3((float)along, (float)bed, (float)side)));
            }
        }
        // les bittes : deux au musoir, une à la racine — sur le bordage
        double top = deckY + 0.20;
        foreach (int s in new[] { -1, 1 })
            Put(p.Bitt, new Transform3D(Basis.Identity, new Vector3((float)(len - 1.6), (float)top, (float)(s * (hw - 0.45)))));
        Put(p.Bitt, new Transform3D(Basis.Identity, new Vector3(2.2f, (float)top, (float)(hw - 0.45))));
    }

    /* ------------------------------------------------------------------ */
    /*  LES PONTONS DE LA FICHE                                            */
    /* ------------------------------------------------------------------ */

    /// <summary>Le registre de l'éditeur : chaque ponton de la fiche s'y inscrit.</summary>
    public EditRegistry? Editor;
    /// <summary>Un ponton a bougé (l'éditeur) : le solveur doit relire ses segments.</summary>
    public event Action? PiersChanged;

    sealed class Pier
    {
        public Node3D G = null!;
        public double X, Z, Yaw, Len, W;            // le MILIEU, en mètres vrais ; le cap vers le musoir
        public bool Gone;
        public double RootX => X - Math.Sin(Yaw) * Len * 0.5;
        public double RootZ => Z - Math.Cos(Yaw) * Len * 0.5;
    }
    readonly List<Pier> _piers = new();

    /// <summary>
    /// BÂTIR LES PONTONS DE LA FICHE (world/*.json → pontons), une fois, avant que le
    /// solveur n'apprenne où sont les quais. Chacun est un objet du mode création :
    /// le déplacer le REBÂTIT — ses jambes doivent retrouver le fond là où il va.
    /// </summary>
    public void BuildPiers()
    {
        int i = 0;
        foreach (var spec in _world.Region.Piers)
        {
            var p = new Pier
            {
                G = new Node3D(), X = spec.X, Z = spec.Z, Yaw = Compass.YawOf(spec.Cap),
                Len = spec.Length, W = spec.Width
            };
            AddChild(p.G);
            _piers.Add(p);
            double baseLen = spec.Length;
            var e = new Editable
            {
                Id = $"ponton:{i++}", Label = spec.Name.Length > 0 ? spec.Name : "un ponton",
                BaseX = p.X, BaseZ = p.Z, BaseYaw = p.Yaw, X = p.X, Z = p.Z, Yaw = p.Yaw,
                Radius = Math.Max(spec.Width, spec.Length * 0.5), Height = 3,
                Family = "ponton", FamilyLabel = "Ponton"
            };
            // l'échelle est sa LONGUEUR : PgUp l'allonge vers le large
            e.Push = ed =>
            {
                p.X = ed.X; p.Z = ed.Z; p.Yaw = ed.Yaw; p.Len = baseLen * ed.Scale; p.Gone = ed.Removed;
                ed.GroundY = 0;
                RebuildPier(p);
                PiersChanged?.Invoke();
            };
            if (Editor != null) Editor.Add(e); else e.Push(e);
        }
        if (_piers.Count > 0) GD.Print($"{_piers.Count} ponton(s) de la fiche");
    }

    void RebuildPier(Pier p)
    {
        foreach (var c in p.G.GetChildren()) { p.G.RemoveChild(c); c.QueueFree(); }
        p.G.Visible = !p.Gone;
        if (p.Gone) return;
        double ang = Math.Atan2(Math.Cos(p.Yaw), Math.Sin(p.Yaw));
        // le repère : la racine à l'origine du groupe, +x vers le large
        var frame = new Node3D { Rotation = new Vector3(0, (float)-ang, 0) };
        p.G.AddChild(frame);
        Timber(frame, p.Len, p.W, p.RootX, p.RootZ, ang);
    }

    /// <summary>
    /// CE QUE LE SOLVEUR EN SAIT : des segments de la largeur d'un quai (Berth.Width),
    /// côte à côte jusqu'à couvrir le tablier — deux pour un ponton de quatorze
    /// mètres. Le solveur n'a ainsi qu'une sorte de ponton à heurter.
    /// </summary>
    public IEnumerable<(double Sx, double Sz, double Hx, double Hz)> PierSegments()
    {
        foreach (var p in _piers)
        {
            if (p.Gone) continue;
            double dx = Math.Sin(p.Yaw), dz = Math.Cos(p.Yaw), px = -dz, pz = dx;
            int k = Math.Max(1, (int)Math.Ceiling(p.W / Berth.Width));
            double spread = p.W - Berth.Width;
            for (int s = 0; s < k; s++)
            {
                double off = k == 1 ? 0 : -spread * 0.5 + spread * s / (k - 1);
                yield return (p.RootX + px * off, p.RootZ + pz * off,
                              p.RootX + dx * p.Len + px * off, p.RootZ + dz * p.Len + pz * off);
            }
        }
    }

    /// <summary>
    /// POSER LE FIGURANT sur le tablier. Son maillage sort normalisé à un mètre
    /// de haut (c'est ainsi que la cuisson le rend), on le ramène donc à la
    /// taille d'un homme — et l'échelle emporte aussi ses déplacements, qui sont
    /// dans le même repère, si bien qu'il ne se met pas à gigoter deux fois plus
    /// que son corps.
    ///
    /// Il regarde LE LARGE, dans l'axe du ponton : c'est de là qu'arrive ce
    /// qu'on attend. Un homme de dos à la mer sur un quai passe pour une erreur.
    /// </summary>
    void Figurant(Node3D frame, double len, double deckY, double hw, string key)
    {
        if (!_figureCherchee) { _figureCherchee = true; _figure = Vat.Load("pirate_0001"); }
        if (_figure is not Vat.Figure f) return;

        const double Taille = 1.75;                 // un homme, en mètres
        double k = f.Height > 0.01 ? Taille / f.Height : 1;
        /* POSÉ SUR SES PIEDS, et non sur son origine : une cuisson ne commence pas
           forcément à la pose de repos, et celle-ci tient l'homme seize
           centimètres plus haut. Le plancher est mesuré au chargement. */
        double pieds = f.Floor * k;

        var mat = (ShaderMaterial)f.Material.Duplicate();
        /* SA MATIÈRE EST PARTAGÉE, SON DÉPHASAGE NE L'EST PAS. Duplicate() sur un
           ShaderMaterial garde le même shader et les mêmes textures — donc
           l'instanciation tient — et ne sépare que les uniformes d'instance. */
        var mi = new MeshInstance3D
        {
            Mesh = f.Mesh,
            MaterialOverride = mat,
            Position = new Vector3((float)(len * 0.66), (float)(deckY + 0.17 - pieds), (float)(hw - 1.0)),
            // +x du repère va vers le large ; le maillage regarde -z, d'où le quart de tour
            Rotation = new Vector3(0, Mathf.Pi * 0.5f, 0),
            Scale = new Vector3((float)k, (float)k, (float)k),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        };
        // un port, un pas : le hachage du nom suffit, et il ne change pas d'une partie à l'autre
        mi.SetInstanceShaderParameter("u_phase", (float)(Math.Abs(key.GetHashCode() % 997) / 997.0));

        /* LA BRUME EN DERNIER, comme tout ce que ce fichier pose : sans elle le
           figurant reste net quand son ponton s'efface, et il flotte. */
        var haze = HazePass.New();
        mat.NextPass = haze;
        Hazed.Add(haze);

        frame.AddChild(mi);
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
        foreach (var p in _piers)
        {
            double dx = p.X - centre.X, dz = p.Z - centre.Z;
            bool near = !p.Gone && dx * dx + dz * dz < Range * Range;
            p.G.Visible = near;
            if (near) p.G.Position = new Vector3((float)(p.RootX - origin.X), 0, (float)(p.RootZ - origin.Z));
        }
    }
}
