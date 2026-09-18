using Godot;
using System;

namespace NavalSim;

/// <summary>
/// Le champ d'écume persistant, porté de foam.js : ce qui laisse sur l'eau, des
/// dizaines de secondes durant, l'écume des crêtes qui ont déferlé.
///
/// DEUX CIBLES EN PING-PONG, et c'est la forme idiomatique ici : chaque
/// <see cref="SubViewport"/> porte un rectangle plein cadre dont le shader lit
/// l'autre. On n'en rend qu'UN par image (<c>UpdateMode.Once</c>), en alternant.
///
/// Ce qui a été MESURÉ dans l'original et reste tel quel : 1024 texels sur
/// 620 m, un fondu à 1/e en 14 s, des demi-flottants — huit bits quantifiaient
/// le lent fondu en marches visibles et laissaient une écume pâle bloquée à une
/// valeur fixe —, et l'ancre calée sur des TEXELS ENTIERS : décalée d'une
/// fraction, chaque image rééchantillonnerait la précédente hors grille et le
/// champ se brouillerait en bouillie en quelques secondes.
/// </summary>
public partial class FoamField : Node
{
    public const int Res = 1024;
    public const float Size = 620f;          // mètres de monde couverts
    public const float Tau = 14f;            // secondes pour tomber à 1/e
    const float TexelWorld = Size / Res;

    readonly SubViewport[] _vp = new SubViewport[2];
    readonly ShaderMaterial[] _mat = new ShaderMaterial[2];
    int _cur;

    /// <summary>Le coin du champ en XZ monde (local à l'origine flottante).</summary>
    public Vector2 Origin { get; private set; } = new(-Size * 0.5f, -Size * 0.5f);
    Vector2 _prevOrigin = new(-Size * 0.5f, -Size * 0.5f);

    public Texture2D Texture => _vp[_cur].GetTexture();

    public override void _Ready()
    {
        var shader = GD.Load<Shader>("res://shaders/foam_field.gdshader");
        for (int i = 0; i < 2; i++)
        {
            _vp[i] = new SubViewport
            {
                Size = new Vector2I(Res, Res),
                // demi-flottants : voir plus haut
                UseHdr2D = true,
                TransparentBg = false,
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                RenderTargetClearMode = SubViewport.ClearMode.Always
            };
            _mat[i] = new ShaderMaterial { Shader = shader };
            _mat[i].SetShaderParameter("u_size", Size);
            _mat[i].SetShaderParameter("u_texel", 1f / Res);
            _vp[i].AddChild(new ColorRect
            {
                Size = new Vector2(Res, Res),
                Color = Colors.Black,
                Material = _mat[i]
            });
            AddChild(_vp[i]);

            /* PARTIR D'UNE EAU PROPRE, comme `_cleared` dans l'original. Une
               cible jamais rendue peut contenir n'importe quoi — NaN compris, et
               un NaN multiplié par le fondu reste NaN pour toujours. Une lecture
               décalée hors du champ ne lit rien : chaque cible est rendue une
               fois à vide, avant la première vraie passe. */
            _mat[i].SetShaderParameter("u_offset_uv", new Vector2(2, 2));
            _vp[i].RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
    }

    bool _armed;

    /// <summary>
    /// Le monde a glissé sous la flotte. Le champ est ancré dans le monde, donc
    /// ses DEUX ancres glissent avec lui — sans quoi l'image suivante lirait le
    /// recentrage comme un déplacement colossal et effacerait tout le champ.
    /// </summary>
    public void Rebase(float dx, float dz)
    {
        Origin -= new Vector2(dx, dz);
        _prevOrigin -= new Vector2(dx, dz);
    }

    /// <summary>
    /// Une passe : reporter, atténuer, déposer. <paramref name="centre"/> est la
    /// coque commandée, que le champ suit.
    /// </summary>
    public void Step(double dt, OceanNode sea, Vector3 centre)
    {
        _prevOrigin = Origin;
        Origin = new Vector2(
            Mathf.Floor(centre.X / TexelWorld) * TexelWorld - Size * 0.5f,
            Mathf.Floor(centre.Z / TexelWorld) * TexelWorld - Size * 0.5f);

        // l'image de l'armement : les deux cibles se vident, on ne dépose rien
        if (!_armed) { _armed = true; return; }

        int next = 1 - _cur;
        var m = _mat[next];
        m.SetShaderParameter("u_prev", _vp[_cur].GetTexture());
        m.SetShaderParameter("u_origin", Origin);
        m.SetShaderParameter("u_decay", (float)Math.Exp(-Math.Max(dt, 0) / Tau));
        // où relire, dans l'image d'avant, le même morceau de monde
        m.SetShaderParameter("u_offset_uv", (Origin - _prevOrigin) / Size);
        sea.PushWaves(m);

        _vp[next].RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _cur = next;
    }
}
