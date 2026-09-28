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
    /* DE QUOI FAIRE UN NUAGE. Sept bulles par gorgée faisaient un chapelet, et un
       chapelet se compte du regard — c'est précisément ce qui le trahissait. Une
       poche qui traverse vingt mètres d'eau se déchire en dizaines de bulles, et
       il en faut donc des milliers à flot. Un panneau non éclairé dans un seul
       maillage multiplié ne coûte presque rien ; le plafond est là pour borner la
       mémoire, pas pour épargner la carte. */
    const int Max = 2600;

    struct Bubble
    {
        public Vector3 From;      // son départ, en repère local
        public float Rise, Life, Age, R, Seed;
        public float Depth0;      // la profondeur d'où elle part : c'est elle qui dit de combien elle enflera
        public float Wob, Freq;   // de combien elle titube, et à quelle cadence
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
    public void Slug(Vector3 at, double volume, double rise, double travel, double spread)
    {
        /* COMBIEN ELLE SE DÉCHIRE EN MORCEAUX. Avec le volume, parce qu'une grosse
           poche fait plus de débris ; et avec le TRAJET, parce qu'une poche qui
           traverse vingt mètres d'eau a vingt mètres pour se déchirer, quand celle
           qui part d'un mètre de fond arrive presque entière. */
        int n = Math.Clamp(8 + (int)(volume * 14 + travel * 6), 8, 46);
        double rBig = Math.Cbrt(3 * volume / (4 * Math.PI));
        double depth = Math.Max(0.5, rise * travel);

        for (int i = 0; i < n; i++)
        {
            ref Bubble b = ref _b[_next];
            _next = (_next + 1) % Max;
            b.On = true;

            /* UN SPECTRE, ET NON UNE TAILLE. Ce qui manquait le plus : toutes les
               bulles d'une gorgée avaient à peu près le même calibre, ce qui ne
               ressemble à rien — une poche qui se déchire donne quelques gros
               morceaux et une poussière de fines. La puissance 2,6 met l'essentiel
               du tirage en bas de la gamme et laisse passer de loin en loin une
               grosse, qui est celle qu'on suit du regard. */
            float u = _rng.Randf();
            b.R = (float)(rBig * (0.04 + 0.46 * Math.Pow(u, 2.6)));

            /* ET SA VITESSE VIENT DE SON RAYON, par la loi du noyau. C'est le
               changement qui fait tout : les grosses filent à un mètre et demi par
               seconde, les fines traînent à un quart, si bien que le panache SE
               TRIE en montant au lieu de défiler d'un bloc. Elles n'arrivent plus
               ensemble — ce qui est exactement ce qu'on voit d'une épave. */
            b.Rise = (float)(WreckAir.RiseSpeed(b.R) * (0.9 + 0.2 * _rng.Randf()));

            /* CE QU'ELLE TITUBE. Une bulle ne monte pas droit : elle zigzague, et
               d'autant plus lentement qu'elle est grosse — une fine frétille, une
               grosse calotte se balance. L'amplitude suit sa taille, la cadence
               fait l'inverse, et c'est ce désaccord qui empêche le panache entier
               de battre à l'unisson comme il le faisait. */
            b.Wob = (float)(0.10 + 3.2 * b.R) * (0.6f + 0.8f * _rng.Randf());
            b.Freq = (float)(3.4 / (0.22 + 5.0 * b.R)) * (0.8f + 0.4f * _rng.Randf());

            b.Age = (float)(-0.5 * _rng.Randf() * Math.Max(0.35, travel));
            b.Depth0 = (float)depth;
            /* CHACUNE A SA PROPRE DURÉE, tirée de SA vitesse et non de celle de la
               poche : c'est le corollaire du tri, et sans lui les fines
               disparaîtraient en pleine eau pendant que les grosses seraient
               encore en route. */
            b.Life = (float)(depth / b.Rise) + 0.5f;
            b.Seed = _rng.Randf() * 100;

            /* ET LE PANACHE A LA LARGEUR QUE LE NOYAU LUI DONNE, laquelle enfle
               avec la profondeur. Elles sortaient d'une boîte d'un mètre et demi
               quelle que soit la profondeur d'où elles venaient : le dessin
               contredisait le modèle, qui sait depuis toujours qu'un panache
               s'élargit en montant. Tiré en racine pour couvrir le disque
               uniformément, sans quoi tout se masse au centre. */
            double ang = _rng.Randf() * Math.PI * 2, rad = spread * Math.Sqrt(_rng.Randf());
            b.From = at + new Vector3((float)(Math.Cos(ang) * rad),
                                      -0.6f - 1.2f * _rng.Randf(),
                                      (float)(Math.Sin(ang) * rad));
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
            /* SA ROUTE. Droit vers le haut, et un zigzag autour — mais un zigzag
               et non une hélice : deux sinusoïdes en quadrature parfaite faisaient
               tourner chaque bulle sur un cercle régulier, ce qui se lit comme un
               ressort. Deux cadences qui ne tombent pas juste l'une sur l'autre
               (1 et 1,7) donnent une route qui ne se referme jamais sur elle-même,
               et c'est cela qu'on reconnaît. */
            float y = b.From.Y + b.Rise * b.Age;
            float ph = b.Age * b.Freq + b.Seed;
            var p = new Vector3(
                b.From.X + (Mathf.Sin(ph) + 0.45f * Mathf.Sin(ph * 1.7f + b.Seed)) * b.Wob,
                y,
                b.From.Z + (Mathf.Cos(ph * 1.13f + b.Seed) + 0.45f * Mathf.Sin(ph * 0.61f)) * b.Wob);
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
            // le rayon VRAI, celui du moment : c'est lui qui dit au shader si cette
            // bulle-là est une perle ou une calotte
            _mm.SetInstanceCustomData(live, new Color(b.Seed, b.R * grow, 0, 1));
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
