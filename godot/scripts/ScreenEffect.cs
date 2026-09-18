using Godot;
using System;

namespace NavalSim;

/// <summary>
/// UNE PASSE PLEIN ÉCRAN APRÈS LE RENDU — le socle commun du flou de mouvement et
/// du flou anamorphique. Inséré après les matières transparentes, pour que la mer
/// (transparente depuis qu'elle montre la coque sous l'eau) soit dedans.
///
/// DEUX PASSES, PARCE QUE GODOT N'EN PERMET PAS UNE. L'image du rendu ne peut être
/// ni copiée (sans anticrénelage), ni écrite par un calcul (avec) ; elle peut
/// toujours être LUE et être la CIBLE D'UN TRACÉ. Un calcul la recopie donc dans
/// un tampon (screen_copy.glsl) — la couleur en rgb, la profondeur de VUE en
/// alpha —, puis un triangle plein écran y trace l'effet en lisant le tampon. Le
/// shader de l'effet reçoit, sur l'ensemble 0 : le tampon (0, filtré), la
/// profondeur brute (1, au plus proche) et ses paramètres (2, std140), que la
/// classe dérivée remplit et qui COMMENCENT par l'inverse de la projection — la
/// recopie s'en sert. L'alpha de l'image n'est pas rendu : l'effet écrit 1.
///
/// Tourne sur le fil de RENDU ; rien n'y est créé à chaque image — les objets de
/// liaison sont gardés et remplis —, pour ne rien laisser au ramasse-miettes.
/// </summary>
public abstract partial class ScreenEffect : CompositorEffect
{
    protected RenderingDevice? Rd;
    Rid _copyShader, _copyPipeline, _shader, _linear, _nearest, _ubo;
    protected readonly byte[] Params;

    readonly RDUniform _cSrc = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
    readonly RDUniform _cDst = new() { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
    readonly RDUniform _cDepth = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 2 };
    readonly RDUniform _cParams = new() { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 3 };
    readonly Godot.Collections.Array<RDUniform> _copySet = new();
    readonly RDUniform _uColor = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
    readonly RDUniform _uDepth = new() { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 };
    readonly RDUniform _uParams = new() { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 2 };
    readonly Godot.Collections.Array<RDUniform> _set = new();
    readonly StringName _ctx, _tmp = "tampon";
    static readonly StringName RB = "render_buffers", ColorName = "color", DepthName = "depth";

    // la cible du tracé, refaite quand l'image change (fenêtre retaillée) ;
    // le pipeline, refait quand son format change (anticrénelage basculé)
    Rid _fb, _fbColor, _pipeline;
    long _pipelineFormat = RenderingDevice.InvalidFormatId;
    readonly Godot.Collections.Array<Rid> _fbTex = new();
    static readonly Color[] NoClear = Array.Empty<Color>();
    readonly string _shaderPath;

    /// <param name="shaderPath">le shader de l'effet, #[vertex] + #[fragment]</param>
    /// <param name="context">le nom de son tampon parmi ceux du rendu : un par effet</param>
    /// <param name="paramBytes">la taille de son bloc de paramètres std140</param>
    protected ScreenEffect(string shaderPath, string context, int paramBytes)
    {
        _shaderPath = shaderPath;
        _ctx = context;
        Params = new byte[paramBytes];
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        /* Avec l'anticrénelage MSAA, l'image et la profondeur ne sont RÉSOLUES
           qu'à la demande : sans ces deux-là, l'effet lisait des tampons pas encore
           résolus. */
        AccessResolvedColor = true;
        AccessResolvedDepth = true;
        _copySet.Add(_cSrc); _copySet.Add(_cDst); _copySet.Add(_cDepth); _copySet.Add(_cParams);
        _set.Add(_uColor); _set.Add(_uDepth); _set.Add(_uParams);
        RenderingServer.CallOnRenderThread(Callable.From(Init));
    }

    void Init()
    {
        Rd = RenderingServer.GetRenderingDevice();
        if (Rd == null) return;
        _copyShader = Rd.ShaderCreateFromSpirV(GD.Load<RDShaderFile>("res://shaders/screen_copy.glsl").GetSpirV());
        _copyPipeline = Rd.ComputePipelineCreate(_copyShader);
        _shader = Rd.ShaderCreateFromSpirV(GD.Load<RDShaderFile>(_shaderPath).GetSpirV());
        _linear = Rd.SamplerCreate(new RDSamplerState
        {
            MinFilter = RenderingDevice.SamplerFilter.Linear, MagFilter = RenderingDevice.SamplerFilter.Linear,
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge
        });
        _nearest = Rd.SamplerCreate(new RDSamplerState
        {
            RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge
        });
        _ubo = Rd.UniformBufferCreate((uint)Params.Length);
    }

    /* LIBÉRER AVANT QUE LE PÉRIPHÉRIQUE NE S'ÉTEIGNE. Une ressource Godot meurt
       quand plus rien ne la tient, et l'effet est tenu par le compositeur de la
       caméra jusqu'après l'arrêt du rendu : la démo l'appelle donc en sortant. */
    public void Release() => RenderingServer.CallOnRenderThread(Callable.From(FreeAll));

    void FreeAll()
    {
        if (Rd == null) return;
        // les dépendants d'abord : pipelines avant shaders
        foreach (Rid r in new[] { _pipeline, _copyPipeline, _ubo, _linear, _nearest, _shader, _copyShader })
            if (r.IsValid) Rd.FreeRid(r);
        _pipeline = _copyPipeline = _ubo = _linear = _nearest = _shader = _copyShader = default;
    }

    /// <summary>
    /// Remplir <see cref="Params"/> pour cette image. Faux : rien à tracer cette
    /// fois (la première image du flou de mouvement, qui n'a pas d'image d'avant).
    /// </summary>
    protected abstract bool Prepare(RenderSceneData scene, Vector2I size);

    // std140 : quatre colonnes de vec4, écrites sans tableau intermédiaire
    protected void Put(int at, Projection m) { Col(at, m.X); Col(at + 16, m.Y); Col(at + 32, m.Z); Col(at + 48, m.W); }

    protected void Col(int at, Vector4 c)
    {
        BitConverter.TryWriteBytes(Params.AsSpan(at), c.X);
        BitConverter.TryWriteBytes(Params.AsSpan(at + 4), c.Y);
        BitConverter.TryWriteBytes(Params.AsSpan(at + 8), c.Z);
        BitConverter.TryWriteBytes(Params.AsSpan(at + 12), c.W);
    }

    protected void Float(int at, float v) => BitConverter.TryWriteBytes(Params.AsSpan(at), v);

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (Rd == null || !_copyPipeline.IsValid || !_shader.IsValid) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD rb) return;
        Vector2I size = rb.GetInternalSize();
        if (size.X == 0 || size.Y == 0) return;
        if (!Prepare(renderData.GetRenderSceneData(), size)) return;
        Rd.BufferUpdate(_ubo, 0, (uint)Params.Length, Params);

        if (!rb.HasTexture(_ctx, _tmp))
            rb.CreateTexture(_ctx, _tmp, RenderingDevice.DataFormat.R16G16B16A16Sfloat,
                (uint)(RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit),
                RenderingDevice.TextureSamples.Samples1, size, 1, 1, true);
        Rid color = rb.GetTexture(RB, ColorName), depth = rb.GetTexture(RB, DepthName), tmp = rb.GetTexture(_ctx, _tmp);

        _cSrc.ClearIds(); _cSrc.AddId(_nearest); _cSrc.AddId(color);
        _cDst.ClearIds(); _cDst.AddId(tmp);
        _cDepth.ClearIds(); _cDepth.AddId(_nearest); _cDepth.AddId(depth);
        _cParams.ClearIds(); _cParams.AddId(_ubo);
        long cl = Rd.ComputeListBegin();
        Rd.ComputeListBindComputePipeline(cl, _copyPipeline);
        Rd.ComputeListBindUniformSet(cl, UniformSetCacheRD.GetCache(_copyShader, 0, _copySet), 0);
        Rd.ComputeListDispatch(cl, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
        Rd.ComputeListEnd();

        if (color != _fbColor || !Rd.FramebufferIsValid(_fb))
        {
            _fbTex.Clear(); _fbTex.Add(color);
            _fb = Rd.FramebufferCreate(_fbTex);
            _fbColor = color;
        }
        long fmt = Rd.FramebufferGetFormat(_fb);
        if (fmt != _pipelineFormat || !_pipeline.IsValid)
        {
            if (_pipeline.IsValid) Rd.FreeRid(_pipeline);
            var blend = new RDPipelineColorBlendState();
            blend.Attachments.Add(new RDPipelineColorBlendStateAttachment());
            _pipeline = Rd.RenderPipelineCreate(_shader, fmt, RenderingDevice.InvalidFormatId,
                RenderingDevice.RenderPrimitive.Triangles, new RDPipelineRasterizationState(),
                new RDPipelineMultisampleState(), new RDPipelineDepthStencilState(), blend);
            _pipelineFormat = fmt;
        }

        _uColor.ClearIds(); _uColor.AddId(_linear); _uColor.AddId(tmp);
        _uDepth.ClearIds(); _uDepth.AddId(_nearest); _uDepth.AddId(depth);
        _uParams.ClearIds(); _uParams.AddId(_ubo);
        long dl = Rd.DrawListBegin(_fb, RenderingDevice.DrawFlags.DefaultAll, NoClear);
        Rd.DrawListBindRenderPipeline(dl, _pipeline);
        Rd.DrawListBindUniformSet(dl, UniformSetCacheRD.GetCache(_shader, 0, _set), 0);
        Rd.DrawListDraw(dl, false, 1, 3);
        Rd.DrawListEnd();
    }
}
