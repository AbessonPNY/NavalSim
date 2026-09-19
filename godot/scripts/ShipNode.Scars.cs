using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// SES BLESSURES — scar() de ship-model.js : une courte liste de points dans SON
/// repère, que ses matières lisent (ship_scar.gdshaderinc). À elle seule : deux
/// navires du même combat montrent chacun les leurs.
/// </summary>
public partial class ShipNode
{
    const int ScarMax = 24;                      // combien de plaies une coque montre à la fois
    readonly List<(Vector3 P, float W)> _scars = new();
    readonly Vector4[] _scarData = new Vector4[ScarMax];
    ShaderMaterial? _scarPass;
    /// <summary>Les matières qui portent ses blessures : coque et espars procéduraux, et la passe posée sur un modèle.</summary>
    readonly List<ShaderMaterial> _scarred = new();

    static readonly StringName UScar = "u_scar", UScarCount = "u_scar_count", UScarK = "u_scar_k",
        UScarTex = "u_scar_tex", UScarGrid = "u_scar_grid", UScarMaps = "u_scar_maps", UShipInv = "u_ship_inv";

    // une blessure à l'échelle du navire qui la prend
    float ScarK => (float)Math.Max(0.5, Spec.L / 30);

    void AddScarred(ShaderMaterial m)
    {
        _scarred.Add(m);
        m.SetShaderParameter(UScarK, ScarK);
        if (_atlas is ImpactAtlas a)
        {
            m.SetShaderParameter(UScarTex, a.Tex);
            m.SetShaderParameter(UScarGrid, new Vector2(a.Stages, a.Variants));
            m.SetShaderParameter(UScarMaps, 1f);
        }
        if (_scars.Count > 0) UploadScars();
    }

    /* UN COUP DANS SON FLANC. Tout près d'une marque, il l'AGGRAVE — elle glisse un
       peu vers le nouveau coup et prend du poids, ce qui fait passer une travée
       battue de l'éraflure à la plaie ; ailleurs il en ouvre une ; la liste pleine,
       il remplace la plus légère. `world` : où le boulet est entré. */
    public void Scar(Vector3 world, double k)
    {
        var loc = GlobalTransform.AffineInverse() * world;
        float add = (float)Math.Min(2, Math.Max(0.6, k * 2));
        float merge = 1.1f * ScarK;
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < _scars.Count; i++)
        {
            float d = _scars[i].P.DistanceTo(loc);
            if (d < bd) { bd = d; best = i; }
        }
        if (best >= 0 && bd < merge)
        {
            var s = _scars[best];
            _scars[best] = (s.P.Lerp(loc, 1 / (s.W + 1)), Math.Min(10, s.W + add));
        }
        else if (_scars.Count < ScarMax) _scars.Add((loc, add));
        else
        {
            int weak = 0;
            for (int i = 1; i < _scars.Count; i++) if (_scars[i].W < _scars[weak].W) weak = i;
            _scars[weak] = (loc, add);
        }
        UploadScars();
    }

    void UploadScars()
    {
        int n = Math.Min(_scars.Count, ScarMax);
        for (int i = 0; i < n; i++) _scarData[i] = new Vector4(_scars[i].P.X, _scars[i].P.Y, _scars[i].P.Z, _scars[i].W);
        foreach (var m in _scarred)
        {
            m.SetNow(UScar, _scarData);
            m.SetShaderParameter(UScarCount, n);
        }
        PushShipInverse();
    }

    // le monde → son repère : relu à chaque image tant qu'elle porte des blessures
    void PushShipInverse()
    {
        if (_scars.Count == 0) return;
        var inv = new Projection(GlobalTransform.AffineInverse());
        foreach (var m in _scarred) m.SetShaderParameter(UShipInv, inv);
    }

    void ClearScars()
    {
        _scars.Clear();
        foreach (var m in _scarred) m.SetShaderParameter(UScarCount, 0);
    }

    /* ------------------------------------------------------------------ */
    /*  LES IMPACTS PEINTS, en UNE texture                                 */
    /* ------------------------------------------------------------------ */

    readonly record struct ImpactAtlas(ImageTexture Tex, int Stages, int Variants);
    ImpactAtlas? _atlas;
    static readonly Dictionary<string, ImpactAtlas?> Atlases = new();

    /* La fiche donne une liste de variantes, chacune une liste de stades : posées
       sur une seule image, une rangée par variante et une colonne par stade, et le
       shader choisit sa case par arithmétique — comme les profils des coques
       partagent une texture, une rangée par navire. Bâtie une fois par liste et
       partagée par toutes les coques qui la nomment. Une variante plus courte
       répète son dernier stade. */
    void LoadImpactAtlas()
    {
        var list = Spec.Appearance.ImpactMaps;
        if (list == null || list.Count == 0) return;
        string key = string.Join("|", list.ConvertAll(v => string.Join(",", v)));
        if (!Atlases.TryGetValue(key, out var atlas))
        {
            const int CELL = 512;
            int variants = list.Count, stages = 0;
            foreach (var v in list) stages = Math.Max(stages, v.Count);
            var img = Image.CreateEmpty(CELL * stages, CELL * variants, false, Image.Format.Rgba8);
            int n = 0;
            for (int vi = 0; vi < variants; vi++)
                for (int si = 0; si < stages; si++)
                {
                    string src = list[vi][Math.Min(si, list[vi].Count - 1)];
                    var cell = Image.LoadFromFile(System.IO.Path.Combine(RepoRoot, src));
                    if (cell == null || cell.IsEmpty()) { GD.PushWarning($"[impacts] image introuvable : {src}"); continue; }
                    cell.Convert(Image.Format.Rgba8);
                    cell.Resize(CELL, CELL, Image.Interpolation.Lanczos);
                    img.BlitRect(cell, new Rect2I(0, 0, CELL, CELL), new Vector2I(si * CELL, vi * CELL));
                    n++;
                }
            if (n > 0)
            {
                img.GenerateMipmaps();
                atlas = new ImpactAtlas(ImageTexture.CreateFromImage(img), stages, variants);
            }
            else atlas = null;
            Atlases[key] = atlas;
        }
        _atlas = atlas;
    }
}
