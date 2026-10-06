using Godot;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES MANTELETS DE SABORD — lus sur le modèle, par leur NOM comme les pièces : un
/// objet dont le nom contient « sabord » ou « mantelet » est un mantelet, modelé
/// FERMÉ. Rien d'autre à préparer dans Blender : la charnière est prise sur son
/// arête HAUTE, côté EXTÉRIEUR (là où on la cloue sur un vrai bordé), et il pivote
/// vers le haut et le dehors autour de l'axe du navire. (Pas « gunport » : « gun »
/// en fait une pièce, voir ShipNode.Guns.)
///
/// LE BRANLE-BAS, DANS L'ORDRE D'UN BORD : on ouvre le sabord, PUIS on met la pièce
/// en batterie — on hale ses palans jusqu'à ce que la bouche passe le bordé. À la
/// fermeture, l'inverse : la pièce rentre, puis le mantelet retombe. Un sabord
/// fermé a donc sa pièce rentrée de la longueur de son recul (sans quoi le tube
/// traverserait le mantelet). Le tout s'égrène le long du bord, de l'avant à
/// l'arrière, comme une équipe qui passe de pièce en pièce.
///
/// Ce que le navire veut (PortsWanted) est posé par le jeu à chaque image ; le
/// navire n'en sait que ça.
/// </summary>
public partial class ShipNode
{
    internal static readonly Regex LidNames = new("sabord|mantelet", RegexOptions.IgnoreCase);

    sealed class PortLid
    {
        public Node3D Pivot = null!;
        public float Sign;            // +1 bâbord (+x), −1 tribord
        public double Delay;          // son rang le long du bord, en secondes
        /// <summary>0 fermé · 1 ouvert, pièce rentrée · 2 ouvert, pièce en batterie.</summary>
        public double Phase;
        public int Gun = -1;          // l'indice de la pièce qu'il couvre (rang de Battery.Guns), ou −1
    }

    readonly List<PortLid> _lids = new();
    /// <summary>Le navire veut-il ses sabords ouverts ? Posé par le jeu à chaque image.</summary>
    public bool PortsWanted;
    bool _portsWas;
    double _portsSince;

    /// <summary>A-t-il des mantelets modelés ?</summary>
    public bool HasPortLids => _lids.Count > 0;
    /// <summary>Ses sabords sont-ils tous ouverts et ses pièces en batterie ?</summary>
    public bool PortsOpen { get { foreach (var l in _lids) if (l.Phase < 2) return false; return _lids.Count > 0; } }

    /// <summary>L'ouverture d'un mantelet, en radians : un peu moins que l'horizontale, comme on les voit sur les gravures.</summary>
    const float LidOpen = 1.45f;
    /// <summary>Ce que prend un mantelet à s'ouvrir, la pièce à sortir, et l'écart d'une pièce à la suivante, en secondes.</summary>
    const double LidSwing = 1.2, RunOutTime = 1.6, LidStagger = 0.25;

    /// <summary>Les mantelets du modèle, chacun pendu à sa charnière ; appelé après FindGuns (il les apparie aux pièces).</summary>
    void FindPortLids()
    {
        _lids.Clear();
        if (ModelRoot == null) return;
        var found = new List<Node3D>();
        void Gather(Node n)
        {
            foreach (var ch in n.GetChildren())
            {
                if (ch is Node3D n3 && LidNames.IsMatch(n3.Name.ToString())) { found.Add(n3); continue; }
                Gather(ch);
            }
        }
        Gather(ModelRoot);
        if (found.Count == 0) return;

        var toShip = GlobalTransform.AffineInverse();
        foreach (var nd in found)
        {
            var bb = PieceBox(nd);
            var rel = toShip * nd.GlobalTransform;
            Vector3 lo = Vector3.Inf, hi = -Vector3.Inf;
            for (int i = 0; i < 8; i++)
            {
                var p = rel * bb.GetEndpoint(i);
                lo = lo.Min(p); hi = hi.Max(p);
            }
            float mx = (lo.X + hi.X) * 0.5f;
            if (Math.Abs(mx) < 0.05f) continue;           // dans l'axe : un mantelet de poupe, pas de bordée — laissé tel quel
            float sign = Math.Sign(mx);
            // la charnière : l'arête haute, côté dehors
            var hinge = new Vector3(sign > 0 ? hi.X : lo.X, hi.Y, (lo.Z + hi.Z) * 0.5f);
            var pivot = new Node3D { Name = "charniere_" + nd.Name, Position = hinge };
            AddChild(pivot);
            nd.Reparent(pivot, true);
            _lids.Add(new PortLid { Pivot = pivot, Sign = sign });
        }

        // de l'avant à l'arrière, un bord après l'autre : l'équipe passe de pièce en pièce
        foreach (float side in new[] { 1f, -1f })
        {
            var mine = _lids.FindAll(l => l.Sign == side);
            mine.Sort((a, b) => b.Pivot.Position.Z.CompareTo(a.Pivot.Position.Z));
            for (int i = 0; i < mine.Count; i++) mine[i].Delay = i * LidStagger;
        }

        // chaque mantelet à la pièce la plus proche de son bord (à moins d'un mètre et demi)
        var taken = new HashSet<int>();
        foreach (var l in _lids)
        {
            int best = -1; float bd = 1.5f;
            for (int i = 0; i < _gunPieces.Count && i < Battery.Guns.Count; i++)
            {
                if (taken.Contains(i) || Math.Sign(Battery.Guns[i].P.X) != l.Sign) continue;
                var hp = _gunPieces[i].Home;
                float d = new Vector2(hp.Y - l.Pivot.Position.Y, hp.Z - l.Pivot.Position.Z).Length();
                if (d < bd) { bd = d; best = i; }
            }
            if (best >= 0) { l.Gun = best; taken.Add(best); }
        }
        RigLog.Add($"sabords : {_lids.Count} mantelet(s), {taken.Count} sur une pièce");
    }

    /// <summary>Une image du branle-bas : chaque sabord s'ouvre puis sort sa pièce, ou l'inverse.</summary>
    public void PortsTick(double dt)
    {
        if (_lids.Count == 0) return;
        if (PortsWanted != _portsWas) { _portsWas = PortsWanted; _portsSince = 0; }
        _portsSince += dt;
        foreach (var l in _lids)
        {
            if (l.Pivot == null || !IsInstanceValid(l.Pivot)) continue;
            if (_portsSince < l.Delay) continue;
            double was = l.Phase;
            if (PortsWanted)
                l.Phase = Math.Min(2, l.Phase + dt / (l.Phase < 1 ? LidSwing : RunOutTime));
            else
                l.Phase = Math.Max(0, l.Phase - dt / (l.Phase > 1 ? RunOutTime : LidSwing));
            if (l.Phase == was) continue;
            float a = LidOpen * (float)MathX.Smooth01(Math.Min(1, l.Phase));
            l.Pivot.Rotation = new Vector3(0, 0, l.Sign * a);
        }
    }

    /// <summary>De combien la pièce <paramref name="gun"/> est rentrée, de 0 (en batterie) à 1 (de toute la longueur de son recul).</summary>
    double LidInboard(int gun)
    {
        foreach (var l in _lids)
            if (l.Gun == gun) return 1 - MathX.Smooth01(Math.Clamp(l.Phase - 1, 0, 1));
        return 0;
    }

    /// <summary>À la rupture, les mantelets de l'avant partent avec lui, figés où ils sont (comme ShipNode.HandOver pour les pièces).</summary>
    void HandOverLids(float zCut, Node3D holder)
    {
        for (int i = _lids.Count - 1; i >= 0; i--)
        {
            var l = _lids[i];
            if (l.Pivot == null || !IsInstanceValid(l.Pivot) || l.Pivot.Position.Z <= zCut) continue;
            l.Pivot.Reparent(holder, true);
            _lids.RemoveAt(i);
        }
    }

    /// <summary>
    /// ESSAI (-- --sabords-essai 1) : faute de mantelets modelés, une planche posée sur
    /// chaque pièce de bordée, au bordé, et nommée comme le serait un mantelet —
    /// pour éprouver la charnière et le branle-bas avant que le modèle n'en ait.
    /// </summary>
    public static bool TestLids;

    void MakeTestLids()
    {
        if (!TestLids || ModelRoot == null || _lids.Count > 0) return;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.12f, 0.08f) };
        int n = 0;
        for (int i = 0; i < _gunPieces.Count && i < Battery.Guns.Count; i++)
        {
            var g = Battery.Guns[i];
            if (Math.Abs(g.Side) != 1) continue;
            float sign = Math.Sign((float)g.P.X);
            var at = g.P.ToGodot() - new Vector3(sign * 0.15f, 0, 0);
            var box = new MeshInstance3D
            {
                Name = $"sabordEssai_{n++}",
                Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.5f, 0.55f) },
                MaterialOverride = mat
            };
            ModelRoot.AddChild(box);
            box.GlobalTransform = GlobalTransform * new Transform3D(Basis.Identity, at);
        }
    }
}
