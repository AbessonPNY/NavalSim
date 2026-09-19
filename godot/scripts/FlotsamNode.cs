using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// CE QUI REMONTE D'UN NAVIRE, ET CE QU'ON EN FAIT — flotsam.js.
///
/// Un navire qui coule laisse quelque chose sur l'eau : une planche ou deux, un
/// tonneau, et de loin en loin une bouteille que quelqu'un a eu le temps de
/// jeter. Les planches et les tonneaux sont du décor — ils marquent l'endroit,
/// et un carré de mer avec un tonneau dessus se lit comme un endroit où il s'est
/// passé quelque chose. La bouteille est la seule chose qui vaille d'être
/// repêchée ; ce qu'elle contient regarde la page — ici on ne fait que flotter,
/// dériver, couler quand le temps est passé, et dire quand le navire est venu
/// assez près et assez lentement pour en prendre une à bord.
///
/// Tout est tenu en mètres MONDE VRAIS et posé contre l'origine à chaque image :
/// rien à décaler au recentrage.
///
/// La cargaison échouée de la page attend le portage du monde : elle se pose sur
/// le plateau d'une île, et il n'y a pas encore d'île.
/// </summary>
public partial class FlotsamNode : Node3D
{
    sealed class Item
    {
        public string Kind = "plank";
        public double X, Z, Y = -2, Yaw, Age, Life, Draft;
        public double Vy;                 // son erre verticale : il remonte avec de l'élan
        public bool Broke;                // a-t-il déjà crevé la surface
        public Node3D Node = null!;
        public ShaderMaterial? Halo;
        public MeshInstance3D? Mark;
        public double MarkFrom;
        public string? From;              // le navire d'où vient la bouteille
    }

    sealed class Kind
    {
        public double Scale = 1, Draft, Life = 900;
        public double PickupRadius = 10, PickupSpeed = 1.03;
        /// <summary>Sa poussée (rad/s²) et son amortissement : un tonneau se balance, une planche non.</summary>
        public double Push = 6, Damp = 1.6;
        public bool Halo = true;
        public double HaloRadius = 1.1, HaloIntensity = 1, Mark = 0.032, MarkFrom = 60;
        public Color HaloColor = new(0xa8 / 255f, 0xec / 255f, 1f);
    }

    readonly Dictionary<string, Kind> _def = new()
    {
        ["plank"] = new Kind { Draft = 0.02, Life = 900, Push = 9, Damp = 3.0 },
        ["barrel"] = new Kind { Draft = 0.12, Life = 900, Push = 6, Damp = 1.5 },
        ["bottle"] = new Kind { Scale = 3, Draft = 0.05, Life = 1800, Push = 8, Damp = 2.6 }
    };
    int _debrisMin = 1, _debrisMax = 2;
    /// <summary>Une bouteille sur combien de naufrages — settings.json → wreck.bottleOneIn.</summary>
    public int BottleOneIn = 6;

    readonly List<Item> _items = new();
    readonly HashSet<ShipPhysics> _seen = new();
    readonly RandomNumberGenerator _rng = new();
    double _clock;

    /// <summary>Une bouteille repêchée : le navire d'où elle vient.</summary>
    public Action<string?>? OnBottle;
    /// <summary>Un objet qui crève la surface en remontant : où, l'eau jetée, sa vitesse.</summary>
    public Action<Vec3d, double, double>? OnBreak;

    public override void _Ready()
    {
        ReadProps();
        _halo = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/bubble.gdshader") };
    }

    ShaderMaterial _halo = null!;
    readonly List<ShaderMaterial> _halos = new();
    public IReadOnlyList<ShaderMaterial> Hazed => _hazed;
    readonly List<ShaderMaterial> _hazed = new();

    /* props/Props.json : ce que chaque objet est. Un fichier absent ou à moitié
       écrit ne change rien — les valeurs de la page sont les mêmes ici. */
    void ReadProps()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "props", "Props.json"));
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("wreck", out var w))
            {
                if (w.TryGetProperty("debrisMin", out var a)) _debrisMin = a.GetInt32();
                if (w.TryGetProperty("debrisMax", out var b)) _debrisMax = b.GetInt32();
            }
            foreach (var (name, k) in _def)
            {
                if (!root.TryGetProperty(name, out var j)) continue;
                double D(string f, double v) => j.TryGetProperty(f, out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetDouble() : v;
                k.Scale = D("scale", k.Scale); k.Draft = D("draft", k.Draft); k.Life = D("life", k.Life);
                k.PickupRadius = D("pickupRadius", k.PickupRadius); k.PickupSpeed = D("pickupSpeed", k.PickupSpeed);
                if (j.TryGetProperty("halo", out var h))
                {
                    if (h.TryGetProperty("enabled", out var he)) k.Halo = he.ValueKind == System.Text.Json.JsonValueKind.True;
                    k.HaloRadius = h.TryGetProperty("radius", out var hr) ? hr.GetDouble() : k.HaloRadius;
                    k.HaloIntensity = h.TryGetProperty("intensity", out var hi) ? hi.GetDouble() : k.HaloIntensity;
                    k.Mark = h.TryGetProperty("mark", out var hm) ? hm.GetDouble() : k.Mark;
                    k.MarkFrom = h.TryGetProperty("markFrom", out var hf) ? hf.GetDouble() : k.MarkFrom;
                    if (h.TryGetProperty("color", out var hc) && hc.ValueKind == System.Text.Json.JsonValueKind.String)
                        k.HaloColor = Color.FromString(hc.GetString()!, k.HaloColor);
                }
            }
        }
        catch (Exception e) { GD.PushWarning($"props/Props.json illisible ({e.Message}) : les valeurs par défaut tiennent"); }
    }

    /* ------------------------------------------------------------------ */
    /*  CE QU'UNE COQUE LAISSE                                             */
    /* ------------------------------------------------------------------ */

    /// <summary>Une image : les naufrages neufs, la dérive, la prise, la fin.</summary>
    public void Step(double dt, Ocean sea, IReadOnlyList<ShipNode> fleet, ShipNode player, Camera3D cam)
    {
        if (dt <= 0) return;
        _clock += dt;
        _halo.SetShaderParameter("u_time", (float)_clock);

        foreach (var s in fleet)
            if (s.Physics.Foundered && _seen.Add(s.Physics))
                Wreck(s, sea, s == player);

        var o = sea.Origin;
        var pb = player.Physics.Body;
        double px = pb.Pos.X + o.X, pz = pb.Pos.Z + o.Z;
        double pv = Math.Sqrt(pb.Vel.X * pb.Vel.X + pb.Vel.Z * pb.Vel.Z);
        var wind = sea.WindVec;

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var it = _items[i];
            it.Age += dt;
            if (it.Age > it.Life) { Remove(it); continue; }
            /* Il dérive au vent, quelques centièmes de lui — le plus qu'une chose
               qui flotte en prenne jamais — et tourne lentement en allant. */
            it.X += wind.X * 0.025 * dt;
            it.Z += wind.Z * 0.025 * dt;
            it.Yaw += 0.02 * dt * Math.Sin(it.Age * 0.1 + it.X);

            double lx = it.X - o.X, lz = it.Z - o.Z;
            double t = sea.Time;
            double sea0 = sea.Sample(lx, lz, t);
            double sx = sea.Sample(lx + 0.8, lz, t) - sea.Sample(lx - 0.8, lz, t);
            double sz = sea.Sample(lx, lz + 0.8, t) - sea.Sample(lx, lz - 0.8, t);
            /* IL REMONTE AVEC DE L'ÉLAN. Il montait à vitesse fixe et se collait à
               la surface dès qu'il l'atteignait : un tonneau arraché à une cale
               noyée arrive au contraire lancé, CRÈVE la surface, retombe et se
               balance jusqu'à s'apaiser. Un ressort amorti le rend d'un trait : la
               poussée le rappelle à sa flottaison, l'amortissement mange l'élan —
               fort pour une planche, qui s'aplatit tout de suite, faible pour un
               tonneau, qui danse. Et le même ressort porte son enfoncement de fin
               de vie, puisque sa flottaison descend. */
            double sinking = Math.Max(0, (it.Age - (it.Life - 20)) / 20);
            double target = sea0 - it.Draft - sinking * 1.5;
            var K0 = _def[it.Kind];
            it.Vy += (target - it.Y) * K0.Push * dt;
            it.Vy *= 1 - Math.Min(1, K0.Damp * dt);
            it.Vy = Math.Clamp(it.Vy, -6, 6);
            it.Y += it.Vy * dt;
            // la gerbe qu'il jette en crevant la surface, une seule fois
            if (!it.Broke && it.Y > sea0 - it.Draft * 0.5)
            {
                it.Broke = true;
                if (it.Vy > 0.8) OnBreak?.Invoke(new Vec3d(lx, sea0, lz), Math.Min(1.2, 0.12 * it.Vy * it.Vy), 1.5 + it.Vy);
            }

            // posé sur la pente de la vague, et tourné sur lui-même
            var up = new Vector3((float)(-sx / 1.6), 1, (float)(-sz / 1.6)).Normalized();
            var q = new Quaternion(Vector3.Up, up) * new Quaternion(Vector3.Up, (float)it.Yaw);
            it.Node.Quaternion = q;
            it.Node.Position = new Vector3((float)lx, (float)it.Y, (float)lz);

            if (it.Mark != null)
            {
                // le repère de loin entre avec la distance, et s'efface avec la bouteille qui coule
                double d = cam.GlobalPosition.DistanceTo(it.Node.GlobalPosition);
                double far = Math.Min(1, Math.Max(0, (d - it.MarkFrom) / it.MarkFrom));
                ((ShaderMaterial)it.Mark.MaterialOverride).SetShaderParameter(U.Opacity, (float)(0.9 * far * (1 - sinking)));
                it.Mark.Visible = sinking < 0.99;
            }

            var K = _def[it.Kind];
            if (it.Kind == "bottle" && pv < K.PickupSpeed
                && Math.Sqrt((it.X - px) * (it.X - px) + (it.Z - pz) * (it.Z - pz)) < K.PickupRadius)
            {
                Remove(it);
                OnBottle?.Invoke(it.From);
            }
        }
    }

    /* Une coque est descendue : ce qui remonte. */
    void Wreck(ShipNode s, Ocean sea, bool player)
    {
        var b = s.Physics.Body;
        var o = sea.Origin;
        double wx = b.Pos.X + o.X, wz = b.Pos.Z + o.Z;
        int n = _debrisMin + (int)(_rng.Randf() * (_debrisMax - _debrisMin + 1));
        for (int i = 0; i < n; i++)
        {
            string kind = _rng.Randf() < 0.6 ? "plank" : "barrel";
            Float(kind, wx + (_rng.Randf() - 0.5) * 16, wz + (_rng.Randf() - 0.5) * 16, null);
        }
        /* Une bouteille est le dernier geste de quelqu'un : le navire qu'on mène
           n'en jette pas. À quelle fréquence est une règle de JEU, pas une
           propriété de l'objet — settings.json répond. */
        if (!player && BottleOneIn > 0 && _rng.Randf() * BottleOneIn < 1)
            Float("bottle", wx + (_rng.Randf() - 0.5) * 10, wz + (_rng.Randf() - 0.5) * 10, s.Spec.Name);
    }

    void Float(string kind, double x, double z, string? from)
    {
        var K = _def[kind];
        var it = new Item
        {
            Kind = kind, X = x, Z = z, Yaw = _rng.Randf() * Mathf.Tau,
            Life = K.Life, Draft = K.Draft, From = from, MarkFrom = K.MarkFrom
        };
        it.Node = Draw(kind, K, it);
        AddChild(it.Node);
        _items.Add(it);
    }

    void Remove(Item it)
    {
        _items.Remove(it);
        RemoveChild(it.Node);
        it.Node.QueueFree();
    }

    /* ------------------------------------------------------------------ */
    /*  CE QU'ILS SONT, DESSINÉS                                           */
    /* ------------------------------------------------------------------ */

    ShaderMaterial? _hazePass;

    /* La même brume que les coques : un débris qui resterait net à deux cents
       mètres d'une mer délavée se lirait comme un décalque. */
    StandardMaterial3D Mat(string hex, float rough, float metal = 0)
    {
        if (_hazePass == null)
        {
            _hazePass = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/hull_haze.gdshader") };
            _hazed.Add(_hazePass);
        }
        return new StandardMaterial3D
        {
            AlbedoColor = Color.FromString(hex, Colors.White), Roughness = rough, Metallic = metal,
            NextPass = _hazePass
        };
    }

    Node3D Draw(string kind, Kind K, Item it)
    {
        var g = new Node3D { Scale = Vector3.One * (float)K.Scale };
        void Add(Mesh mesh, Material mat, Vector3 at, Vector3 rot)
        {
            g.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at, Rotation = rot });
        }
        if (kind == "plank")
        {
            float len = 1.6f + _rng.Randf() * 1.4f;
            Add(new BoxMesh { Size = new Vector3(len, 0.08f, 0.30f) },
                Mat(_rng.Randf() < 0.5f ? "#5e4630" : "#8a6a44", 0.9f), Vector3.Zero, Vector3.Zero);
            // et un bout éclaté, pour qu'elle se lise arrachée d'un navire et non sciée
            Add(new BoxMesh { Size = new Vector3(0.5f, 0.08f, 0.14f) },
                Mat("#8a6a44", 0.85f), new Vector3(len * 0.5f + 0.2f, 0, 0.07f), new Vector3(0, 0.3f, 0));
        }
        else if (kind == "barrel")
        {
            Add(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.9f, RadialSegments = 14 },
                Mat("#5e4630", 0.9f), Vector3.Zero, new Vector3(0, 0, Mathf.Pi / 2));   // couché sur le flanc
            foreach (float x in new[] { -0.3f, 0.3f })
                Add(new TorusMesh { InnerRadius = 0.313f, OuterRadius = 0.357f, RingSegments = 16, Rings = 5 },
                    Mat("#2a2522", 0.6f, 0.4f), new Vector3(x, 0, 0), new Vector3(0, 0, Mathf.Pi / 2));
        }
        else
        {
            /* Dessinée à sa VRAIE taille ; Props.json la grossit trois fois, et
               c'est voulu — une vraie bouteille est un point à une longueur de
               navire. Le même argument que le boulet. */
            var glass = Mat("#1f5a2c", 0.12f, 0.1f);
            Add(new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.06f, Height = 0.22f, RadialSegments = 10 }, glass, Vector3.Zero, new Vector3(0, 0, 1.2f));
            Add(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.045f, Height = 0.09f, RadialSegments = 8 }, glass, new Vector3(-0.145f, 0.055f, 0), new Vector3(0, 0, 1.2f));
            Add(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.02f, Height = 0.04f, RadialSegments = 6 }, Mat("#9a7a52", 0.9f), new Vector3(-0.2f, 0.076f, 0), new Vector3(0, 0, 1.2f));
            if (K.Halo) Halo(g, K, it);
        }
        return g;
    }

    /* UN PEU DE MAGIE AUTOUR DE LA BOUTEILLE, demandée parce qu'elle était
       introuvable dans la houle. Une bulle de savon plutôt qu'une lampe : une
       sphère presque rien de face et vive sur son bord (Fresnel), avec l'arc-en-
       ciel d'un film mince et quelques éclats qui rampent dessus. Elle DONNE de
       la lumière au lieu d'en renvoyer — c'est la seule chose ici qui doive
       luire, et la nuit est justement le moment où il faut la trouver.

       Et un REPÈRE pour le loin : la bulle fait un mètre, un point à deux
       encablures, donc une lueur tenue à quelques pixels quelle que soit la
       distance — celle des fanaux — entre passé markFrom mètres. */
    void Halo(Node3D g, Kind K, Item it)
    {
        var bubble = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = (float)K.HaloRadius, Height = (float)K.HaloRadius * 2, RadialSegments = 28, Rings = 14 },
            MaterialOverride = _halo,
            Position = new Vector3(0, (float)K.HaloRadius * 0.3f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _halo.SetShaderParameter(U.Color, new Vector3(K.HaloColor.R, K.HaloColor.G, K.HaloColor.B));
        _halo.SetShaderParameter("u_intensity", (float)K.HaloIntensity);
        g.AddChild(bubble);

        var mark = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/lantern_glow.gdshader") };
        mark.SetShaderParameter(U.Color, new Vector3(K.HaloColor.R, K.HaloColor.G, K.HaloColor.B));
        mark.SetShaderParameter(U.Size, (float)K.Mark);
        mark.SetShaderParameter(U.Fixed, 1f);
        mark.SetShaderParameter(U.Opacity, 0f);
        it.Mark = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One }, MaterialOverride = mark,
            Position = new Vector3(0, (float)K.HaloRadius * 0.5f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 1e4f
        };
        g.AddChild(it.Mark);
    }
}
