using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE POISSON SUR LE PONT — chaque prise y tombe, et y pèse.
///
/// Il passe le pavois en arc, depuis le bord où pendait la ligne, rebondit sur
/// les planches, glisse quand le navire gîte ou tangue, bat de la queue un moment
/// puis reste couché sur le flanc. Les poissons s'empilent, et le bordé les tient
/// à bord.
///
/// UNE PHYSIQUE À NOUS, DANS LE REPÈRE DU NAVIRE, et non le moteur physique de
/// Godot : le navire n'est pas un corps de Godot, c'est notre solveur qui le
/// meut, et un pont de Godot traîné derrière lui à chaque image ferait sauter
/// tout ce qui est posé dessus. Ici la gravité est simplement tournée dans le
/// repère du bord, l'accélération du navire retranchée : un poisson glisse vers
/// le bord bas parce que le pont penche, et recule quand le navire prend de
/// l'erre — pour la même raison qu'un homme qui n'est pas amariné.
///
/// SON POIDS EST LÀ OÙ IL EST : un colis de la cale posé au niveau du pont, dans
/// le compartiment et du bord où il gît (<see cref="FishHold"/>). Le navire
/// s'assied d'autant et gîte du côté de la pile — de peu : cent cinquante kilos
/// sur les vingt-deux tonnes du sloop, c'est quelques millimètres. Le chiffre est
/// juste ; c'est la pile qui se voit.
/// </summary>
public partial class ShipDemo
{
    sealed class DeckFish
    {
        public FishLot Lot;
        public MeshInstance3D Node = null!;
        /// <summary>Dans le repère du navire.</summary>
        public Vector3 P, V;
        /// <summary>Le cap du poisson sur le pont, et son roulis autour de sa longueur (0 debout, ±π/2 sur le flanc).</summary>
        public float Yaw, Roll, YawV, RollV;
        /// <summary>Sa longueur, et son demi-épaisseur couché / demi-hauteur debout.</summary>
        public float Len, Thick, Tall;
        /// <summary>Ce qu'il lui reste à se débattre, et quand il battra de nouveau.</summary>
        public double Alive, NextFlop;
        public bool Resting;
    }

    readonly List<DeckFish> _deckFish = new();
    ShipNode? _deckShip;
    Mesh? _fishMesh;
    Material? _fishMat, _snapperMat;
    bool _fishMeshTried;
    float _fishTall = 0.41f, _fishWide = 0.51f;
    Vector3 _shipVelPrev;
    double _fishWeighAcc;

    /// <summary>Au-delà, le poisson pèse dans la cale sans être dessiné : un pont n'en montre pas mille.</summary>
    const int MaxDeckFish = 80;

    /// <summary>
    /// LE MODÈLE (props/merou.glb), ramené à la longueur 1 le long de x et centré.
    /// Le vivaneau n'a pas encore le sien : le même, teinté du rouge du vivaneau.
    /// </summary>
    void LoadFishMesh()
    {
        _fishMeshTried = true;
        string path = Assets.Path("props/merou.glb");
        if (!System.IO.File.Exists(path)) { GD.PushWarning("[pêche] props/merou.glb absent : pas de poisson sur le pont"); return; }
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(path, state) != Error.Ok || doc.GenerateScene(state) is not Node3D obj)
        {
            GD.PushWarning("[pêche] merou.glb illisible");
            return;
        }
        MeshInstance3D? mi = null;
        var stack = new Stack<Node>();
        stack.Push(obj);
        while (stack.Count > 0 && mi == null)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D m && m.Mesh != null) mi = m;
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        if (mi == null) { obj.QueueFree(); return; }
        var box = mi.Mesh.GetAabb();
        float len = Math.Max(1e-3f, box.Size.X);
        var mid = box.Position + box.Size * 0.5f;
        var outMesh = new ArrayMesh();
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            var arr = mi.Mesh.SurfaceGetArrays(s);
            /* LE DOS VERS +Y. Le modèle a le dos vers +z (relevé à la capture : posés
               « sur le flanc », ils étaient à plat ventre, nageoires écartées). On le
               redresse ici, sommets, normales et tangentes ensemble : ensuite, roulis
               nul veut dire debout, et ±90° couché sur un flanc. */
            static Vector3 Up(Vector3 v) => new(v.X, v.Z, -v.Y);
            var verts = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            for (int i = 0; i < verts.Length; i++) verts[i] = Up((verts[i] - mid) / len);
            arr[(int)Mesh.ArrayType.Vertex] = verts;
            if (arr[(int)Mesh.ArrayType.Normal].VariantType != Variant.Type.Nil)
            {
                var nrm = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                for (int i = 0; i < nrm.Length; i++) nrm[i] = Up(nrm[i]);
                arr[(int)Mesh.ArrayType.Normal] = nrm;
            }
            if (arr[(int)Mesh.ArrayType.Tangent].VariantType != Variant.Type.Nil)
            {
                var tg = arr[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
                for (int i = 0; i + 3 < tg.Length; i += 4) { float y = tg[i + 1]; tg[i + 1] = tg[i + 2]; tg[i + 2] = -y; }
                arr[(int)Mesh.ArrayType.Tangent] = tg;
            }
            outMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            outMesh.SurfaceSetMaterial(s, mi.Mesh.SurfaceGetMaterial(s));
        }
        _fishMesh = outMesh;
        // redressé : sa hauteur est l'ancien z, son épaisseur l'ancien y
        _fishTall = box.Size.Z / len;
        _fishWide = box.Size.Y / len;
        _fishMat = mi.Mesh.SurfaceGetMaterial(0);
        if (_fishMat is BaseMaterial3D bm)
        {
            var red = (BaseMaterial3D)bm.Duplicate();
            red.AlbedoColor = new Color(1.0f, 0.55f, 0.50f);
            _snapperMat = red;
        }
        obj.QueueFree();
    }

    /// <summary>
    /// SA LONGUEUR SELON SON POIDS : un poisson pèse comme le cube de sa longueur.
    /// Mérou de Nassau : dix kilos pour quatre-vingts centimètres ; vivaneau rouge :
    /// cinq kilos pour soixante-dix.
    /// </summary>
    static float FishLength(string key, double kg) =>
        (float)((key == "vivaneau" ? 0.41 : 0.37) * Math.Cbrt(Math.Max(0.2, kg)));

    DeckFish? MakeDeckFish(FishLot lot)
    {
        if (!_fishMeshTried) LoadFishMesh();
        if (_fishMesh == null || _deckFish.Count >= MaxDeckFish) return null;
        var f = new DeckFish { Lot = lot, Len = FishLength(lot.Key, lot.Kg) };
        // couché, il repose sur son flanc, nageoires écrasées : moins que la largeur du modèle
        f.Thick = 0.5f * f.Len * _fishWide * 0.55f;
        f.Tall = 0.5f * f.Len * _fishTall;
        f.Node = new MeshInstance3D { Mesh = _fishMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        if (lot.Key == "vivaneau" && _snapperMat != null) f.Node.MaterialOverride = _snapperMat;
        _ship.AddChild(f.Node);
        _deckFish.Add(f);
        return f;
    }

    /// <summary>
    /// UNE PRISE TOMBE À BORD : hissée par-dessus le pavois du bord où pendait sa
    /// ligne, lancée vers l'axe, vivante.
    /// </summary>
    void DropFish(FishLot lot)
    {
        EnsureDeckShip();
        if (MakeDeckFish(lot) is not { } f) return;
        var spec = _ship.Spec;
        float side = _fishRng.NextDouble() < 0.5 ? -1 : 1;
        float z = (float)((_fishRng.NextDouble() - 0.5) * 0.5 * spec.L);
        double deck = _ship.DeckTop(0, z);
        double hw = Math.Max(_ship.HullHalfWidth(z, deck + 0.3), 0.3 * spec.B);
        f.P = new Vector3(side * (float)(hw + 0.2), (float)(deck + 1.4), z);
        f.V = new Vector3(-side * (1.6f + (float)_fishRng.NextDouble()), 1.2f + (float)_fishRng.NextDouble(), (float)(_fishRng.NextDouble() - 0.5));
        f.Yaw = (float)(_fishRng.NextDouble() * Math.Tau);
        f.Roll = 0;
        f.YawV = (float)((_fishRng.NextDouble() - 0.5) * 6);
        f.RollV = (float)((_fishRng.NextDouble() - 0.5) * 10);
        f.Alive = 10 + _fishRng.NextDouble() * 20;
        f.NextFlop = 0.6;
        PoseFish(f);
    }

    /// <summary>
    /// LE PONT A CHANGÉ DE NAVIRE (le chantier) ou les poissons n'existent pas
    /// encore (une partie reprise) : les prises sont recouchées sur le pont du bord
    /// présent, au hasard, immobiles.
    /// </summary>
    void EnsureDeckShip()
    {
        if (_deckShip == _ship) return;
        foreach (var f in _deckFish) if (IsInstanceValid(f.Node)) f.Node.QueueFree();
        _deckFish.Clear();
        _deckShip = _ship;
        _shipVelPrev = Vector3.Zero;
        var spec = _ship.Spec;
        foreach (var lot in _catch)
        {
            if (MakeDeckFish(lot) is not { } f) break;
            float z = (float)((_fishRng.NextDouble() - 0.5) * 0.5 * spec.L);
            double deck = _ship.DeckTop(0, z);
            double hw = Math.Max(0.2, _ship.HullHalfWidth(z, deck + 0.3) - 0.3);
            float x = (float)((_fishRng.NextDouble() - 0.5) * 1.6 * hw);
            f.P = new Vector3(x, (float)(_ship.DeckTop(x, z) + f.Thick + 0.3), z);
            f.Yaw = (float)(_fishRng.NextDouble() * Math.Tau);
            f.Roll = _fishRng.NextDouble() < 0.5 ? -MathF.PI / 2 : MathF.PI / 2;
            f.Alive = 0;
            PoseFish(f);
        }
        FishHold();
    }

    void PoseFish(DeckFish f)
    {
        var basis = new Basis(Vector3.Up, f.Yaw) * new Basis(Vector3.Right, f.Roll);
        f.Node.Transform = new Transform3D(basis.Scaled(Vector3.One * f.Len), f.P);
    }

    /// <summary>À chaque image : la physique des poissons sur le pont, puis leur poids là où ils sont.</summary>
    void DeckFishTick(double dt)
    {
        if (_catch.Count == 0 && _deckFish.Count == 0) return;
        EnsureDeckShip();

        // les prises vendues, gâtées ou perdues quittent le pont
        if (_deckFish.Count > 0)
        {
            var keep = new HashSet<FishLot>(_catch);
            for (int i = _deckFish.Count - 1; i >= 0; i--)
                if (!keep.Contains(_deckFish[i].Lot)) { _deckFish[i].Node.QueueFree(); _deckFish.RemoveAt(i); }
        }
        if (_deckFish.Count == 0 || dt <= 0) { WeighFish(dt, false); return; }

        /* LA PESANTEUR DANS LE REPÈRE DU BORD, moins son accélération : c'est ce
           qu'un objet posé sur le pont ressent. Le roulis et le tangage y sont,
           puisque c'est le pont qui a tourné sous la verticale. */
        var b = _ship.Physics.Body;
        var vel = new Vector3((float)b.Vel.X, (float)b.Vel.Y, (float)b.Vel.Z);
        var acc = (vel - _shipVelPrev) / (float)Math.Max(dt, 1e-3);
        _shipVelPrev = vel;
        // une image lente, une mise à l'eau, un saut : pas de coup de pied à tout ce qui est posé
        if (acc.Length() > 30) acc = Vector3.Zero;
        var inv = _ship.GlobalTransform.Basis.Inverse();
        var gLocal = inv * (new Vector3(0, -9.81f, 0) - acc);

        bool moving = false;
        int steps = Math.Clamp((int)Math.Ceiling(dt / (1.0 / 90)), 1, 6);
        float h = (float)(dt / steps);
        for (int s = 0; s < steps; s++)
        {
            foreach (var f in _deckFish) StepFish(f, h, gLocal);
            SeparateFish();
        }
        foreach (var f in _deckFish)
        {
            PoseFish(f);
            if (!f.Resting) moving = true;
        }
        WeighFish(dt, moving);
    }

    void StepFish(DeckFish f, float h, Vector3 g)
    {
        var spec = _ship.Spec;
        f.V += g * h;
        f.P += f.V * h;
        f.Yaw += f.YawV * h;
        f.Roll += f.RollV * h;

        double deck = _ship.DeckTop(f.P.X, f.P.Z);
        // la demi-épaisseur du côté sur lequel il repose : debout il est haut, couché il est mince
        float lie = Math.Abs(MathF.Sin(f.Roll));
        float r = f.Tall * (1 - lie) + f.Thick * lie;
        float floor = (float)deck + r;
        bool contact = f.P.Y <= floor + 0.01f;
        if (f.P.Y < floor)
        {
            f.P.Y = floor;
            if (f.V.Y < 0) f.V.Y = -f.V.Y * (f.V.Y < -1.5f ? 0.3f : 0f);
        }
        if (contact)
        {
            /* GLISSANT : un poisson frais est couvert de mucus, le frottement est
               faible — il file sur un pont mouillé qui gîte. */
            float mu = 0.3f;
            var vt = new Vector3(f.V.X, 0, f.V.Z);
            float vl = vt.Length();
            float drop = mu * Math.Max(0, -g.Y) * h;
            if (vl <= drop) { f.V.X = 0; f.V.Z = 0; }
            else { f.V.X -= vt.X / vl * drop; f.V.Z -= vt.Z / vl * drop; }
            // il se couche : le roulis tombe vers le flanc le plus proche, et s'y amortit
            float target = f.Roll >= 0 ? MathF.PI / 2 : -MathF.PI / 2;
            f.RollV += (target - f.Roll) * 40 * h;
            f.RollV *= MathF.Max(0, 1 - 6 * h);
            f.YawV *= MathF.Max(0, 1 - 4 * h);

            // il se débat : des sauts de plus en plus rares et faibles
            if (f.Alive > 0)
            {
                f.Alive -= h;
                f.NextFlop -= h;
                if (f.NextFlop <= 0)
                {
                    float vigor = (float)Math.Min(1, f.Alive / 15) + 0.2f;
                    f.V += new Vector3((float)(_fishRng.NextDouble() - 0.5) * 1.2f, (1.0f + (float)_fishRng.NextDouble()) * vigor, (float)(_fishRng.NextDouble() - 0.5) * 1.2f) * MathF.Min(1, 0.6f + 0.1f / MathF.Max(0.3f, f.Len));
                    f.RollV += (_fishRng.NextDouble() < 0.5 ? -1 : 1) * (6 + 6 * (float)_fishRng.NextDouble()) * vigor;
                    f.YawV += (float)(_fishRng.NextDouble() - 0.5) * 8 * vigor;
                    f.NextFlop = 0.4 + _fishRng.NextDouble() * (2.5 - 2 * Math.Min(1, f.Alive / 20));
                }
            }
        }

        // le bordé le tient à bord, à sa hauteur — au-dessus du plat-bord, plus rien ne le retient
        double hw = _ship.HullHalfWidth(f.P.Z, f.P.Y);
        if (hw > 0)
        {
            float inner = (float)Math.Max(0.1, hw - 0.12 - 0.3 * f.Len);
            if (Math.Abs(f.P.X) > inner)
            {
                f.P.X = Math.Sign(f.P.X) * inner;
                if (f.V.X * Math.Sign(f.P.X) > 0) f.V.X = -f.V.X * 0.25f;
            }
        }
        // les bouts : l'étrave et le tableau
        float zEnd = (float)(0.45 * spec.L - 0.3 * f.Len);
        if (Math.Abs(f.P.Z) > zEnd) { f.P.Z = Math.Sign(f.P.Z) * zEnd; f.V.Z = -f.V.Z * 0.25f; }

        f.Resting = contact && f.Alive <= 0 && f.V.LengthSquared() < 0.0025f && Math.Abs(f.RollV) < 0.2f;
    }

    /// <summary>
    /// ILS S'EMPILENT : chaque poisson est une boule aplatie — large de sa
    /// longueur en plan, mince de son épaisseur en hauteur —, et deux qui se
    /// chevauchent se repoussent le long de cette mesure. Sur un pont qui se
    /// remplit, ils se couchent les uns sur les autres au lieu de se traverser.
    /// </summary>
    void SeparateFish()
    {
        int n = _deckFish.Count;
        for (int i = 0; i < n; i++)
        {
            var a = _deckFish[i];
            for (int j = i + 1; j < n; j++)
            {
                var c = _deckFish[j];
                float rh = 0.32f * (a.Len + c.Len), rv = a.Thick + c.Thick;
                var d = c.P - a.P;
                float ex = d.X / rh, ey = d.Y / rv, ez = d.Z / rh;
                float q = ex * ex + ey * ey + ez * ez;
                if (q >= 1 || q < 1e-8f) continue;
                float k = (1 - MathF.Sqrt(q)) * 0.5f;
                float qn = MathF.Sqrt(q);
                var push = new Vector3(ex / qn * rh, ey / qn * rv, ez / qn * rh) * k;
                a.P -= push; c.P += push;
                // celui du dessus repose sur l'autre : sa chute s'arrête
                if (push.Y > 0 && c.V.Y < 0) c.V.Y *= 0.2f;
                if (push.Y < 0 && a.V.Y < 0) a.V.Y *= 0.2f;
                a.Resting = c.Resting = false;
            }
        }
    }

    /// <summary>Le poids suit les poissons qui bougent, une fois par seconde ; immobiles, il ne change plus.</summary>
    void WeighFish(double dt, bool moving)
    {
        _fishWeighAcc += dt;
        if (!moving || _fishWeighAcc < 1) return;
        _fishWeighAcc = 0;
        FishHold();
    }

    /// <summary>
    /// LE POIDS DES PRISES, rebâti : un colis au niveau du pont pour chaque poisson
    /// qu'on voit, dans le compartiment et du bord où il gît ; le reste (au-delà de
    /// ce que le pont montre) au fond de la cale. La cale efface un colis de moins
    /// d'un kilo, d'où le recalage complet plutôt qu'un ajout prise par prise.
    /// </summary>
    void FishHold()
    {
        // en chaloupe, c'est le navire laissé qui porte la pêche
        var ship = _mother ?? _ship;
        var ph = ship.Physics;
        ph.UnloadKind(FishKind, 1e9);
        double rest = 0;
        var shown = new HashSet<FishLot>();
        // une prise vendue l'instant d'avant est encore dessinée : elle ne pèse plus
        var live = new HashSet<FishLot>(_catch);
        if (_deckShip == ship)
            foreach (var f in _deckFish)
            {
                if (!live.Contains(f.Lot)) continue;
                shown.Add(f.Lot);
                int hold = 0;
                double best = double.MaxValue;
                for (int k = 0; k < ph.Comps.Length; k++)
                {
                    double d = Math.Abs(ph.Comps[k].Mid.Z - f.P.Z);
                    if (d < best) { best = d; hold = k; }
                }
                var c = ph.Comps.Length > 0 ? ph.Comps[hold] : null;
                double side = c == null || c.HalfB <= 0 ? 0 : Math.Clamp(-f.P.X / (0.55 * c.HalfB), -1, 1);
                // au dixième près : deux poissons au même endroit font un colis, pas deux
                ph.LoadCargo(hold, 1.0, Math.Round(side, 1), f.Lot.Kg / 1000, FishKind);
            }
        foreach (var l in _catch) if (!shown.Contains(l)) rest += l.Kg;
        if (rest > 0) ph.LoadCargo(Config.NComp / 2, HoldFloor, 0, rest / 1000, FishKind);
    }
}
