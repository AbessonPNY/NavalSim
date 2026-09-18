using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE FLOU DE MOUVEMENT — Godot 4.7 n'en a pas ; il s'écrit en CompositorEffect,
/// un calcul inséré dans le rendu, après les matières transparentes pour que la
/// mer (transparente depuis qu'elle montre la coque sous l'eau) soit dedans.
///
/// Un flou de CAMÉRA, par reprojection : chaque pixel retrouve sa position par
/// la profondeur et la compare à la caméra de l'image précédente. Les vecteurs de
/// mouvement du moteur ne serviraient à rien ici : la mer est déformée dans son
/// shader et transparente, ils ne la voient pas bouger. Ce qui bouge AVEC la
/// caméra — le navire qu'elle suit — reste net, ce qui est ce qu'on veut.
///
/// La part d'obturateur est un 180° de cinéma par défaut (0,5) : le flou est le
/// déplacement pendant la moitié d'une image. Tourne sur le fil de RENDU ; rien
/// n'y est créé à chaque image — les objets de liaison sont gardés et remplis —,
/// pour ne rien laisser au ramasse-miettes.
/// </summary>
public partial class MotionBlurEffect : CompositorEffect
{
    public float Shutter = 0.5f;
    public int Samples = 8;
    public float MaxLength = 0.06f;       // fraction de l'écran

    RenderingDevice? _rd;
    Rid _copyShader, _copyPipeline, _shader, _linear, _nearest, _ubo;
    readonly byte[] _params = new byte[144];

    // la recopie : l'image du rendu vers le tampon
    readonly RDUniform _cSrc = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
    readonly RDUniform _cDst = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
    readonly Godot.Collections.Array<RDUniform> _copySet = new();
    // le flou : le tampon et la profondeur, tracés dans l'image du rendu
    readonly RDUniform _uColor = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
    readonly RDUniform _uDepth = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
    readonly RDUniform _uParams = new() { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 2 };
    readonly Godot.Collections.Array<RDUniform> _set = new();
    static readonly StringName Ctx = "naval_flou", Tmp = "tampon";
    static readonly StringName RB = "render_buffers", ColorName = "color", DepthName = "depth";

    // la cible du tracé, refaite quand l'image change (fenêtre retaillée) ;
    // le pipeline, refait quand son format change (anticrénelage basculé)
    Rid _fb, _fbColor, _pipeline;
    long _pipelineFormat = RenderingDevice.InvalidFormatId;
    readonly Godot.Collections.Array<Rid> _fbTex = new();
    static readonly Color[] NoClear = Array.Empty<Color>();

    Projection _prevVP;
    bool _hasPrev;

    /* LA CONVENTION, VÉRIFIÉE À CHAQUE LANCEMENT. Godot passe à l'effet la projection
       DÉJÀ CORRIGÉE pour Vulkan — constaté : y à −1,92 là où la caméra a 1,92, et
       la profondeur inversée (5,5e−6 et 0,05 au lieu de −1,00001 et −0,1). La
       première écriture la corrigeait une seconde fois. On s'en sert donc telle
       quelle, et l'effet vérifie qu'elle vaut bien Correction × celle de Camera3D,
       posée par la démo sur le fil principal : si Godot changeait de convention, le
       flou partirait de travers et on le saurait. */
    public Projection? MainProjection;
    bool _checked;

    public MotionBlurEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        /* Avec l'anticrénelage MSAA, l'image et la profondeur ne sont RÉSOLUES
           qu'à la demande : sans ces deux-là, l'effet lisait des tampons pas encore
           résolus. */
        AccessResolvedColor = true;
        AccessResolvedDepth = true;
        _copySet.Add(_cSrc); _copySet.Add(_cDst);
        _set.Add(_uColor); _set.Add(_uDepth); _set.Add(_uParams);
        RenderingServer.CallOnRenderThread(Callable.From(Init));
    }

    void Init()
    {
        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null) return;
        _copyShader = _rd.ShaderCreateFromSpirV(GD.Load<RDShaderFile>("res://shaders/motion_copy.glsl").GetSpirV());
        _copyPipeline = _rd.ComputePipelineCreate(_copyShader);
        _shader = _rd.ShaderCreateFromSpirV(GD.Load<RDShaderFile>("res://shaders/motion_blur.glsl").GetSpirV());
        _linear = _rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear, MagFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge
        });
        _nearest = _rd.SamplerCreate(new RDSamplerState
        {
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge
        });
        _ubo = _rd.UniformBufferCreate((uint)_params.Length);
    }

    public override void _Notification(int what)
    {
        // les pipelines, ensembles et la cible dépendent des shaders : ils partent avec eux
        if (what != NotificationPredelete || _rd == null) return;
        foreach (Rid r in new[] { _shader, _copyShader, _linear, _nearest, _ubo })
            if (r.IsValid) _rd.FreeRid(r);
    }

    /* LA CORRECTION DE VULKAN, que Godot pose sur la projection de la caméra : y
       retourné (l'image a son origine en haut), profondeur ramenée à 0..1 ET
       INVERSÉE (1 au plan proche, 0 au lointain). */
    static readonly Projection Correction = new(
        new Vector4(1, 0, 0, 0),
        new Vector4(0, -1, 0, 0),
        new Vector4(0, 0, -0.5f, 0),
        new Vector4(0, 0, 0.5f, 1));

    static void Put(byte[] b, int at, Projection m)
    {
        // std140 : quatre colonnes de vec4, écrites sans tableau intermédiaire
        Col(b, at, m.X); Col(b, at + 16, m.Y); Col(b, at + 32, m.Z); Col(b, at + 48, m.W);
    }

    static void Col(byte[] b, int at, Vector4 c)
    {
        BitConverter.TryWriteBytes(b.AsSpan(at), c.X);
        BitConverter.TryWriteBytes(b.AsSpan(at + 4), c.Y);
        BitConverter.TryWriteBytes(b.AsSpan(at + 8), c.Z);
        BitConverter.TryWriteBytes(b.AsSpan(at + 12), c.W);
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (_rd == null || !_copyPipeline.IsValid || !_shader.IsValid) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD rb) return;
        var sd = renderData.GetRenderSceneData();
        Vector2I size = rb.GetInternalSize();
        if (size.X == 0 || size.Y == 0) return;

        Projection raw = sd.GetCamProjection();
        if (!_checked && MainProjection is Projection mp)
        {
            _checked = true;
            float e = 0;
            Projection want = Correction * mp;
            for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) e = Math.Max(e, Math.Abs(raw[c][r] - want[c][r]));
            GD.Print($"flou de mouvement : projection reçue {(e < 1e-4f ? "= correction Vulkan × caméra, comme attendu" : $"INATTENDUE (écart {e:E1}) — convention à revoir")}");
        }
        Projection P = raw;                  // déjà corrigée : voir plus haut
        Transform3D cam = sd.GetCamTransform();
        Projection VP = P * new Projection(cam.AffineInverse());
        if (!_hasPrev) { _prevVP = VP; _hasPrev = true; return; }

        Put(_params, 0, P.Inverse());
        Put(_params, 64, _prevVP * new Projection(cam));
        BitConverter.TryWriteBytes(_params.AsSpan(128), Shutter);
        BitConverter.TryWriteBytes(_params.AsSpan(132), (float)Samples);
        BitConverter.TryWriteBytes(_params.AsSpan(136), MaxLength);
        _rd.BufferUpdate(_ubo, 0, (uint)_params.Length, _params);
        _prevVP = VP;

        if (!rb.HasTexture(Ctx, Tmp))
            rb.CreateTexture(Ctx, Tmp, RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                (uint)(RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit),
                RenderingDevice.TextureSamples.Samples1, size, 1, 1, true);

        /* DEUX PASSES, PARCE QUE GODOT N'EN PERMET PAS UNE. L'image du rendu ne
           peut être ni copiée (sans anticrénelage), ni écrite par un calcul (avec) ;
           elle peut toujours être LUE et être la CIBLE D'UN TRACÉ. Un calcul la
           recopie donc dans le tampon, et un triangle plein écran y trace le flou
           en lisant le tampon. */
        Rid color = rb.GetTexture(RB, ColorName), depth = rb.GetTexture(RB, DepthName), tmp = rb.GetTexture(Ctx, Tmp);

        _cSrc.ClearIds(); _cSrc.AddId(_nearest); _cSrc.AddId(color);
        _cDst.ClearIds(); _cDst.AddId(tmp);
        long cl = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(cl, _copyPipeline);
        _rd.ComputeListBindUniformSet(cl, UniformSetCacheRD.GetCache(_copyShader, 0, _copySet), 0);
        _rd.ComputeListDispatch(cl, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        _rd.ComputeListEnd();

        if (color != _fbColor || !_rd.FramebufferIsValid(_fb))
        {
            _fbTex.Clear(); _fbTex.Add(color);
            _fb = _rd.FramebufferCreate(_fbTex);
            _fbColor = color;
        }
        long fmt = _rd.FramebufferGetFormat(_fb);
        if (fmt != _pipelineFormat || !_pipeline.IsValid)
        {
            if (_pipeline.IsValid) _rd.FreeRid(_pipeline);
            var blend = new RDPipelineColorBlendState();
            blend.Attachments.Add(new RDPipelineColorBlendStateAttachment());
            _pipeline = _rd.RenderPipelineCreate(_shader, fmt, RenderingDevice.InvalidFormatId,
                RenderingDevice.RenderPrimitive.Triangles, new RDPipelineRasterizationState(),
                new RDPipelineMultisampleState(), new RDPipelineDepthStencilState(), blend);
            _pipelineFormat = fmt;
        }

        _uColor.ClearIds(); _uColor.AddId(_linear); _uColor.AddId(tmp);
        _uDepth.ClearIds(); _uDepth.AddId(_nearest); _uDepth.AddId(depth);
        _uParams.ClearIds(); _uParams.AddId(_ubo);
        long dl = _rd.DrawListBegin(_fb, RenderingDevice.DrawFlags.DefaultAll, NoClear);
        _rd.DrawListBindRenderPipeline(dl, _pipeline);
        _rd.DrawListBindUniformSet(dl, UniformSetCacheRD.GetCache(_shader, 0, _set), 0);
        _rd.DrawListDraw(dl, false, 1, 3);
        _rd.DrawListEnd();
    }
}
