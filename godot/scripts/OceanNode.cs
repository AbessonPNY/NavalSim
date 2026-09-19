using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// La mer, côté moteur : un plan, un matériau, et le pont qui porte le spectre du
/// noyau jusqu'aux uniformes du shader.
///
/// Elle ne DÉCIDE de rien. Le spectre, la phase et l'origine flottante vivent
/// dans <see cref="NavalSim.Core.Ocean"/>, qui ne connaît pas Godot et que le
/// banc de parité compare au JavaScript d'origine. Ce fichier ne fait que
/// recopier ce que le noyau a calculé, une fois par image.
/// </summary>
public partial class OceanNode : Node3D
{
    /// <summary>Le noyau. C'est lui qu'on interroge pour savoir où est l'eau.</summary>
    public Ocean Core { get; } = new();

    MeshInstance3D _plane = null!;
    ShaderMaterial _mat = null!;

    /// <summary>Le materiau de la mer, pour que SkyNode y ecrive le meme ciel.</summary>
    public ShaderMaterial? Material => _mat;

    // les réserves d'uniformes, écrites sur place : reconstruire ces tableaux
    // soixante fois par seconde ferait mille objets éphémères par seconde, donc
    // des pauses de ramasse-miettes — et une pause de ramasse-miettes EST une
    // saccade. Même discipline que setSeaState côté JavaScript.
    readonly Godot.Collections.Array _waveA = new();
    readonly Godot.Collections.Array _waveB = new();
    readonly float[] _wavePhase = new float[Config.NWaves];

    public override void _Ready()
    {
        for (int i = 0; i < Config.NWaves; i++)
        {
            _waveA.Add(Variant.From(Vector4.Zero));
            _waveB.Add(Variant.From(Vector2.Zero));
        }

        var shader = GD.Load<Shader>("res://shaders/ocean.gdshader");
        if (shader == null)
        {
            GD.PushError("shaders/ocean.gdshader introuvable");
            return;
        }
        /* LA PREMIÈRE DES MATIÈRES TRANSPARENTES. La mer lit l'image et la
           profondeur déjà rendues pour montrer la coque sous l'eau, ce qui la fait
           passer après tout l'opaque ; la priorité la plus basse la met avant tout
           le reste du transparent — brume des modèles, feux, embrun —, qui se teste
           alors contre la profondeur qu'elle écrit (depth_draw_always). */
        _mat = new ShaderMaterial { Shader = shader, RenderPriority = -1 };

        /* Le plan, et son nombre de segments : c'est le même OCEAN_SEG que le
           JavaScript, parce que le remaillage vers la caméra du vertex shader est
           calibré dessus — la taille locale de la maille sort de sa dérivée, et
           elle sert de critère unique à l'anticrénelage des vagues. */
        var mesh = new PlaneMesh
        {
            Size = new Vector2((float)Config.OceanSize, (float)Config.OceanSize),
            SubdivideWidth = Config.OceanSeg - 1,
            SubdivideDepth = Config.OceanSeg - 1
        };

        _plane = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = _mat,
            // le déplacement sort du vertex shader, donc la boîte que Godot a
            // calculée sur le plan au repos est fausse : sans cela il l'écarte du
            // rendu dès que la caméra regarde de côté
            CustomAabb = new Aabb(
                new Vector3(-(float)Config.OceanSize, -60, -(float)Config.OceanSize),
                new Vector3((float)Config.OceanSize * 2, 120, (float)Config.OceanSize * 2)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(_plane);

        _mat.SetShaderParameter(U.Half, (float)Config.OceanSize * 0.5f);
        _mat.SetShaderParameter(U.Seg, (float)Config.OceanSeg);
    }

    /// <summary>
    /// Tient le plan sous l'œil (l'illusion de mer sans fin) et recopie le
    /// spectre. La phase vient du noyau, réduite modulo 2π en DOUBLE : c'est ce
    /// qui la laisse tenir exactement dans le flottant 32 bits du shader, si loin
    /// qu'elle navigue.
    /// </summary>
    public void UpdateFrom(Vector3 eye, double t)
    {
        if (_mat == null) return;
        Core.Time = t;

        var centre = new Vector3(eye.X, 0, eye.Z);
        _plane.Position = centre;
        _mat.SetShaderParameter(U.Centre, centre);
        _mat.SetShaderParameter(U.Sharp, (float)Core.Sharp);
        _mat.SetShaderParameter(U.AmpMax, (float)Core.AmpMax);
        FillWaves();
        PushWaves(_mat);
    }

    /// <summary>
    /// Le spectre et l'horloge, vers tout shader qui lit la houle. UNE écriture
    /// pour tous : la mer et le champ d'écume doivent déferler sur les MÊMES
    /// vagues, et deux copies du tableau finiraient par ne plus l'être. Pose ce
    /// que <see cref="UpdateFrom"/> a rempli pour cette image : l'appeler après.
    /// </summary>
    public void PushWaves(ShaderMaterial m)
    {
        m.SetShaderParameter(U.Time, (float)Core.Time);
        m.SetNow(U.WaveA, _waveA);
        m.SetNow(U.WaveB, _waveB);
        m.SetNow(U.WavePhase, _wavePhase);
    }

    /// <summary>
    /// Le champ d'écume persistant, que la mer lit désormais. Taille une fois ;
    /// texture et ancre à chaque image, par <see cref="SyncFoam"/>.
    /// </summary>
    public void AttachFoam(FoamField foam)
    {
        _foam = foam;
        _mat?.SetShaderParameter(U.FoamSize, FoamField.Size);
        _mat?.SetShaderParameter(U.FoamOn, 1.0f);
    }

    FoamField? _foam;

    // les feux du bord, pour la mer qu'aucune lampe du moteur n'atteint (NLAMP)
    public readonly Vector4[] Lamps = new Vector4[8];
    public readonly float[] LampRange = new float[8];

    /// <summary>Les <paramref name="count"/> premiers feux de <see cref="Lamps"/>, vers la mer.</summary>
    public void PushLamps(int count)
    {
        if (_mat == null) return;
        _mat.SetShaderParameter(U.LampCount, count);
        if (count == 0) return;
        _mat.SetNow(U.Lamp, Lamps);
        _mat.SetNow(U.LampRange, LampRange);
    }

    // ------------------------------------------------------------------
    //  LA FLOTTE, VUE PAR LA MER — pour le collier d'écume et le sillage
    // ------------------------------------------------------------------

    /* Une rangée de demi-largeurs par navire ; l'indice dans la flotte EST la
       rangée, comme dans l'original. Un rectangle tant qu'on ne lui a rien
       dit : faux, mais jamais absent. */
    Image? _profImg;
    ImageTexture? _profTex;
    readonly HullProfile?[] _profs = new HullProfile?[Config.MaxShips];

    readonly Vector3[] _shipPos = new Vector3[Config.MaxShips];
    readonly Vector2[] _shipFwd = new Vector2[Config.MaxShips];
    readonly Vector2[] _shipHalf = new Vector2[Config.MaxShips];
    readonly Vector2[] _hullEnds = new Vector2[Config.MaxShips];
    readonly float[] _shipSpeed = new float[Config.MaxShips];
    readonly float[] _shipAfloat = new float[Config.MaxShips];
    int _shipCount;

    const int ProfCols = 64;

    /// <summary>Donner à la mer le contour d'un navire, dans SA rangée.</summary>
    public void SetHullProfile(int index, HullProfile prof)
    {
        if (index < 0 || index >= Config.MaxShips) return;
        if (_profImg == null)
        {
            _profImg = Image.CreateEmpty(ProfCols, Config.MaxShips, false, Image.Format.Rf);
            _profImg.Fill(new Color(1, 1, 1, 1));
        }
        int n = prof.Fractions.Length;
        for (int i = 0; i < ProfCols; i++)
        {
            // un profil plus court est étiré sur la rangée plutôt que laissé blanc
            float f = prof.Fractions[Math.Min(n - 1, i * n / ProfCols)];
            _profImg.SetPixel(i, index, new Color(f, 0, 0, 1));
        }
        if (_profTex == null) _profTex = ImageTexture.CreateFromImage(_profImg);
        else _profTex.Update(_profImg);

        _profs[index] = prof;
        _hullEnds[index] = new Vector2((float)prof.EndAft, (float)prof.EndFwd);
    }

    /// <summary>
    /// Dire à la mer où est chaque coque à flot, une fois par image. L'entrée 0
    /// est le navire commandé ; les rangées au-delà du compte ne sont pas lues.
    /// </summary>
    public void TrackShips(IReadOnlyList<ShipPhysics> fleet)
    {
        _shipCount = Math.Min(fleet.Count, Config.MaxShips);
        for (int i = 0; i < _shipCount; i++)
        {
            var p = fleet[i];
            var b = p.Body;
            _shipPos[i] = new Vector3((float)b.Pos.X, (float)b.Pos.Y, (float)b.Pos.Z);
            Vec3d f = b.Quat.Rotate(new Vec3d(0, 0, 1));
            _shipFwd[i] = new Vector2((float)f.X, (float)f.Z).Normalized();
            // sa plus grande demi-largeur TELLE QUE MESURÉE, qui met son profil à
            // l'échelle : spec.B n'est que le chiffre annoncé
            _shipHalf[i] = new Vector2((float)(p.Spec.L * 0.5),
                (float)(_profs[i]?.MaxHalfB ?? p.Spec.B * 0.5));
            _shipSpeed[i] = (float)Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z);
            /* CE QU'IL RESTE D'ELLE À LA SURFACE. Le solveur l'a déjà : le collier,
               la gerbe d'étrave et le sillage appartiennent à une coque qui FEND
               l'eau, et une épave n'en fend plus. Envoyé 1 quoi qu'il arrive, le
               collier restait sur l'eau après le naufrage — signalé à l'usage. */
            _shipAfloat[i] = (float)p.Afloat;
            // sans profil, ses extrémités sont celles de sa longueur hors tout
            if (_profs[i] == null)
                _hullEnds[i] = new Vector2((float)(-p.Spec.L * 0.5), (float)(p.Spec.L * 0.5));
        }
        TrackWakes(fleet);
        if (_mat != null) PushShips(_mat);
    }

    /// <summary>
    /// La flotte vers tout shader qui inclut hull_gap : la mer et le champ
    /// d'écume lisent les MÊMES tableaux, écrits ici seulement.
    /// </summary>
    public void PushShips(ShaderMaterial m)
    {
        m.SetShaderParameter(U.ShipCount, _shipCount);
        if (_profTex != null) m.SetShaderParameter(U.HullProf, _profTex);
        m.SetNow(U.ShipPos, _shipPos);
        m.SetNow(U.ShipFwd, _shipFwd);
        m.SetNow(U.ShipHalf, _shipHalf);
        m.SetNow(U.HullEnds, _hullEnds);
        m.SetNow(U.ShipSpeed, _shipSpeed);
        m.SetNow(U.ShipAfloat, _shipAfloat);
        if (_kelvinTex != null) m.SetShaderParameter("u_kelvin", _kelvinTex);
    }

    /// <summary>
    /// La cible que le champ vient de rendre, et SON ancre — les deux de la même
    /// passe, faute de quoi l'écume glisserait d'un pas à chaque image.
    /// </summary>
    public void SyncFoam()
    {
        if (_foam == null || _mat == null) return;
        _mat.SetShaderParameter(U.FoamTex, _foam.Texture);
        _mat.SetShaderParameter(U.FoamOrigin, _foam.Origin);
    }

    void FillWaves()
    {
        for (int i = 0; i < Config.NWaves; i++)
        {
            ref Wave w = ref Core.Waves[i];
            _waveA[i] = Variant.From(new Vector4((float)w.Dx, (float)w.Dz, (float)w.Amp, (float)w.K));
            _waveB[i] = Variant.From(new Vector2((float)w.Omega, (float)w.Q));
            _wavePhase[i] = (float)w.Phase;
        }
    }
}
