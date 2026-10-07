using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA TERRE QU'ON TERRASSE (ShipDemo.EditorTerrace.cs). Un carreau de 1 440 m se bâtit
/// en un bloc et n'est jamais rebâti : le relief ne changeait pas. Ceux qui touchent un
/// carré de terrassement (World.Terraforms) se bâtissent autrement :
///   · DEUX FOIS PLUS FINS (2,5 m au lieu de 5) : une berge se retouche au mètre près ;
///   · EN MORCEAUX (8 × 8 de 180 m) : un coup de pinceau ne rebâtit que les morceaux
///     qu'il touche, quelques millisecondes, et peut donc se voir pendant le geste ;
///   · leurs NORMALES prises aux différences centrées, sur la hauteur des voisins même
///     hors du morceau : deux morceaux s'éclairent pareil le long de leur couture ;
///   · RECOUSUS à leur bord : un sommet sur deux du bord n'existe pas chez le voisin
///     plus grossier, il prend la moyenne des deux qui l'entourent — pas de fente.
/// </summary>
public partial class LandNode
{
    const int TerraceChunks = 8;

    sealed class Chunked
    {
        public Node3D Root = null!;
        public int I, J, N;
        public readonly MeshInstance3D?[,] Parts = new MeshInstance3D?[TerraceChunks, TerraceChunks];
    }

    readonly Dictionary<(int, int), Chunked> _chunked = new();

    /// <summary>Une case de foule (BuildCrowd) : ses pièces, où elles sont, et leurs maillages.</summary>
    sealed class CrowdPatch
    {
        public double X0, Z0, Size;
        public List<(double X, double Z, double Lift)> Seats = null!;
        public List<Transform3D> Ts = null!;
        public readonly List<(MultiMesh Mm, Transform3D Local)> Parts = new();
    }
    readonly List<CrowdPatch> _crowds = new();

    /// <summary>
    /// LA VÉGÉTATION SUIT LE SOL (demandé : « recaler la végétation en direct après un
    /// terrassement »). Chaque pièce de foule dans le rectangle reprend la hauteur du sol
    /// sous elle, plus ce qu'elle en dépassait : un arbre monte avec la butte, un buisson
    /// descend dans le creux. Seules les cases touchées sont relues.
    /// </summary>
    void Reseat(double x0, double z0, double x1, double z1)
    {
        foreach (var c in _crowds)
        {
            if (c.X0 > x1 || c.Z0 > z1 || c.X0 + c.Size < x0 || c.Z0 + c.Size < z0) continue;
            for (int i = 0; i < c.Seats.Count; i++)
            {
                var (x, z, lift) = c.Seats[i];
                if (x < x0 || x > x1 || z < z0 || z > z1) continue;
                var t = c.Ts[i];
                // l'origine du maillage est recentrée sur sa boîte : on ne change que sa hauteur
                float dy = (float)(World.HeightAt(x, z) + lift) - t.Origin.Y;
                if (Math.Abs(dy) < 1e-3f) continue;
                t.Origin += new Vector3(0, dy, 0);
                c.Ts[i] = t;
                foreach (var (mm, local) in c.Parts) mm.SetInstanceTransform(i, t * local);
            }
        }
    }

    /// <summary>Ce carreau touche-t-il un carré de terrassement ?</summary>
    bool Terraced(int i, int j)
    {
        double x0 = i * Tile, z0 = j * Tile, x1 = x0 + Tile, z1 = z0 + Tile;
        foreach (var t in World.Terraforms)
            if (t.X0 < x1 && t.X0 + t.Side > x0 && t.Z0 < z1 && t.Z0 + t.Side > z0) return true;
        return false;
    }

    Node3D BuildTerraced(int i, int j, int n)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        n = Math.Max(TerraceChunks, n / TerraceChunks * TerraceChunks);
        var c = new Chunked { Root = new Node3D(), I = i, J = j, N = n };
        AddChild(c.Root);
        /* LES HAUTEURS EN PARALLÈLE, les maillages ensuite sur le fil du moteur : un
           carreau fin lit 360 000 fois le relief, 450 ms d'un seul fil (mesuré). Le relief
           se lit de plusieurs fils sans risque — le semis le fait déjà. */
        var data = new PartData[TerraceChunks * TerraceChunks];
        System.Threading.Tasks.Parallel.For(0, data.Length, q => data[q] = ComputePart(c, q % TerraceChunks, q / TerraceChunks));
        for (int q = 0; q < data.Length; q++) c.Parts[q % TerraceChunks, q / TerraceChunks] = MakePart(c, data[q]);
        _chunked[(i, j)] = c;
        GD.Print($"[terre] carreau terrassé ({i}, {j}) : {n}×{n} en {TerraceChunks * TerraceChunks} morceaux, {clock.ElapsedMilliseconds} ms");
        MapCost.Add("terre", clock.Elapsed.TotalMilliseconds);
        return c.Root;
    }

    /// <summary>La hauteur d'un sommet du carreau, recousue au bord (voir plus haut).</summary>
    double StitchedHeight(int i, int j, int n, int a, int b)
    {
        double x0 = i * Tile, z0 = j * Tile, d = Tile / n;
        bool edgeA = b == 0 || b == n, edgeB = a == 0 || a == n;
        if (edgeA && (a & 1) == 1)
            return 0.5 * (World.HeightAt(x0 + (a - 1) * d, z0 + b * d) + World.HeightAt(x0 + (a + 1) * d, z0 + b * d));
        if (edgeB && (b & 1) == 1)
            return 0.5 * (World.HeightAt(x0 + a * d, z0 + (b - 1) * d) + World.HeightAt(x0 + a * d, z0 + (b + 1) * d));
        return World.HeightAt(x0 + a * d, z0 + b * d);
    }

    sealed class PartData { public Vector3[] Pos = null!, Nrm = null!; public Color[] Col = null!; public int[] Idx = null!; }

    MeshInstance3D BuildPart(Chunked c, int ca, int cb) => MakePart(c, ComputePart(c, ca, cb));

    /// <summary>Les sommets d'un morceau — sans rien toucher du moteur : se calcule sur n'importe quel fil.</summary>
    PartData ComputePart(Chunked c, int ca, int cb)
    {
        int n = c.N, m = n / TerraceChunks;
        int a0 = ca * m, b0 = cb * m;
        double x0 = c.I * Tile, z0 = c.J * Tile, d = Tile / n;
        // les hauteurs avec une marge d'un sommet, pour les normales au bord du morceau
        int w = m + 3;
        var hh = new double[w * w];
        for (int b = 0; b < w; b++)
            for (int a = 0; a < w; a++)
            {
                int ga = a0 + a - 1, gb = b0 + b - 1;
                hh[b * w + a] = ga >= 0 && gb >= 0 && ga <= n && gb <= n
                    ? StitchedHeight(c.I, c.J, n, ga, gb)
                    : World.HeightAt(x0 + ga * d, z0 + gb * d);
            }
        int v = (m + 1) * (m + 1);
        var pos = new Vector3[v];
        var nrm = new Vector3[v];
        var col = new Color[v];
        int k = 0;
        for (int b = 0; b <= m; b++)
            for (int a = 0; a <= m; a++, k++)
            {
                int q = (b + 1) * w + (a + 1);
                double h = hh[q];
                double x = (a0 + a) * d, z = (b0 + b) * d;
                pos[k] = new Vector3((float)x, (float)h, (float)z);
                // la pente aux différences centrées : la même des deux côtés d'une couture
                double dx = (hh[q + 1] - hh[q - 1]) / (2 * d), dz = (hh[q + w] - hh[q - w]) / (2 * d);
                nrm[k] = new Vector3((float)-dx, 1, (float)-dz).Normalized();
                col[k] = h >= SandTop && World.PavedAt(x0 + x, z0 + z) ? Paved : Tint(h);
            }
        var idx = new int[m * m * 6];
        int t = 0;
        for (int b = 0; b < m; b++)
            for (int a = 0; a < m; a++)
            {
                int p = b * (m + 1) + a;
                // le sens horaire de Godot, comme Build après son échange
                idx[t++] = p; idx[t++] = p + m + 2; idx[t++] = p + m + 1;
                idx[t++] = p; idx[t++] = p + 1; idx[t++] = p + m + 2;
            }
        return new PartData { Pos = pos, Nrm = nrm, Col = col, Idx = idx };
    }

    MeshInstance3D MakePart(Chunked c, PartData d)
    {
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = d.Pos;
        arr[(int)Mesh.ArrayType.Normal] = d.Nrm;
        arr[(int)Mesh.ArrayType.Color] = d.Col;
        arr[(int)Mesh.ArrayType.Index] = d.Idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        c.Root.AddChild(mi);
        return mi;
    }

    /// <summary>
    /// LE SOL A CHANGÉ dans ce rectangle (mètres vrais) : rebâtir les morceaux qu'il
    /// touche, une marge d'un sommet comprise (leurs normales lisent les voisins). Les
    /// carreaux lointains du même endroit sont jetés et se rebâtiront à leur tour.
    /// </summary>
    public void Reshape(double x0, double z0, double x1, double z1)
    {
        int i0 = (int)Math.Floor(x0 / Tile), i1 = (int)Math.Floor(x1 / Tile);
        int j0 = (int)Math.Floor(z0 / Tile), j1 = (int)Math.Floor(z1 / Tile);
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                if (_chunked.TryGetValue((i, j), out var c))
                {
                    double part = Tile / TerraceChunks, pad = 2 * Tile / c.N;
                    for (int cb = 0; cb < TerraceChunks; cb++)
                        for (int ca = 0; ca < TerraceChunks; ca++)
                        {
                            double px0 = i * Tile + ca * part - pad, pz0 = j * Tile + cb * part - pad;
                            if (px0 > x1 || pz0 > z1 || px0 + part + 2 * pad < x0 || pz0 + part + 2 * pad < z0) continue;
                            var old = c.Parts[ca, cb];
                            c.Parts[ca, cb] = BuildPart(c, ca, cb);
                            old?.QueueFree();
                        }
                }
                if (_tiles.TryGetValue((i, j), out var t) && t.Far != null)
                {
                    t.Far.QueueFree();
                    t.Far = null;
                }
            }
        Reseat(x0, z0, x1, z1);
    }
}
