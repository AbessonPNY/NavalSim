using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// SA PÂLEUR — _ghostify et _show de ghosts.js. Ce qui la rend spectre à l'œil ;
/// ce qui la rend spectre au boulet est décidé autour d'elle (GhostScene).
/// </summary>
public partial class ShipNode
{
    /// <summary>Est-elle un spectre ?</summary>
    public bool IsGhost { get; private set; }

    readonly List<ShaderMaterial> _ghostMats = new();
    readonly List<MeshInstance3D> _ghostAura = new();
    readonly List<(MeshInstance3D Node, Vector3 Home, float Seed, float Spin)> _ghostMist = new();
    readonly List<(MeshInstance3D Node, Vector3 From, float Period, float Seed, float Size)> _ghostSteam = new();
    float _ghostBase, _mistRot;

    static readonly StringName UTint = "u_tint", UTex = "u_tex", UHasTex = "u_has_tex", UAlpha = "u_alpha",
        ULo = "u_lo", UHi = "u_hi", UTorn = "u_torn", URot = "u_rot", UPass = "u_pass";

    // la teinte de la page, 0xbfeee2, et combien elle tire les couleurs vers elle
    static readonly Color GhostPale = new Color(0xbf / 255f, 0xee / 255f, 0xe2 / 255f).SrgbToLinear();

    /// <summary>
    /// Tout ce qu'elle porte, rendu pâle : teinté vers un vert froid, lumineux du
    /// dedans, à moitié transparent, et fondu. Ses matières sont les siennes —
    /// chaque coque est bâtie à neuf —, elles sont donc remplacées sur place.
    /// </summary>
    public void Ghostify(double opacity, ImageTexture smoke)
    {
        if (IsGhost) return;
        IsGhost = true;
        _ghostBase = (float)opacity;
        float lo = 0.5f, hi = 0.5f + 0.15f * (float)Spec.L;
        var wood = GD.Load<Shader>("res://shaders/ship_ghost.gdshader");
        var cloth = GD.Load<Shader>("res://shaders/ship_ghost_sail.gdshader");

        /* Deux matières par matière : la profondeur seule, dessinée avant toute
           couleur de spectre, puis la couleur, qui ne passe plus que sur la peau la
           plus proche (ship_ghost.gdshaderinc). Rendue : la première, qui porte
           l'autre en passe suivante. */
        ShaderMaterial Ghost(Shader sh, Color srgb, Texture2D? tex, Texture2D? torn = null)
        {
            var c = srgb.SrgbToLinear().Lerp(GhostPale, 0.7f);
            ShaderMaterial Pass(float pass)
            {
                var m = new ShaderMaterial { Shader = sh };
                m.SetShaderParameter(UTint, new Vector3(c.R, c.G, c.B));
                if (tex != null) { m.SetShaderParameter(UTex, tex); m.SetShaderParameter(UHasTex, 1f); }
                if (torn != null) m.SetShaderParameter(UTorn, torn);
                m.SetShaderParameter(ULo, lo);
                m.SetShaderParameter(UHi, hi);
                m.SetShaderParameter(UAlpha, 0f);
                m.SetShaderParameter(UPass, pass);
                _ghostMats.Add(m);
                Hazed.Add(m);
                return m;
            }
            var depth = Pass(0);
            depth.RenderPriority = -10;
            depth.NextPass = Pass(1);
            return depth;
        }

        /* Sa toile en loques : une carte par voile, tirée parmi trois, pour qu'une
           ligne de spectres ne porte pas les mêmes plaies. */
        var sails = new Dictionary<(Material, int), ShaderMaterial>();
        foreach (var c in _canvases)
        {
            int v = (int)(Rng.Randi() % TornCount);
            if (!sails.TryGetValue((c.Mat, v), out var gm))
            {
                var src = c.Mat as ShaderMaterial;
                var col = src?.GetShaderParameter(U.Canvas).AsColor() ?? Colors.White;
                bool painted = src != null && src.GetShaderParameter(U.HasMap).AsSingle() > 0.5f;
                gm = Ghost(cloth, col, painted ? src!.GetShaderParameter(U.Map).As<Texture2D>() : null, Torn(v));
                sails[(c.Mat, v)] = gm;
            }
            c.Mat = gm;
            c.Mesh.SurfaceSetMaterial(0, gm);
            c.Node.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }

        // le reste : bois, métal, espars — ce qui a déjà sa pâleur (la toile) et les feux exceptés
        var done = new Dictionary<Material, ShaderMaterial>();
        var hullShader = GD.Load<Shader>("res://shaders/hull.gdshader");
        foreach (var (mi, _) in Meshes(this))
        {
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            if (mi.MaterialOverride is ShaderMaterial om)
            {
                if (om.Shader == hullShader)
                {
                    if (!done.TryGetValue(om, out var g))
                        done[om] = g = Ghost(wood, om.GetShaderParameter(U.Albedo).AsColor(), null);
                    mi.MaterialOverride = g;
                }
                continue;                                   // la toile, les feux
            }
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
                if (mat is ShaderMaterial) continue;
                if (mat == null || !done.TryGetValue(mat, out var g))
                {
                    var bm = mat as BaseMaterial3D;
                    g = Ghost(wood, bm?.AlbedoColor ?? Colors.White, bm?.AlbedoTexture);
                    if (mat != null) done[mat] = g;
                }
                mi.SetSurfaceOverrideMaterial(s, g);
            }
        }

        // ses fanaux brûlent vert
        var green = new Vector3(0x8d / 255f, 1f, 0xd8 / 255f);
        foreach (var L in _lanterns)
        {
            L.Halo.SetShaderParameter(U.Color, green);
            L.Mark.SetShaderParameter(U.Color, green);
            L.Light.LightColor = new Color(0x7d / 255f, 1f, 0xc8 / 255f);
        }

        /* LA BRUME où elle glisse : onze bouffées au ras de l'eau, le long d'elle et
           de part et d'autre, qui dérivent et respirent chacune à sa façon. */
        var quad = new QuadMesh { Size = Vector2.One };
        var mist = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/ghost_mist.gdshader") };
        mist.SetShaderParameter(UTex, smoke);
        float L0 = (float)Spec.L, B = (float)Spec.B;
        for (int k = 0; k < 11; k++)
        {
            float along = (k / 10f - 0.5f) * 1.25f * L0;
            float side2 = (k % 2 == 1 ? 1 : -1) * (B * 0.35f + Rng.Randf() * B * 0.9f);
            var home = new Vector3(side2, 0.8f + Rng.Randf() * 1.8f, along);
            float size = L0 * (0.35f + Rng.Randf() * 0.3f);
            var n = new MeshInstance3D
            {
                Mesh = quad, MaterialOverride = mist, Position = home,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = size
            };
            n.SetInstanceShaderParameter(U.Size, size);
            AddChild(n);
            _ghostMist.Add((n, home, Rng.Randf() * 100, (Rng.Randf() - 0.5f) * 0.1f));
        }

        /* LA VAPEUR QU'IL EXHALE : des volutes qui partent de ses ponts, montent en
           s'élargissant, penchent d'un côté puis de l'autre et s'évanouissent — une
           lente respiration, chacune à son rythme, pour qu'elles ne partent jamais
           ensemble. La même bouffée et le même vert que la brume du bas. */
        for (int k = 0; k < 14; k++)
        {
            var from = new Vector3((Rng.Randf() - 0.5f) * B * 0.8f, hi * (0.7f + Rng.Randf() * 0.5f),
                                   (Rng.Randf() - 0.5f) * 0.9f * L0);
            var n = new MeshInstance3D
            {
                Mesh = quad, MaterialOverride = mist, Position = from,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = L0
            };
            AddChild(n);
            _ghostSteam.Add((n, from, 7 + Rng.Randf() * 5, Rng.Randf() * 100, L0 * (0.22f + Rng.Randf() * 0.14f)));
        }

        /* UNE AURA : trois lueurs vertes le long d'elle, la texture même des fanaux,
           additives. Des panneaux et pas de lampe — le compte des lumières ne
           change pas. */
        var glow = GD.Load<Shader>("res://shaders/lantern_glow.gdshader");
        foreach (float zf in new[] { -0.3f, 0f, 0.3f })
        {
            var m = new ShaderMaterial { Shader = glow };
            m.SetShaderParameter(U.Color, new Vector3(0x6f / 255f, 1f, 0xd2 / 255f));
            m.SetShaderParameter(U.Size, L0 * 1.25f);
            m.SetShaderParameter(U.Opacity, 0f);
            var n = new MeshInstance3D
            {
                Mesh = quad, MaterialOverride = m, Position = new Vector3(0, L0 * 0.22f, zf * L0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = L0
            };
            AddChild(n);
            _ghostAura.Add(n);
        }
    }

    /// <summary>Une image de son fondu (0 absente, 1 entière) ; <paramref name="now"/> en secondes, pour ce qui palpite.</summary>
    public void GhostShow(double fade, double now, double dt, int side)
    {
        if (!IsGhost) return;
        float f = (float)fade;
        foreach (var m in _ghostMats) m.SetShaderParameter(UAlpha, _ghostBase * f);
        // l'aura vacille un peu, comme une lueur de spectre
        float aura = 0.32f * f * (0.8f + 0.2f * MathF.Sin((float)now * 2.1f + side * 2));
        foreach (var n in _ghostAura) ((ShaderMaterial)n.MaterialOverride).SetShaderParameter(U.Opacity, aura);
        _mistRot += (float)dt;
        foreach (var (n, home, seed, spin) in _ghostMist)
        {
            float tm = (float)now + seed;
            n.Position = new Vector3(home.X + MathF.Sin(tm * 0.23f) * 2.5f, home.Y + MathF.Sin(tm * 0.31f) * 0.4f,
                                     home.Z + MathF.Cos(tm * 0.17f) * 3f);
            // un angle par seconde, pas par image : la page tournait de spin·0,016 à chaque image
            n.SetInstanceShaderParameter(URot, seed + spin * 60 * _mistRot);
            n.SetInstanceShaderParameter(U.Opacity, 0.22f * f * (0.7f + 0.3f * MathF.Sin(tm * 0.5f)));
        }
        float L0 = (float)Spec.L;
        foreach (var (n, from, period, seed, size) in _ghostSteam)
        {
            // où elle en est de sa montée, de 0 (au pont) à 1 (évanouie)
            float u = (((float)now + seed) / period) % 1f;
            float sway = MathF.Sin(u * 3.1f + seed) * 0.12f * L0 * u;
            n.Position = new Vector3(from.X + sway, from.Y + u * 0.45f * L0, from.Z + 0.5f * sway);
            n.SetInstanceShaderParameter(U.Size, size * (0.45f + 1.1f * u));
            n.SetInstanceShaderParameter(URot, seed + u * 1.2f);
            // elle naît vite et s'éteint lentement
            n.SetInstanceShaderParameter(U.Opacity, 0.16f * f * MathF.Sin(MathF.PI * MathF.Pow(u, 0.6f)));
        }
    }

    /* ------------------------------------------------------------------ */
    /*  LA TOILE DÉCHIRÉE, dessinée plutôt que chargée — Naval.tornCanvas  */
    /* ------------------------------------------------------------------ */

    const int TornCount = 3;
    static ImageTexture[]? _torn;

    /* Une carte de transparence où le blanc est la toile et le noir ce qui manque :
       une bordure en langues qui pendent, des morsures aux deux chutes, des trous
       aux bords fendus. Les rangées vont de la têtière (en haut) à la bordure (en
       bas), comme les UV de la toile : les loques sont en bas. */
    static ImageTexture Torn(int v)
    {
        if (_torn == null)
        {
            var rng = new RandomNumberGenerator();
            _torn = new ImageTexture[TornCount];
            for (int i = 0; i < TornCount; i++) _torn[i] = DrawTorn(rng);
        }
        return _torn[v];
    }

    static ImageTexture DrawTorn(RandomNumberGenerator rng)
    {
        const int S = 256;
        var a = new byte[S * S];
        Array.Fill(a, (byte)255);
        float R() => rng.Randf();
        var poly = new List<Vector2>();

        // la bordure : des langues de toile qui pendent entre de profondes morsures
        poly.Add(new Vector2(0, S));
        float x = 0;
        while (x < S)
        {
            float w = 10 + R() * 28, up = S * (0.12f + R() * 0.38f);
            poly.Add(new Vector2(x + w * 0.3f, S - up * (0.4f + R() * 0.6f)));
            poly.Add(new Vector2(x + w * 0.55f, S - up));
            poly.Add(new Vector2(x + w * 0.8f, S - up * (0.3f + R() * 0.5f)));
            poly.Add(new Vector2(x + w, S - R() * S * 0.05f));
            x += w;
        }
        poly.Add(new Vector2(S, S));
        Fill(a, S, poly);

        // des morsures aux deux chutes
        for (int side = 0; side < 2; side++)
            for (int k = 0; k < 4; k++)
            {
                float y = S * (0.15f + R() * 0.6f), h = 12 + R() * 30, d = 6 + R() * 26;
                float ex = side == 1 ? S : 0, ix = side == 1 ? S - d : d;
                poly.Clear();
                poly.Add(new Vector2(ex, y)); poly.Add(new Vector2(ix, y + h * 0.3f));
                poly.Add(new Vector2(ex + (ix - ex) * 0.4f, y + h * 0.6f));
                poly.Add(new Vector2(ix * 0.9f + ex * 0.1f, y + h * 0.8f)); poly.Add(new Vector2(ex, y + h));
                Fill(a, S, poly);
            }

        // des trous, fendus en étoile
        for (int k = 0; k < 9; k++)
        {
            float cx = S * (0.12f + R() * 0.76f), cy = S * (0.1f + R() * 0.55f);
            float r = 5 + R() * 16;
            int n = 7 + (int)(R() * 5);
            poly.Clear();
            for (int i = 0; i < n; i++)
            {
                float an = i / (float)n * Mathf.Tau, rr = r * (i % 2 == 1 ? 0.35f + R() * 0.3f : 0.8f + R() * 0.7f);
                poly.Add(new Vector2(cx + MathF.Cos(an) * rr, cy + MathF.Sin(an) * rr * 1.3f));
            }
            Fill(a, S, poly);
        }

        var img = Image.CreateFromData(S, S, false, Image.Format.L8, a);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // un polygone rempli de noir, au centre de chaque pixel, par la règle pair-impair
    static void Fill(byte[] a, int S, List<Vector2> p)
    {
        float y0 = float.MaxValue, y1 = float.MinValue;
        foreach (var q in p) { y0 = MathF.Min(y0, q.Y); y1 = MathF.Max(y1, q.Y); }
        var xs = new List<float>();
        for (int y = Math.Max(0, (int)y0); y <= Math.Min(S - 1, (int)y1); y++)
        {
            float py = y + 0.5f;
            xs.Clear();
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                var A = p[i]; var B = p[j];
                if ((A.Y > py) == (B.Y > py)) continue;
                xs.Add(A.X + (py - A.Y) / (B.Y - A.Y) * (B.X - A.X));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = Math.Max(0, (int)MathF.Ceiling(xs[k] - 0.5f)); x <= Math.Min(S - 1, (int)MathF.Floor(xs[k + 1] - 0.5f)); x++)
                    a[y * S + x] = 0;
        }
    }
}
