using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'ALLER-RETOUR AVEC BLENDER (demandé : « tenir ma map dans le jeu à jour avec des
/// modifs faites dans Blender »). Le jeu reste la vérité — le monde est procédural et
/// la main passe par-dessus en RETOUCHES — ; Blender n'est qu'un autre mode création,
/// plus confortable pour disposer cent maisons.
///
///   -- --zone-blender port-royal[,côté,pas]   écrit world/blender/&lt;port&gt;.glb et son
///                                             manifeste &lt;port&gt;-zone.json, puis quitte
///   -- --zone-retour port-royal[,essai]       relit &lt;port&gt;-blender.json (ce que Blender
///                                             a relevé dans le .blend) et l'applique
///
/// Les deux passent par <see cref="EditRegistry"/>, et par lui seul : un objet se pose
/// par le <c>Push</c> de celui qui l'a bâti, une copie naît par <see cref="SpawnAdded"/>
/// — exactement ce que font la souris et Ctrl+V. Le fichier des retouches s'écrit donc
/// comme si l'on avait tout fait à la main, et rien d'autre (fiche, relief, peinture)
/// n'est touché. L'outil qui enchaîne Blender et le jeu : tools/blender-zone.js.
///
/// LE NOM EST LA CLÉ. Chaque objet du .glb s'appelle comme sa retouche
/// (« maison:Port-Royal:37 ») ; Blender nomme un double « …:37.001 », qui devient une
/// copie. Un objet du manifeste qu'on ne retrouve plus est retiré. Les noms trop longs
/// pour Blender (63 octets) prennent un alias, que le manifeste traduit.
/// </summary>
public partial class ShipDemo
{
    string? _zoneOut, _zoneBack;
    bool _zoneTry;
    /// <summary>Le côté de la zone ; 0 : celui que donne le port lui-même (ZoneFit).</summary>
    double _zoneSide = 0, _zoneStep = 2, _zoneClock;
    int _zoneSeen = -1;
    double _zoneStable;

    static string ZoneDir => System.IO.Path.Combine(Assets.Root, "world", "blender");

    void ZoneArgs(string flag, string value)
    {
        var a = value.Split(',');
        if (flag == "--zone-blender" || flag == "--zone-palette" || flag == "--zone-terrain")
        {
            _zonePaletteOnly = flag == "--zone-palette";
            _zoneTerrainOnly = flag == "--zone-terrain";
            _zoneOut = a[0];
            if (a.Length > 1) _zoneSide = Math.Clamp(a[1].ToFloat(), 100, 6000);
            if (a.Length > 2) _zoneStep = Math.Clamp(a[2].ToFloat(), 0.5, 20);
        }
        else
        {
            _zoneBack = a[0];
            _zoneTry = a.Length > 1 && a[1] == "essai";
        }
        _askTitle ??= false;
    }

    /// <summary>
    /// Chaque image : dès que le port est bâti — les ajouts nés, le registre qui ne
    /// grossit plus depuis trois secondes —, l'export ou le retour, puis on quitte.
    /// </summary>
    void ZoneTick(double dt)
    {
        /* QUITTER PLUS TARD : le rendu rend encore les copies qu'on vient de faire et de
           jeter (free_rid hors du fil de rendu), et quitter dans la même image faisait tomber
           le jeu en sortant — après avoir écrit, mais tomber quand même. */
        if (_zoneQuit is int q)
        {
            _zoneQuitIn -= dt;
            if (_zoneQuitIn <= 0) { _zoneQuit = null; GetTree().Quit(q); }
            return;
        }
        if (_zoneOut == null && _zoneBack == null) return;
        if (_editReg == null || _world == null) return;
        _zoneClock += dt;
        int n = _editReg.Items.Count;
        if (n != _zoneSeen) { _zoneSeen = n; _zoneStable = 0; }
        else _zoneStable += dt;
        bool settled = _zoneClock > 5 && _zoneStable > 3 && (_pendingAdds is { Count: 0 } || _zoneClock > 60);
        if (!settled) return;
        bool export = _zoneOut != null;
        string key = (_zoneOut ?? _zoneBack)!;
        _zoneOut = _zoneBack = null;
        int code = 1;
        try
        {
            if (_world.ByKey(key) is not { } isl)
                GD.PushError($"[blender] lieu inconnu : {key}");
            else if (export) code = ZoneExport(isl) ? 0 : 1;
            else code = ZoneImport(isl) ? 0 : 1;
        }
        catch (Exception ex) { GD.PushError($"[blender] {ex}"); }
        _zoneQuit = code; _zoneQuitIn = 1.0;
    }
    int? _zoneQuit;
    double _zoneQuitIn;

    /* ------------------------------------------------------------------ */
    /*  LE JEU VERS BLENDER                                                */
    /* ------------------------------------------------------------------ */

    /* LA PALETTE SEULE (-- --zone-palette port) : un nouveau modèle déposé dans world/models
       ou props, à mettre dans un .blend où l'on travaille déjà. Rien d'autre n'est écrit —
       ni le .blend, ni le manifeste — : on importe palette.glb dans Blender par-dessus. Le
       rang est celui de la zone exportée (son côté lu au manifeste), au nord d'elle. */
    bool _zonePaletteOnly;
    /* LE TERRAIN SEUL (-- --zone-terrain port) : le relief a changé (repeint, terrassé), le
       .blend où l'on travaille garde ses objets et ne reçoit qu'une terre et une mer neuves
       (tools/blender/zone_terrain.py). Même carré et même pas que l'export, lus au manifeste,
       pour que la terre neuve tombe exactement sous les objets. */
    bool _zoneTerrainOnly;

    bool ZoneExport(Isle isl)
    {
        double cx = isl.X, cz = isl.Z;
        string manPath = System.IO.Path.Combine(ZoneDir, isl.Key + "-zone.json");
        if ((_zonePaletteOnly || _zoneTerrainOnly) && File.Exists(manPath) && JsonNode.Parse(File.ReadAllText(manPath)) is { } man0)
        {
            if (man0["cote"] is { } side0) _zoneSide = side0.GetValue<double>();
            if (_zoneTerrainOnly && man0["pas"] is { } step0) _zoneStep = step0.GetValue<double>();
        }
        if (!(_zoneSide > 0)) _zoneSide = ZoneFit(isl);
        double half = _zoneSide * 0.5;
        var g = new GlbOut();
        var top = new List<int>();
        var manifest = new JsonObject();
        int alias = 0, count = 0, boxes = 0;

        // LA TERRE, telle que le jeu la lit : relief, rades draguées, môle, récifs, terrassement
        if (!_zonePaletteOnly)
        {
            top.Add(ZoneTerrain(g, cx, cz));
            top.Add(ZoneSea(g));
        }

        foreach (var e in _editReg!.Items)
        {
            if (_zonePaletteOnly || _zoneTerrainOnly) break;
            if (e.Smoke || e.Removed) continue;   // une fumée suit sa maison ; un retiré n'est plus là
            double rx = e.X - cx, rz = e.Z - cz;
            if (Math.Abs(rx) > half || Math.Abs(rz) > half) continue;
            int mesh = -1;
            if (e.MakeVisual != null)
            {
                var vis = e.MakeVisual();
                mesh = g.Visual(vis);
                vis.QueueFree();
            }
            if (mesh < 0) { mesh = g.Box(e.Radius, e.Height); boxes++; }
            string name = Encoding.UTF8.GetByteCount(e.Id) <= 58 ? e.Id : "#" + (++alias);
            double y = e.GroundY + e.Lift * e.Scale;
            double yaw = e.Yaw - e.VisualYaw;
            top.Add(g.Node(name, mesh, new Vector3((float)rx, (float)y, (float)rz), yaw, e.Scale, e.Tilt));
            manifest[name] = new JsonObject
            {
                ["id"] = e.Id,
                ["x"] = Math.Round(rx, 3), ["y"] = Math.Round(y, 3), ["z"] = Math.Round(rz, 3),
                ["cap"] = Math.Round(yaw, 6), ["echelle"] = Math.Round(e.Scale, 5),
                ["vcap"] = Math.Round(e.VisualYaw, 6), ["lift"] = Math.Round(e.Lift, 4),
                ["famille"] = e.FamilyLabel
            };
            if (e.Tilted) manifest[name]!["penche"] = TiltJson(e.Tilt);
            count++;
        }

        /* LA PALETTE : un exemplaire de chaque modèle brut, à sa taille de jeu, en rang au
           nord de la zone. On le duplique dans Blender et on pose le double : il devient un
           ajout « glb » — la même chose que Tab puis Ctrl+V dans le mode création. */
        int pal = 0;
        double px = -half;
        foreach (var rel in RawModelNames())
        {
            if (_zoneTerrainOnly) break;
            if (RawModel(rel) is not { } raw) continue;
            var vis = raw.Make();
            int mesh = g.Visual(vis);
            vis.QueueFree();
            if (mesh < 0) continue;
            double w = Math.Max(4, raw.Radius * 2);
            px += w * 0.5;
            string name = "brut:" + rel;
            if (Encoding.UTF8.GetByteCount(name) > 58) name = "#" + (++alias);
            top.Add(g.Node(name, mesh, new Vector3((float)px, 0, (float)(half + 40 + raw.Radius)), 0, 1));
            manifest[name] = new JsonObject { ["brut"] = rel };
            px += w * 0.5 + 8;
            pal++;
        }

        Directory.CreateDirectory(ZoneDir);
        if (_zoneTerrainOnly)
        {
            string tg = System.IO.Path.Combine(ZoneDir, isl.Key + "-terre.glb");
            g.Write(tg, "terre", top);
            GD.Print(FormattableString.Invariant($"[blender] {tg} : la terre et la mer seules, {_zoneSide:F0} m au pas de {_zoneStep:F1} m ({new FileInfo(tg).Length / 1048576.0:F1} Mo)"));
            return true;
        }
        if (_zonePaletteOnly)
        {
            string pg = System.IO.Path.Combine(ZoneDir, "palette.glb");
            g.Write(pg, "palette", top);
            GD.Print(FormattableString.Invariant($"[blender] {pg} : {pal} modèles bruts, à leur taille de jeu, en rang au nord de la zone ({new FileInfo(pg).Length / 1048576.0:F1} Mo)"));
            return true;
        }
        string glb = System.IO.Path.Combine(ZoneDir, isl.Key + ".glb");
        g.Write(glb, isl.Key, top);
        var doc = new JsonObject
        {
            ["port"] = isl.Key, ["region"] = _world!.Region.Key,
            ["cx"] = cx, ["cz"] = cz, ["cote"] = _zoneSide, ["pas"] = _zoneStep,
            ["note"] = "Écrit par le jeu (-- --zone-blender). Ce qui était dans la zone à l'export : le retour compare le .blend à ceci.",
            ["objets"] = manifest
        };
        File.WriteAllText(System.IO.Path.Combine(ZoneDir, isl.Key + "-zone.json"),
                          doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        GD.Print(FormattableString.Invariant(
            $"[blender] {glb} : {count} objets ({boxes} en boîte, sans modèle), {pal} modèles bruts en palette, {g.MeshCount} maillages, {new FileInfo(glb).Length / 1048576.0:F1} Mo ; zone {_zoneSide:F0} m au pas de {_zoneStep:F1} m autour de ({cx:F1}, {cz:F1})"));
        return true;
    }

    /// <summary>
    /// LE CARRÉ QUE DEMANDE LE PORT, et non un carré fixe : un kilomètre deux coupait le
    /// fort de la pointe nord de Port-Royal (ajout:10, à 718 m). Il couvre son relief fin
    /// (le patch de la fiche, « la carte de Port-Royal ») et tout ce qui est posé à moins de
    /// 2,5 km et plus près de lui que d un autre port, emprise comprise, plus une marge ; arrondi aux 50 m, 6 km au plus.
    /// </summary>
    double ZoneFit(Isle isl)
    {
        double half = 300;
        foreach (var p in _world!.Region.Patches)
        {
            var (ax, az) = _world.Geo.ToXZ(p.South, p.West);
            var (bx, bz) = _world.Geo.ToXZ(p.North, p.East);
            bool mine = p.Key == isl.Key || (isl.X > Math.Min(ax, bx) && isl.X < Math.Max(ax, bx) && isl.Z > Math.Min(az, bz) && isl.Z < Math.Max(az, bz));
            if (!mine) continue;
            foreach (var x in new[] { ax, bx }) half = Math.Max(half, Math.Abs(x - isl.X));
            foreach (var z in new[] { az, bz }) half = Math.Max(half, Math.Abs(z - isl.Z));
        }
        foreach (var e in _editReg!.Items)
        {
            if (e.Smoke || e.Removed) continue;
            double dx = Math.Abs(e.X - isl.X), dz = Math.Abs(e.Z - isl.Z);
            if (Math.Max(dx, dz) > 2500) continue;
            if (NearerPort(isl, e.X, e.Z)) continue;   // la ville voisine (Passage Fort, à 2,4 km) n est pas celle-ci
            half = Math.Max(half, Math.Max(dx, dz) + e.Radius * e.Scale + 40);
        }
        return Math.Min(6000, Math.Ceiling(2 * half / 50) * 50);
    }

    /// <summary>Un autre port est-il plus près de ce point que celui-ci ?</summary>
    bool NearerPort(Isle isl, double x, double z)
    {
        double mine = (x - isl.X) * (x - isl.X) + (z - isl.Z) * (z - isl.Z);
        foreach (var o in _world!.Isles)
            if (!ReferenceEquals(o, isl) && (x - o.X) * (x - o.X) + (z - o.Z) * (z - o.Z) < mine) return true;
        return false;
    }

    /// <summary>Les modèles bruts de la palette du mode création (FillPalette), dans le même ordre.</summary>
    IEnumerable<string> RawModelNames()
    {
        foreach (var dir in new[] { "world/models", "props" })
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in new[] { Assets.Root, Assets.Heavy })
            {
                string d = System.IO.Path.Combine(root, dir);
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*.glb"))
                {
                    string n = System.IO.Path.GetFileName(f);
                    if (new FileInfo(f).Length < 25_000_000 || PaletteInfo($"{dir}/{n}").Item1 != null) names.Add(n);
                }
            }
            foreach (var n in names) yield return $"{dir}/{n}";
        }
    }

    /// <summary>Le relief en grille, sa teinte cuite dans une image (sol peint compris).</summary>
    int ZoneTerrain(GlbOut g, double cx, double cz)
    {
        double side = _zoneSide, step = _zoneStep;
        int n = (int)Math.Round(side / step) + 1;
        double x0 = cx - side * 0.5, z0 = cz - side * 0.5;
        var h = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                h[j * n + i] = (float)_world!.HeightAt(x0 + i * step, z0 + j * step);
        var pos = new float[n * n * 3];
        var nor = new float[n * n * 3];
        var uv = new float[n * n * 2];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                pos[k * 3] = (float)(i * step - side * 0.5); pos[k * 3 + 1] = h[k]; pos[k * 3 + 2] = (float)(j * step - side * 0.5);
                float hx = h[j * n + Math.Min(n - 1, i + 1)] - h[j * n + Math.Max(0, i - 1)];
                float hz = h[Math.Min(n - 1, j + 1) * n + i] - h[Math.Max(0, j - 1) * n + i];
                var v = new Vector3(-hx, (float)(2 * step), -hz).Normalized();
                nor[k * 3] = v.X; nor[k * 3 + 1] = v.Y; nor[k * 3 + 2] = v.Z;
                uv[k * 2] = i / (float)(n - 1); uv[k * 2 + 1] = j / (float)(n - 1);
            }
        var idx = new uint[(n - 1) * (n - 1) * 6];
        int q = 0;
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                uint a = (uint)(j * n + i), b = a + 1, c = a + (uint)n, d = c + 1;
                idx[q++] = a; idx[q++] = c; idx[q++] = b; idx[q++] = b; idx[q++] = c; idx[q++] = d;
            }
        int mat = g.TextureMaterial("terre", ZoneGroundImage(cx, cz));
        int mesh = g.RawMesh("terre", pos, nor, uv, idx, mat);
        return g.Node("terre", mesh, Vector3.Zero, 0, 1);
    }

    /// <summary>
    /// LA TEINTE DU SOL, à peu près celle du jeu : le relief (fond, sable, herbe, roche)
    /// puis le sol peint par-dessus. De quoi voir les rues et les berges en posant.
    /// </summary>
    Image ZoneGroundImage(double cx, double cz)
    {
        double side = _zoneSide;
        int res = Math.Clamp((int)Math.Round(side / 0.6), 256, 2048);
        var img = Image.CreateEmpty(res, res, false, Image.Format.Rgb8);
        double px = side / res, x0 = cx - side * 0.5, z0 = cz - side * 0.5;
        Color grass = GroundMaterials.Grass, sand = GroundMaterials.Sand, rock = GroundMaterials.Rock,
              white = GroundMaterials.WhiteSand, mud = GroundMaterials.Mud, weed = GroundMaterials.Seagrass,
              cobble = GroundMaterials.Cobble;
        for (int r = 0; r < res; r++)
            for (int c = 0; c < res; c++)
            {
                double x = x0 + (c + 0.5) * px, z = z0 + (r + 0.5) * px;
                double y = _world!.HeightAt(x, z);
                double sl = Math.Abs(_world.HeightAt(x + 1, z) - y) + Math.Abs(_world.HeightAt(x, z + 1) - y);
                Color col;
                if (y < 0) col = white.Lerp(mud, (float)Math.Clamp(-y / 12, 0, 1));
                else if (y < 1.2) col = sand;
                else col = grass.Lerp(sand, (float)Math.Clamp((2.5 - y) / 1.3, 0, 1));
                if (sl > 0.9) col = col.Lerp(rock, (float)Math.Clamp((sl - 0.9) / 0.6, 0, 1));
                if (_paint != null && _paint.Covers(x, z))
                {
                    for (int l = 0; l < 2; l++)
                    {
                        var (pr, pg, pb, pa) = _paint.At(x, z, l);
                        float sum = pr + pg + pb;
                        if (pa <= 0 || sum <= 0) continue;
                        Color m = l == 0 ? grass * (pr / sum) + cobble * (pg / sum) + sand * (pb / sum)
                                         : white * (pr / sum) + mud * (pg / sum) + weed * (pb / sum);
                        col = col.Lerp(m, pa);
                    }
                }
                img.SetPixel(c, r, col.LinearToSrgb());
            }
        return img;
    }

    int ZoneSea(GlbOut g)
    {
        float s = (float)(_zoneSide * 0.5);
        int mat = g.ColorMaterial("mer", new Color(0.12f, 0.36f, 0.45f, 0.45f), blend: true);
        int mesh = g.RawMesh("mer",
            new[] { -s, -0.02f, -s, s, -0.02f, -s, s, -0.02f, s, -s, -0.02f, s },
            new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0 },
            new float[] { 0, 0, 1, 0, 1, 1, 0, 1 },
            new uint[] { 0, 3, 1, 1, 3, 2 }, mat);
        return g.Node("mer", mesh, Vector3.Zero, 0, 1);
    }

    /* ------------------------------------------------------------------ */
    /*  BLENDER VERS LE JEU                                                */
    /* ------------------------------------------------------------------ */

    bool ZoneImport(Isle isl)
    {
        string manPath = System.IO.Path.Combine(ZoneDir, isl.Key + "-zone.json");
        string dumpPath = System.IO.Path.Combine(ZoneDir, isl.Key + "-blender.json");
        if (!File.Exists(manPath) || !File.Exists(dumpPath))
        {
            GD.PushError($"[blender] il faut {manPath} (l'export) et {dumpPath} (le relevé du .blend) — voir tools/blender-zone.js");
            return false;
        }
        var man = JsonNode.Parse(File.ReadAllText(manPath))!.AsObject();
        var dump = JsonNode.Parse(File.ReadAllText(dumpPath))!.AsObject();
        double cx = man["cx"]!.GetValue<double>(), cz = man["cz"]!.GetValue<double>();
        double half = man["cote"]!.GetValue<double>() * 0.5;
        var objs = man["objets"]!.AsObject();
        var seen = new HashSet<string>();
        var lines = new List<string>();
        int moved = 0, added = 0, removed = 0, skipped = 0;
        /* CE QUE LE MANIFESTE DEVRA RETENIR une fois les retouches écrites : le .blend tel
           qu'il est devient l'état connu. Sans quoi le passage suivant revoyait les mêmes
           écarts — et un double « ajout:46.001 » refaisait une copie à chaque retour. On
           ne renomme pas l'objet dans le .blend (il est peut-être ouvert dans Blender) :
           c'est le manifeste qui sait désormais que « ajout:46.001 » est ajout:119. */
        var after = new List<(string Key, JsonObject? Val)>();
        JsonObject Known(string id, double x, double y, double z, double cap, double sc, double vcap, double lift, string fam, Quaternion tilt)
        {
            var k = new JsonObject
            {
                ["id"] = id, ["x"] = Math.Round(x, 3), ["y"] = Math.Round(y, 3), ["z"] = Math.Round(z, 3),
                ["cap"] = Math.Round(cap, 6), ["echelle"] = Math.Round(sc, 5), ["vcap"] = Math.Round(vcap, 6),
                ["lift"] = Math.Round(lift, 4), ["famille"] = fam
            };
            if (Mathf.Abs(tilt.W) < 0.99999f) k["penche"] = TiltJson(tilt);
            return k;
        }

        var list = dump["objets"]!.AsArray();
        // le .blend d'un autre port, ou vide : on ne retire pas toute une ville sur une erreur de fichier
        int known = 0;
        foreach (var o in list) if (objs.ContainsKey(o!["nom"]!.GetValue<string>())) known++;
        int expected = 0;
        foreach (var kv in objs) if (kv.Value!["id"] != null) expected++;
        if (expected > 0 && known < expected / 2)
        {
            GD.PushError($"[blender] le .blend ne contient que {known} des {expected} objets exportés : est-ce le bon fichier ? Rien n'est appliqué.");
            return false;
        }

        foreach (var o in list)
        {
            string nom = o!["nom"]!.GetValue<string>();
            double x = o["x"]!.GetValue<double>(), y = o["y"]!.GetValue<double>(), z = o["z"]!.GetValue<double>();
            double cap = o["cap"]!.GetValue<double>(), sc = o["echelle"]!.GetValue<double>();
            var tilt = TiltRead(o["penche"]);
            // un double déjà rentré est connu sous son nom de Blender
            string bas = objs[nom] is JsonObject k0 && k0["id"] != null ? nom
                       : System.Text.RegularExpressions.Regex.Replace(nom, @"\.\d{3}$", "");
            bool copy = bas != nom;
            if (!copy) seen.Add(nom);
            bool inside = Math.Abs(x) <= half && Math.Abs(z) <= half;

            if (objs[bas] is JsonObject m && m["id"] != null)
            {
                string id = m["id"]!.GetValue<string>();
                if (!_editReg!.ById.TryGetValue(id, out var e)) { lines.Add($"  ? {id} : pas dans le monde chargé, laissé"); skipped++; continue; }
                double vcap = m["vcap"]!.GetValue<double>(), lift = m["lift"]!.GetValue<double>();
                if (e.Sheet.Length > 0 && Mathf.Abs(tilt.W) < 0.99999f)
                {
                    lines.Add($"  ! {nom} : un navire posé ne penche pas, la mer le porte : seul son cap est gardé");
                    tilt = Quaternion.Identity;
                }
                if (!copy)
                {
                    bool same = TiltSame(tilt, TiltRead(m["penche"])) && Math.Abs(x - m["x"]!.GetValue<double>()) < 0.01 && Math.Abs(z - m["z"]!.GetValue<double>()) < 0.01
                             && Math.Abs(Mathf.AngleDifference((float)cap, (float)m["cap"]!.GetValue<double>())) < 1e-3
                             && Math.Abs(sc - m["echelle"]!.GetValue<double>()) < 1e-3
                             && Math.Abs(y - m["y"]!.GetValue<double>()) < 0.02;
                    if (same) continue;
                    bool lifted = Math.Abs(y - m["y"]!.GetValue<double>()) >= 0.02;
                    ZonePose(e, cx + x, cz + z, cap + vcap, e.Sheet.Length > 0 ? 1 : sc, lifted ? y : (double?)null, lift, tilt);
                    lines.Add(FormattableString.Invariant($"  ~ {id} → ({e.X:F2}, {e.Z:F2}) cap {e.Yaw * 180 / Math.PI:F1}°{(Math.Abs(e.Scale - 1) > 1e-3 ? FormattableString.Invariant($" ×{e.Scale:F2}") : "")}{(Math.Abs(e.Dy) > 1e-3 ? FormattableString.Invariant($" dy {e.Dy:+0.00;-0.00}") : "")}{(e.Tilted ? FormattableString.Invariant($" penché de {2 * Math.Acos(Math.Min(1, Math.Abs(e.Tilt.W))) * 180 / Math.PI:F1}°") : "")}"));
                    moved++;
                    after.Add((nom, Known(id, x, y, z, cap, sc, vcap, lift, m["famille"]?.GetValue<string>() ?? "", tilt)));
                }
                else
                {
                    // un double : une copie de l'objet, comme Ctrl+C Ctrl+V (la copie d'une copie remonte à la source)
                    string from = e.Added ? e.From : id;
                    var born = ZoneSpawn(from, e.Added ? e.Glb : "", e.Sheet, cx + x, cz + z, cap + vcap, sc, y, lift, tilt);
                    if (born == null) { lines.Add($"  ? {nom} : copie impossible"); skipped++; continue; }
                    lines.Add(FormattableString.Invariant($"  + {born.Id} : copie de {(from.Length > 0 ? from : e.Glb.Length > 0 ? e.Glb : e.Sheet)} en ({born.X:F2}, {born.Z:F2})"));
                    added++;
                    after.Add((nom, Known(born.Id, x, y, z, cap, sc, vcap, lift, m["famille"]?.GetValue<string>() ?? "", tilt)));
                }
            }
            else
            {
                /* UN MODÈLE BRUT, tiré de la palette — celle du .blend, ou palette.glb importé
                   après coup (« brut:<fichier> », à sa taille de jeu, posé sur son pied). Un
                   fichier importé TEL QUEL n'est pas reconnu, et c'est voulu : son unité et son
                   nœud racine changent d'un fichier à l'autre (un tonneau de cent unités sous
                   une racine qui le tourne), il ne tomberait pas où on l'a vu. */
                string? rel = objs[bas] is JsonObject pm && pm["brut"] != null ? pm["brut"]!.GetValue<string>()
                            : bas.StartsWith("brut:", StringComparison.Ordinal) ? bas[5..] : null;
                if (rel == null)
                {
                    lines.Add($"  ? {nom} : nom inconnu — un nouveau modèle se prend dans la palette (node tools/blender-zone.js palette), laissé");
                    skipped++; continue;
                }
                if (!inside) continue;   // resté dans la palette
                var born = ZoneSpawn("", rel, "", cx + x, cz + z, cap, sc, y, 0, tilt);
                if (born == null) { lines.Add($"  ? {nom} : {rel} illisible"); skipped++; continue; }
                lines.Add(FormattableString.Invariant($"  + {born.Id} : {rel} en ({born.X:F2}, {born.Z:F2})"));
                added++;
                after.Add((nom, Known(born.Id, x, y, z, cap, sc, 0, 0, System.IO.Path.GetFileNameWithoutExtension(rel), tilt)));
            }
        }

        // ce qu'on ne retrouve plus a été supprimé dans Blender
        foreach (var kv in objs)
        {
            if (kv.Value!["id"] == null || seen.Contains(kv.Key)) continue;
            string id = kv.Value["id"]!.GetValue<string>();
            if (!_editReg!.ById.TryGetValue(id, out var e) || e.Removed) continue;
            e.Removed = true;
            _editReg.Commit(e);
            lines.Add($"  - {id} retiré");
            removed++;
            after.Add((kv.Key, null));
        }

        foreach (var l in lines) GD.Print(l);
        string verdict;
        if (_zoneTry) verdict = "ESSAI : rien d'écrit";
        else if (moved + added + removed == 0) verdict = "rien à écrire";
        else if (_editReg!.Save())
        {
            foreach (var (k, v) in after)
                if (v == null) objs.Remove(k); else objs[k] = v;
            File.WriteAllText(manPath, man.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            verdict = $"écrit dans {_editReg.Path} ; le .blend est désormais l'état connu";
        }
        else verdict = "NON écrit : le fichier des retouches était illisible";
        GD.Print($"[blender] retour de {isl.Name} : {moved} déplacé(s), {added} ajouté(s), {removed} retiré(s), {skipped} laissé(s) — {verdict}");
        return true;
    }

    /* LA PENTE EN JSON : [x, y, z, w], absente quand l'objet est droit. Un relevé
       d'avant la pente n'y mettait qu'un angle : il compte pour droit. */
    static JsonArray TiltJson(Quaternion q) =>
        new(Math.Round(q.X, 6), Math.Round(q.Y, 6), Math.Round(q.Z, 6), Math.Round(q.W, 6));

    static Quaternion TiltRead(JsonNode? n)
    {
        if (n is not JsonArray a || a.Count != 4) return Quaternion.Identity;
        var q = new Quaternion((float)a[0]!.GetValue<double>(), (float)a[1]!.GetValue<double>(),
                               (float)a[2]!.GetValue<double>(), (float)a[3]!.GetValue<double>()).Normalized();
        // un dixième de degré : du bruit de Blender, l'objet est droit
        return Mathf.Abs(q.W) > 0.9999996f ? Quaternion.Identity : q;
    }

    static bool TiltSame(Quaternion a, Quaternion b) => Mathf.Abs(a.Dot(b)) > 0.9999996f;

    /// <summary>
    /// POSER COMME LA SOURIS : place, cap, échelle, puis la hauteur. Une hauteur qu'on n'a
    /// pas touchée dans Blender laisse l'objet suivre le sol à sa nouvelle place (comme un
    /// glisser) ; une hauteur changée est prise telle quelle, en levée sur ce sol.
    /// </summary>
    void ZonePose(Editable e, double x, double z, double yaw, double scale, double? y, double lift, Quaternion tilt)
    {
        e.X = x; e.Z = z; e.Yaw = yaw; e.Scale = scale; e.Removed = false; e.Tilt = tilt;
        if (y is double want)
        {
            e.Dy = 0;
            e.Push?.Invoke(e);
            e.Dy = Math.Round(want - lift * scale - e.GroundY, 2);
        }
        _editReg!.Commit(e);
    }

    Editable? ZoneSpawn(string from, string glb, string sheet, double x, double z, double yaw, double scale, double y, double lift, Quaternion tilt)
    {
        string id = _editReg!.NextAddId();
        var e = SpawnAdded(id, from, glb, x, z, yaw, scale, 0, sheet);
        if (e == null) return null;
        if (sheet.Length == 0) e.Tilt = tilt;
        if (sheet.Length > 0) { _editReg.Commit(e); return e; }
        // la hauteur d'un ajout se lit telle qu'on l'a posée : sa levée sur le sol de sa place
        e.Push?.Invoke(e);
        double dy = Math.Round(y - lift * scale - e.GroundY, 2);
        e.Dy = Math.Abs(dy) < 0.03 ? 0 : dy;
        _editReg.Commit(e);
        return e;
    }
}

/// <summary>
/// UN .GLB ÉCRIT À LA MAIN, pour Blender : des maillages partagés (cent maisons d'un même
/// modèle n'en font qu'un), une couleur et une image de base par matière, rien d'autre.
/// Chaque objet du jeu devient UN nœud portant UN maillage — ses morceaux y sont fondus
/// en primitives —, pour qu'un clic dans Blender prenne la maison entière.
/// </summary>
sealed class GlbOut
{
    readonly MemoryStream _bin = new();
    readonly JsonArray _views = new(), _acc = new(), _meshes = new(), _mats = new(), _nodes = new(),
                       _images = new(), _textures = new();
    readonly Dictionary<string, int> _matBy = new(), _meshBy = new();
    readonly Dictionary<ulong, int> _texBy = new();

    public int MeshCount => _meshes.Count;

    int View(byte[] data, int? target)
    {
        while (_bin.Length % 4 != 0) _bin.WriteByte(0);
        var v = new JsonObject { ["buffer"] = 0, ["byteOffset"] = _bin.Length, ["byteLength"] = data.Length };
        if (target is int t) v["target"] = t;
        _bin.Write(data);
        _views.Add(v);
        return _views.Count - 1;
    }

    int Floats(float[] f, string type, bool minmax)
    {
        var bytes = new byte[f.Length * 4];
        Buffer.BlockCopy(f, 0, bytes, 0, bytes.Length);
        int comps = type == "VEC3" ? 3 : 2;
        var a = new JsonObject { ["bufferView"] = View(bytes, 34962), ["componentType"] = 5126, ["count"] = f.Length / comps, ["type"] = type };
        if (minmax)
        {
            var mn = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var mx = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < f.Length; i += 3)
                for (int k = 0; k < 3; k++) { mn[k] = Math.Min(mn[k], f[i + k]); mx[k] = Math.Max(mx[k], f[i + k]); }
            a["min"] = new JsonArray(mn[0], mn[1], mn[2]);
            a["max"] = new JsonArray(mx[0], mx[1], mx[2]);
        }
        _acc.Add(a);
        return _acc.Count - 1;
    }

    int Indices(uint[] idx)
    {
        var bytes = new byte[idx.Length * 4];
        Buffer.BlockCopy(idx, 0, bytes, 0, bytes.Length);
        _acc.Add(new JsonObject { ["bufferView"] = View(bytes, 34963), ["componentType"] = 5125, ["count"] = idx.Length, ["type"] = "SCALAR" });
        return _acc.Count - 1;
    }

    JsonObject Prim(float[] pos, float[]? nor, float[]? uv, uint[] idx, int mat)
    {
        var at = new JsonObject { ["POSITION"] = Floats(pos, "VEC3", true) };
        if (nor != null) at["NORMAL"] = Floats(nor, "VEC3", false);
        if (uv != null) at["TEXCOORD_0"] = Floats(uv, "VEC2", false);
        return new JsonObject { ["attributes"] = at, ["indices"] = Indices(idx), ["material"] = mat };
    }

    public int RawMesh(string name, float[] pos, float[] nor, float[] uv, uint[] idx, int mat)
    {
        _meshes.Add(new JsonObject { ["name"] = name, ["primitives"] = new JsonArray(Prim(pos, nor, uv, idx, mat)) });
        return _meshes.Count - 1;
    }

    public int ColorMaterial(string name, Color c, bool blend = false, int tex = -1)
    {
        string key = FormattableString.Invariant($"{name}|{c.R:F3},{c.G:F3},{c.B:F3},{c.A:F3}|{blend}|{tex}");
        if (_matBy.TryGetValue(key, out int have)) return have;
        var srgb = c.LinearToSrgb();
        var pbr = new JsonObject
        {
            ["baseColorFactor"] = new JsonArray(srgb.R, srgb.G, srgb.B, c.A),
            ["metallicFactor"] = 0, ["roughnessFactor"] = 0.9
        };
        if (tex >= 0) pbr["baseColorTexture"] = new JsonObject { ["index"] = tex };
        var m = new JsonObject { ["name"] = name, ["pbrMetallicRoughness"] = pbr };
        if (blend) { m["alphaMode"] = "BLEND"; m["doubleSided"] = true; }
        _mats.Add(m);
        return _matBy[key] = _mats.Count - 1;
    }

    public int TextureMaterial(string name, Image img) => ColorMaterial(name, Colors.White, false, Texture(img));

    int Texture(Image img)
    {
        byte[] png = img.SavePngToBuffer();
        _images.Add(new JsonObject { ["bufferView"] = View(png, null), ["mimeType"] = "image/png" });
        _textures.Add(new JsonObject { ["source"] = _images.Count - 1 });
        return _textures.Count - 1;
    }

    /// <summary>L'image d'une texture du jeu, décompressée et ramenée à 1024 au plus ; -1 si illisible.</summary>
    int Texture(Texture2D t)
    {
        ulong key = t.GetInstanceId();
        if (_texBy.TryGetValue(key, out int have)) return have;
        int outp = -1;
        try
        {
            var img = t.GetImage();
            if (img != null && !img.IsEmpty())
            {
                img = (Image)img.Duplicate();
                if (img.IsCompressed()) img.Decompress();
                img.ClearMipmaps();
                int w = img.GetWidth(), h = img.GetHeight();
                if (Math.Max(w, h) > 1024)
                {
                    float k = 1024f / Math.Max(w, h);
                    img.Resize(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)));
                }
                img.Convert(img.DetectAlpha() == Image.AlphaMode.None ? Image.Format.Rgb8 : Image.Format.Rgba8);
                outp = Texture(img);
            }
        }
        catch (Exception) { outp = -1; }
        return _texBy[key] = outp;
    }

    /// <summary>Ce qu'une matière du jeu donne à voir : sa couleur et son image de base, si on les trouve.</summary>
    int Material(Material? m)
    {
        Color c = new(0.7f, 0.7f, 0.7f);
        Texture2D? tex = null;
        bool blend = false;
        string name = "matiere";
        if (m is BaseMaterial3D b)
        {
            c = b.AlbedoColor.SrgbToLinear();
            tex = b.AlbedoTexture;
            blend = b.Transparency != BaseMaterial3D.TransparencyEnum.Disabled && c.A < 0.99f;
            name = b.ResourceName.Length > 0 ? b.ResourceName : "matiere";
        }
        else if (m is ShaderMaterial s)
        {
            foreach (var p in new[] { "albedo", "albedo_color", "u_albedo", "u_color", "base_color", "u_tint" })
                if (s.GetShaderParameter(p).VariantType == Variant.Type.Color) { c = s.GetShaderParameter(p).AsColor().SrgbToLinear(); break; }
            foreach (var p in new[] { "texture_albedo", "albedo_texture", "u_tex", "u_albedo_tex", "u_map" })
                if (s.GetShaderParameter(p).Obj is Texture2D t) { tex = t; break; }
        }
        int ti = tex != null ? Texture(tex) : -1;
        return ColorMaterial(name, ti >= 0 ? new Color(c.R, c.G, c.B, c.A) : c, blend, ti);
    }

    /// <summary>
    /// UN OBJET DU JEU EN UN MAILLAGE : chaque MeshInstance3D de l'arbre, sa pose relative
    /// cuite dans ses sommets. Partagé entre tous les objets qui donnent la même chose
    /// (même maillage, même pose, même matière). -1 s'il n'y a rien à dessiner.
    /// </summary>
    public int Visual(Node3D root)
    {
        var parts = new List<(Mesh Mesh, Transform3D Xf, Material?[] Mats)>();
        Walk(root, Transform3D.Identity, parts, true);
        if (parts.Count == 0) return -1;
        var sig = new StringBuilder();
        var mats = new List<int[]>();
        foreach (var p in parts)
        {
            var mi = new int[p.Mats.Length];
            for (int s = 0; s < mi.Length; s++) mi[s] = Material(p.Mats[s]);
            mats.Add(mi);
            var o = p.Xf.Origin; var bx = p.Xf.Basis;
            sig.Append(p.Mesh.GetInstanceId()).Append('|')
               .Append(FormattableString.Invariant($"{o.X:F3},{o.Y:F3},{o.Z:F3},{bx.Column0.X:F4},{bx.Column0.Y:F4},{bx.Column0.Z:F4},{bx.Column1.Y:F4},{bx.Column2.X:F4},{bx.Column2.Z:F4}|"))
               .Append(string.Join(",", mi)).Append(';');
        }
        string key = sig.ToString();
        if (_meshBy.TryGetValue(key, out int have)) return have;

        var prims = new JsonArray();
        for (int pi = 0; pi < parts.Count; pi++)
        {
            var (mesh, xf, _) = parts[pi];
            bool mirror = xf.Basis.Determinant() < 0;
            var nb = xf.Basis.Inverse().Transposed();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if (mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
                var arr = mesh.SurfaceGetArrays(s);
                if (arr.Count <= (int)Mesh.ArrayType.Index) continue;
                var vtx = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                if (vtx.Length == 0) continue;
                var nrm = arr[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var tuv = arr[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var ind = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                var pos = new float[vtx.Length * 3];
                for (int i = 0; i < vtx.Length; i++)
                {
                    var v = xf * vtx[i];
                    pos[i * 3] = v.X; pos[i * 3 + 1] = v.Y; pos[i * 3 + 2] = v.Z;
                }
                float[]? nor = null;
                if (nrm != null && nrm.Length == vtx.Length)
                {
                    nor = new float[nrm.Length * 3];
                    for (int i = 0; i < nrm.Length; i++)
                    {
                        var v = (nb * nrm[i]).Normalized();
                        nor[i * 3] = v.X; nor[i * 3 + 1] = v.Y; nor[i * 3 + 2] = v.Z;
                    }
                }
                float[]? uv = null;
                if (tuv != null && tuv.Length == vtx.Length)
                {
                    uv = new float[tuv.Length * 2];
                    for (int i = 0; i < tuv.Length; i++) { uv[i * 2] = tuv[i].X; uv[i * 2 + 1] = tuv[i].Y; }
                }
                int cnt = ind?.Length ?? vtx.Length;
                cnt -= cnt % 3;
                var idx = new uint[cnt];
                // Godot tient les faces avant dans le sens horaire, glTF dans l'autre
                for (int i = 0; i < cnt; i += 3)
                {
                    uint a = (uint)(ind?[i] ?? i), b = (uint)(ind?[i + 1] ?? i + 1), c = (uint)(ind?[i + 2] ?? i + 2);
                    idx[i] = a;
                    if (mirror) { idx[i + 1] = b; idx[i + 2] = c; } else { idx[i + 1] = c; idx[i + 2] = b; }
                }
                prims.Add(Prim(pos, nor, uv, idx, mats[pi][Math.Min(s, mats[pi].Length - 1)]));
            }
        }
        if (prims.Count == 0) return _meshBy[key] = -1;
        _meshes.Add(new JsonObject { ["name"] = root.Name.ToString().Length > 0 ? root.Name.ToString() : "objet", ["primitives"] = prims });
        return _meshBy[key] = _meshes.Count - 1;
    }

    static void Walk(Node n, Transform3D parent, List<(Mesh, Transform3D, Material?[])> parts, bool isRoot)
    {
        var xf = parent;
        if (n is Node3D n3)
        {
            if (!n3.Visible) return;
            xf = parent * n3.Transform;
        }
        if (n is MeshInstance3D mi && mi.Mesh != null && mi.VisibilityRangeBegin <= 0
            && !mi.Name.ToString().EndsWith(Assets.LodLowSuffix, StringComparison.Ordinal))
        {
            var mats = new Material?[Math.Max(1, mi.Mesh.GetSurfaceCount())];
            for (int s = 0; s < mats.Length; s++)
                mats[s] = mi.MaterialOverride ?? (s < mi.Mesh.GetSurfaceCount() ? mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s) : null);
            parts.Add((mi.Mesh, xf, mats));
        }
        foreach (var c in n.GetChildren()) Walk(c, xf, parts, false);
    }

    /// <summary>Une boîte, pour ce qui n'a pas de modèle à copier (un navire posé).</summary>
    public int Box(double radius, double height)
    {
        string key = FormattableString.Invariant($"boite|{radius:F2}|{height:F2}");
        if (_meshBy.TryGetValue(key, out int have)) return have;
        float r = (float)Math.Max(0.5, radius), h = (float)Math.Max(0.5, height);
        var pos = new List<float>(); var nor = new List<float>(); var idx = new List<uint>();
        void Face(Vector3 nrm, Vector3 u, Vector3 v, Vector3 c)
        {
            uint b = (uint)(pos.Count / 3);
            foreach (var (su, sv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var p = c + u * su + v * sv;
                pos.Add(p.X); pos.Add(p.Y); pos.Add(p.Z); nor.Add(nrm.X); nor.Add(nrm.Y); nor.Add(nrm.Z);
            }
            idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        var X = new Vector3(r, 0, 0); var Y = new Vector3(0, h * 0.5f, 0); var Z = new Vector3(0, 0, r);
        var C = new Vector3(0, h * 0.5f, 0);
        Face(Vector3.Up, Z, X, C + Y); Face(Vector3.Down, X, Z, C - Y);
        Face(Vector3.Right, Y, Z, C + X); Face(Vector3.Left, Z, Y, C - X);
        Face(Vector3.Back, X, Y, C + Z); Face(Vector3.Forward, Y, X, C - Z);
        int mat = ColorMaterial("repere", new Color(0.9f, 0.4f, 0.1f));
        _meshes.Add(new JsonObject { ["name"] = "repere", ["primitives"] = new JsonArray(Prim(pos.ToArray(), nor.ToArray(), null, idx.ToArray(), mat)) });
        return _meshBy[key] = _meshes.Count - 1;
    }

    public int Node(string name, int mesh, Vector3 at, double yaw, double scale, Quaternion? tilt = null)
    {
        // la pente après le cap, comme Editable.Orient
        var q = (tilt ?? Quaternion.Identity) * new Quaternion(Vector3.Up, (float)yaw);
        var n = new JsonObject
        {
            ["name"] = name, ["mesh"] = mesh,
            ["translation"] = new JsonArray(at.X, at.Y, at.Z),
            ["rotation"] = new JsonArray(q.X, q.Y, q.Z, q.W)
        };
        if (Math.Abs(scale - 1) > 1e-6) n["scale"] = new JsonArray((float)scale, (float)scale, (float)scale);
        _nodes.Add(n);
        return _nodes.Count - 1;
    }

    public void Write(string path, string scene, List<int> top)
    {
        while (_bin.Length % 4 != 0) _bin.WriteByte(0);
        var nodes = new JsonArray();
        foreach (var i in top) nodes.Add(i);
        var gltf = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "naval-sim zone-blender" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["name"] = scene, ["nodes"] = nodes }),
            ["nodes"] = _nodes, ["meshes"] = _meshes, ["materials"] = _mats,
            ["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = _bin.Length }),
            ["bufferViews"] = _views, ["accessors"] = _acc
        };
        if (_images.Count > 0)
        {
            gltf["images"] = _images; gltf["textures"] = _textures;
        }
        byte[] json = Encoding.UTF8.GetBytes(gltf.ToJsonString());
        int jpad = (4 - json.Length % 4) % 4;
        byte[] bin = _bin.ToArray();
        using var f = File.Create(path);
        using var w = new BinaryWriter(f);
        w.Write(0x46546C67u); w.Write(2u);
        w.Write((uint)(12 + 8 + json.Length + jpad + 8 + bin.Length));
        w.Write((uint)(json.Length + jpad)); w.Write(0x4E4F534Au);
        w.Write(json); for (int i = 0; i < jpad; i++) w.Write((byte)0x20);
        w.Write((uint)bin.Length); w.Write(0x004E4942u);
        w.Write(bin);
    }
}
