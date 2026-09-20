using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES BULLES QU'ON VOIT MONTER — ce qui manquait au naufrage : le solveur
/// comptait l'air, <see cref="WreckAir"/> le faisait arriver en haut, mais entre
/// les deux il montait en silence et rien ne le montrait. Sous l'eau, c'est
/// pourtant tout ce qu'on voit d'une coque qui descend.
///
/// Une poche part du point de fuite et monte à la vitesse que le noyau lui a
/// donnée (Davies et Taylor), donc elle arrive en haut À L'INSTANT où la gerbe
/// et le bouillon paraissent : ce sont les deux bouts du même trajet. Elle
/// n'est pas une bulle unique mais une GRAPPE — une poche d'air qui traverse
/// vingt mètres d'eau se déchire en chapelet —, chacune tremblant un peu autour
/// de sa route.
///
/// Un seul maillage multiplié (MultiMesh), sans lumière : des panneaux face à
/// l'œil, à peine visibles de face et vifs sur leur bord, comme une bulle.
/// </summary>
public partial class BubbleNode : Node3D
{
    const int Max = 900;

    struct Bubble
    {
        public Vector3 From;      // son départ, en repère local
        public float Rise, Life, Age, R, Seed;
        public float Depth0;      // la profondeur d'où elle part : c'est elle qui dit de combien elle enflera
        public bool On;
    }

    readonly Bubble[] _b = new Bubble[Max];
    int _next;
    MultiMesh _mm = null!;
    readonly RandomNumberGenerator _rng = new();
    public ShaderMaterial Material { get; private set; } = null!;

    public override void _Ready()
    {
        Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/bubbles.gdshader") };
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = Max,
            VisibleInstanceCount = 0
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _mm, MaterialOverride = Material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 200
        });
    }

    /// <summary>
    /// Une poche d'air part : <paramref name="at"/> le point de fuite (local),
    /// <paramref name="volume"/> en m³, <paramref name="rise"/> sa vitesse de
    /// montée, <paramref name="travel"/> le temps qu'elle mettra à arriver.
    /// </summary>
    public void Slug(Vector3 at, double volume, double rise, double travel)
    {
        // une grosse poche se déchire en plus de bulles, jamais moins de trois
        int n = Math.Clamp(3 + (int)(volume * 6), 3, 14);
        for (int i = 0; i < n; i++)
        {
            ref Bubble b = ref _b[_next];
            _next = (_next + 1) % Max;
            b.On = true;
            b.Age = (float)(-0.35 * _rng.Randf() * travel);      // le chapelet s'étire en partant
            b.Life = (float)travel + 0.4f;
            b.Rise = (float)rise * (0.75f + 0.5f * _rng.Randf()); // les petites traînent, les grosses filent
            /* PETITES AU DÉPART, ET QUI GROSSISSENT. Une bulle garde sa masse
               d'air : en montant, la pression tombe et son volume enfle d'autant
               (Boyle) — de dix mètres de fond à la surface, deux fois le volume,
               un quart de rayon en plus. C'est le contraire d'une bulle dessinée
               à taille fixe, et c'est ce qu'on voit d'une épave. */
            b.R = (float)Math.Cbrt(volume / n) * (0.17f + 0.22f * _rng.Randf());
            b.Depth0 = (float)Math.Max(0.5, rise * travel);
            b.Seed = _rng.Randf() * 100;
            b.From = at + new Vector3((_rng.Randf() - 0.5f) * 1.2f, 0, (_rng.Randf() - 0.5f) * 1.2f);
        }
    }

    /// <summary>Une image : elles montent, tremblent, et s'effacent en crevant.</summary>
    public void Step(double dt, Ocean sea, double t)
    {
        int live = 0;
        for (int i = 0; i < Max; i++)
        {
            ref Bubble b = ref _b[i];
            if (!b.On) continue;
            b.Age += (float)dt;
            if (b.Age > b.Life) { b.On = false; continue; }
            if (b.Age < 0) continue;
            /* Sa route : droit vers le haut, avec le tremblement d'une bulle qui
               roule sur elle-même — une bulle ne monte jamais droit. */
            float y = b.From.Y + b.Rise * b.Age;
            float wob = 0.25f + 2.5f * b.R;
            var p = new Vector3(
                b.From.X + Mathf.Sin(b.Age * 2.7f + b.Seed) * wob,
                y,
                b.From.Z + Mathf.Cos(b.Age * 2.1f + b.Seed * 1.7f) * wob);
            // arrivée en haut : elle crève, et c'est la gerbe du noyau qui prend le relais
            double surf = sea.Sample(p.X, p.Z, t);
            if (p.Y > surf - 0.05) { b.On = false; continue; }

            /* Ce qu'elle enfle : la pression vaut une atmosphère plus une par dix
               mètres d'eau, le volume lui est inversement proportionnel, donc le
               rayon suit la racine cubique du rapport. Et un peu au-delà : les
               petites se rejoignent en montant, ce qu'on ne simule pas. */
            float depth = Mathf.Max(0.2f, (float)surf - p.Y);
            float grow = Mathf.Pow((10f + b.Depth0) / (10f + depth), 1f / 3f)
                       * (1f + 0.6f * Mathf.Clamp(1f - depth / Mathf.Max(1f, b.Depth0), 0, 1));
            _mm.SetInstanceTransform(live, new Transform3D(Basis.Identity.Scaled(Vector3.One * b.R * grow * 2.0f), p));
            _mm.SetInstanceCustomData(live, new Color(b.Seed, b.R, 0, 1));
            live++;
            if (live >= Max) break;
        }
        _mm.VisibleInstanceCount = live;
    }

    /// <summary>L'origine flottante.</summary>
    public void Rebase(double dx, double dz)
    {
        for (int i = 0; i < Max; i++)
            if (_b[i].On) _b[i].From += new Vector3((float)dx, 0, (float)dz);
    }
}
