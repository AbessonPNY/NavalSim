using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LES COUPS DE FOUDRE — lightning.js : l'éclair, du nuage à une tête de mât.
/// Trois bandes en réserve (un coup ne dure qu'un quart de seconde, deux à la fois
/// sont déjà rares), chacune avec son matériau et SA LAMPE ; le dessin est dans
/// lightning_bolt.gdshader.
///
/// L'éclat du ciel (<see cref="SkyNode.Strike"/>) reste : il monte l'hémisphérique
/// et court sur la mer, les voiles et le gréement, ce qui est le coup vu de loin.
/// Mais un coup AU-DESSUS DE LA TÊTE vient d'un endroit, et une lumière qui vient
/// de partout ne peut pas le rendre : il faut qu'un bord s'allume, que l'autre
/// reste noir et que l'ombre des mâts se couche en travers du pont. D'où une
/// lampe par bande, posée sur le trait du coup, qui suit EXACTEMENT sa courbe
/// d'éclat — une définition, deux usagers.
///
/// Les trois lampes sont créées à l'armement et ne sont jamais ni ajoutées ni
/// retirées ; seules leur énergie et leur visibilité changent, comme celles des
/// canons. Leur visibilité, parce qu'une lampe à ombres portées rend sa carte
/// cubique à chaque image tant qu'on la voit, et qu'un coup ne dure qu'un quart
/// de seconde sur des minutes de calme.
/// </summary>
public partial class LightningNode : Node3D
{
    const float Height = 300;                  // mètres du nuage à la tête de mât
    const float W = 512, H = 1024;             // le dessin de la page

    sealed class Bolt
    {
        public MeshInstance3D Mesh = null!;
        public ShaderMaterial Mat = null!;
        public OmniLight3D Lamp = null!;
        public Vector3 Top, Bottom;
        public double Age = -1;                // négatif : libre
    }

    /// <summary>Ce que le coup jette sur le pont — settings.json → storm.lightning.</summary>
    public double FlashEnergy = 90, FlashRange = 170;
    public bool FlashShadow = true;

    /* OÙ SE TIENT LA LAMPE SUR LE TRAIT, en mètres au-dessus de la tête de mât
       frappée. Au point d'impact même, elle éclairerait la pomme du mât et rien
       d'autre — un mât de quarante mètres ferait écran à son propre pont. Trente
       mètres plus haut, le pont entier est dans son cône et les mâts couchent
       leur ombre dessus, ce qui est ce qu'on veut voir. Le coup, lui, fait trois
       cents mètres : la lampe n'est pas l'éclair, elle en est la part utile. */
    const float LampUp = 30;

    readonly Bolt[] _bolts = new Bolt[3];
    readonly Random _rng = new();
    readonly Vector2[] _pts = new Vector2[48], _fork = new Vector2[8];

    static readonly StringName UTop = "u_top", UBottom = "u_bottom", UOpacity = "u_opacity",
        UPts = "u_pts", UN = "u_n", UFork = "u_fork", UWidth = "u_width";

    public override void _Ready()
    {
        var shader = GD.Load<Shader>("res://shaders/lightning_bolt.gdshader");
        var quad = new QuadMesh { Size = Vector2.One };
        var big = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f));
        for (int i = 0; i < _bolts.Length; i++)
        {
            var mat = new ShaderMaterial { Shader = shader };
            mat.SetShaderParameter(UWidth, Height * W / H);
            var mi = new MeshInstance3D
            {
                Mesh = quad, MaterialOverride = mat, CustomAabb = big, Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(mi);
            /* BLANC-BLEU ET FROID : un arc est un plasma à trente mille degrés, et
               tout ce qu'il éclaire prend cette teinte-là. C'est ce qui le distingue
               d'un fanal, qui est une flamme, et c'est pourquoi un pont aux feux
               couverts paraît soudain gris acier sous le coup. */
            var lamp = new OmniLight3D
            {
                LightColor = new Color(0.82f, 0.88f, 1f),
                LightEnergy = 0,
                OmniRange = (float)FlashRange,
                OmniAttenuation = 1.0f,
                ShadowEnabled = FlashShadow,
                Visible = false
            };
            AddChild(lamp);
            _bolts[i] = new Bolt { Mesh = mi, Mat = mat, Lamp = lamp };
        }
    }

    double R() => _rng.NextDouble();

    /// <summary>Un coup, du nuage jusqu'à <paramref name="top"/> (mètres locaux).</summary>
    public void Strike(Vector3 top)
    {
        Bolt? b = null;
        foreach (var x in _bolts) if (x.Age < 0) { b = x; break; }
        b ??= _bolts[0];
        Paint(b.Mat);
        // un peu hors de la verticale, comme un coup qui sort d'un nuage en marche
        double a = R() * Math.PI * 2, off = Height * 0.12 * R();
        b.Bottom = top;
        b.Top = new Vector3(top.X + (float)(Math.Cos(a) * off), top.Y + Height, top.Z + (float)(Math.Sin(a) * off));
        b.Mat.SetShaderParameter(UTop, b.Top);
        b.Mat.SetShaderParameter(UBottom, b.Bottom);
        b.Age = 0;
        b.Mesh.Visible = true;
        /* SUR LE TRAIT DU COUP, pas à côté : l'ombre d'un mât doit tomber du même
           côté que le trait qu'on voit, sans quoi l'œil sent la triche sans
           pouvoir la nommer. */
        b.Lamp.Position = b.Bottom + (b.Top - b.Bottom).Normalized() * LampUp;
        b.Lamp.OmniRange = (float)FlashRange;
        b.Lamp.ShadowEnabled = FlashShadow;
        b.Lamp.Visible = FlashEnergy > 0;
    }

    /* LE COUP TEL QU'ON L'ESQUISSERAIT — paint() de la page : d'en haut dans le
       nuage, en larges bonds obliques qui finissent chacun par un petit crochet, le
       dernier ramené au bas, au centre, là où est la tête de mât. */
    void Paint(ShaderMaterial m)
    {
        int n = 0;
        double x = W * (0.45 + R() * 0.35), y = 0;
        void Add(double px, double py) { if (n < _pts.Length) _pts[n++] = new Vector2((float)px, (float)py); }
        Add(x, y);
        int dir = R() < 0.5 ? -1 : 1;
        while (y < H * 0.84 && n < _pts.Length - 5)
        {
            // un long bond oblique…
            double nx = Math.Max(40, Math.Min(W - 40, x + dir * (90 + R() * 170)));
            y += 30 + R() * 50;
            Add(nx, y);
            // …un court crochet en arrière et vers le bas…
            y += 25 + R() * 45;
            x = nx - dir * (15 + R() * 35);
            Add(x, y);
            // …une secousse de l'autre côté
            y += 10 + R() * 25;
            x += dir * (10 + R() * 20);
            Add(x, y);
            dir = -dir;
        }
        Add(W / 2 + (R() - 0.5) * 30, H * 0.94);
        Add(W / 2, H);

        for (int k = 0; k < 2; k++)
        {
            int i = 1 + (int)Math.Floor(R() * Math.Max(1, n - 4));
            double fx = _pts[i].X, fy = _pts[i].Y;
            int fd = R() < 0.5 ? -1 : 1;
            _fork[k * 4] = _pts[i];
            for (int j = 1; j < 4; j++)
            {
                fx = Math.Max(10, Math.Min(W - 10, fx + fd * (30 + R() * 60)));
                fy += 25 + R() * 40;
                _fork[k * 4 + j] = new Vector2((float)fx, (float)fy);
                fd = -fd;
            }
        }
        m.SetNow(UPts, _pts);
        m.SetShaderParameter(UN, n);
        m.SetNow(UFork, _fork);
    }

    /// <summary>Une image : chaque coup vit un quart de seconde, sur le rythme du ciel.</summary>
    public void Step(double dt)
    {
        foreach (var b in _bolts)
        {
            if (b.Age < 0) continue;
            b.Age += dt;
            if (b.Age > 0.25)
            {
                b.Age = -1; b.Mesh.Visible = false;
                b.Lamp.LightEnergy = 0; b.Lamp.Visible = false;
                continue;
            }
            // la forme du coup du ciel : un premier éclat, un creux, un arc en retour, fini
            double a = b.Age;
            double op = a < 0.06 ? 1 : a < 0.10 ? 0.25 : a < 0.16 ? 0.9 : Math.Max(0, 0.5 * (1 - (a - 0.16) / 0.09));
            b.Mat.SetShaderParameter(UOpacity, (float)op);
            // la MÊME courbe : le trait et ce qu'il éclaire sont le même événement
            b.Lamp.LightEnergy = (float)(op * FlashEnergy);
        }
    }

    public void Rebase(double dx, double dz)
    {
        var d = new Vector3((float)dx, 0, (float)dz);
        foreach (var b in _bolts)
        {
            if (b.Age < 0) continue;
            b.Top -= d; b.Bottom -= d;
            b.Lamp.Position -= d;
            b.Mat.SetShaderParameter(UTop, b.Top);
            b.Mat.SetShaderParameter(UBottom, b.Bottom);
        }
    }
}
