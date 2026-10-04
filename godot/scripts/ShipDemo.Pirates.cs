using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>Qui se bat et pourquoi : le pavillon noir, la barre des autres (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    // ------------------------------------------------------------------
    //  QUI SE BAT, ET POURQUOI — le pavillon noir, la barre des autres,
    //  le pirate qui chasse puis aborde
    // ------------------------------------------------------------------

    /* LE PAVILLON NOIR EST UNE DÉCLARATION, PAS UNE DÉCORATION : un navire est
       hostile parce qu'il arbore la tête de mort (appearance.ensign = « jolly »),
       et non parce qu'une fiche porte un drapeau booléen quelque part. Tous les
       autres sont pacifiques jusqu'à ce qu'on les touche. */
    static bool IsJolly(ShipNode s) => s.Spec.Appearance.Ensign == "jolly";

    readonly Dictionary<ShipNode, AutoHelm> _helms = new();
    readonly Dictionary<ShipNode, Pirate> _pirates = new();
    readonly List<Pirate.Sail> _sails = new();

    AutoHelm HelmOf(ShipNode s)
    {
        if (_helms.TryGetValue(s, out var h)) return h;
        h = new AutoHelm(s.Physics);
        _helms[s] = h;
        return h;
    }

    /* SON HUMEUR DE PIRATE, sur son entrée et nulle part ailleurs ; et la garde
       suit le pavillon : une barre réglée à une longueur et demie amène à quarante
       mètres, ce qui est un abordage et non un duel. Un navire qui vient canonner
       se tient au plein fouet. */
    void Arm(ShipNode s)
    {
        if (!IsJolly(s) || _pirates.ContainsKey(s)) return;
        var h = HelmOf(s);
        h.Standoff = Math.Max(h.Standoff, 185);
        _pirates[s] = new Pirate(h.Standoff);
    }

    /// <summary>La coque qu'un navire a pour ennemi, ou nulle : le pirate pendant sa chasse, un navire provoqué contre qui l'a touché.</summary>
    /* LE PIRATE QUI APPROCHE, ANNONCÉ À TEMPS. Une voile noire à l'horizon était
       dite, puis plus rien jusqu'aux grappins — trop tard pour faire quoi que ce
       soit (signalé). Deux crans, chacun une fois, du plus loin au plus près :
       qu'on le tienne à distance tant qu'il est temps, puis qu'il est sur nous.
       Le compte se rouvre quand il s'est éloigné, avec une marge pour qu'un
       pirate qui louvoie à la limite ne le fasse pas répéter. */
    readonly Dictionary<Pirate, int> _pirWarn = new();
    // le second cran EN DEÇÀ de sa garde de tir (185 m) : à 185 il canonne, plus près il vient à couple
    const double WarnFar = 900, WarnNear = 120, WarnReset = 1300;

    void PirateWarn(ShipNode s, Pirate p)
    {
        if (s.IsGhost || p.Cible != _ship.Physics || p.State == Pirate.Phase.Fuite) { _pirWarn.Remove(p); return; }
        var a = s.Physics.Body.Pos; var b = _ship.Physics.Body.Pos;
        double d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
        int stage = _pirWarn.GetValueOrDefault(p);
        if (d > WarnReset) { if (stage != 0) _pirWarn[p] = 0; return; }
        if (stage < 2 && d < WarnNear)
        {
            _pirWarn[p] = 2;
            Say("Le pirate est sur nous ! Ne le laissez pas venir à couple — feu, ou virez de bord");
        }
        else if (stage < 1 && d < WarnFar)
        {
            _pirWarn[p] = 1;
            Say(FormattableString.Invariant($"Un pirate se rapproche à {d:F0} m — tenez-le à distance, ne le laissez pas aborder"));
        }
    }

    ShipPhysics? EnemyOf(ShipNode s)
    {
        if (_pirates.TryGetValue(s, out var p)) return p.Enemy;
        // en escarmouche, c'est le pavillon qui désigne l'ennemi, et lui seul
        if (_skirmish && !s.IsGhost) return MeleeFoe(s)?.Physics;
        if (s.IsGhost) return _ghosts.Of(s.Physics)?.Foe;
        if (_hostile.TryGetValue(s, out var h) && IsInstanceValid(h.Foe) && !h.Foe.Physics.Foundered) return h.Foe.Physics;
        return null;
    }

    /* LA BARRE DES AUTRES, avant le solveur : elle écrit dans les mêmes commandes
       qu'une main, jamais dans la coque. Le pirate choisit où aller ; un navire
       provoqué va vers qui l'a touché ; les autres ne bougent pas de leur route. */
    void Steer(ShipNode s, double dt)
    {
        if (_pirates.TryGetValue(s, out var p))
        {
            Unknown(p);
            var h = HelmOf(s);
            var prey = p.Cible;
            _sails.Clear();
            _sails.Add(new Pirate.Sail(_ship.Physics, _ship.Battery, IsJolly(_ship)));
            // un vrai pirate ne chasse pas les spectres
            foreach (var o in _others) if (!o.IsGhost) _sails.Add(new Pirate.Sail(o.Physics, o.Battery, IsJolly(o)));
            p.Grappled = prey != null && _grapples.Holds(prey);
            string? ev = p.Pilot(dt, _t, s.Physics, h, _sails, _ship.Physics);
            PirateWarn(s, p);

            /* LA VOLÉE. Il lance quand il est à portée de crochet et qu'il n'en a
               plus un seul qui tienne — donc au premier abordage, et de nouveau
               chaque fois qu'on lui a tout coupé. Le délai empêche seulement de
               relancer à chaque image pendant que les crochets volent encore. */
            if (prey != null && p.State == Pirate.Phase.Abordage && !_grapples.Holds(prey))
            {
                var pb2 = prey.Body; var sb2 = s.Physics.Body;
                double bord = Math.Sqrt((pb2.Pos.X - sb2.Pos.X) * (pb2.Pos.X - sb2.Pos.X)
                                      + (pb2.Pos.Z - sb2.Pos.Z) * (pb2.Pos.Z - sb2.Pos.Z))
                            - (s.Physics.Spec.B + prey.Spec.B) * 0.5;
                if (bord < Grapple.Portee && _t - _volee.GetValueOrDefault(s.Physics, -99) > 6)
                {
                    _volee[s.Physics] = _t;
                    _grapples.Throw(s.Physics, prey, _grappleRng);
                    if (prey == _ship.Physics) Say("Des grappins ! Coupez les filins (⇧D)");
                }
            }
            if (ev != null && prey != null)
            {
                bool mine = prey == _ship.Physics;
                if (ev == "abordage" && mine) Say("Le pirate cesse le feu — il vient vous aborder par l’arrière");
                else if (ev == "pillage")
                {
                    if (mine) Say(Pillage());
                    else if ((prey.Body.Pos - _ship.Physics.Body.Pos).Length < 3000)
                    {
                        string name = "un navire";
                        foreach (var o in _others) if (o.Physics == prey) { name = o.Spec.Name; break; }
                        Say("Le pirate aborde et pille " + name);
                    }
                }
                /* PAS DE QUARTIER — la prise avait tiré. Le pillage lui a déjà
                   tout pris ; ce qui suit est la vengeance, et elle est la même
                   pour tout le monde : la soute saute. Pour le joueur, c'est la
                   fin de la partie. */
                else if (ev == "carnage")
                {
                    if (mine) Captured();
                    else foreach (var o in _others) if (o.Physics == prey)
                    {
                        BlowUp(o);
                        if ((prey.Body.Pos - _ship.Physics.Body.Pos).Length < 6000)
                            Say($"Le pirate ne fait pas de quartier — {o.Spec.Name} saute");
                        break;
                    }
                }
            }
            h.Update(dt, _sea.Core, s.Ctrl);
            return;
        }
        /* Sans pavillon, les honnêtes gens s'écartent avant tout le reste — mais
           pas au milieu d'une bataille rangée : là, amener ses couleurs vous sort
           d'un camp, cela ne fait pas fuir la ligne d'en face. */
        if (!_skirmish && Wary(s, dt)) return;
        if (s.IsGhost)
        {
            // droit sur l'ennemi que la scène lui donne ; la bataille finie, il garde sa route
            var foe = _ghosts.Of(s.Physics)?.Foe;
            var h = HelmOf(s);
            if (foe != null) h.Target = foe.Body.Pos;
            h.Update(dt, _sea.Core, s.Ctrl);
            return;
        }
        /* Droit sur l'ennemi qu'on lui connaît, d'où qu'il vienne — un boulet
           reçu hier, ou le pavillon d'en face. Une seule question posée une seule
           fois : les canons la posent déjà (ServeGuns), la barre la posait
           autrement, et deux réponses auraient fini par différer. */
        if (EnemyOf(s) is { } quarry)
        {
            var h = HelmOf(s);
            h.Target = quarry.Body.Pos;
            h.Update(dt, _sea.Core, s.Ctrl);
            return;
        }
        // un navire de la rade suit sa route (ShipDemo.Traffic.cs)
        if (SteerTrip(s, dt)) return;
        /* ET UN MARCHAND VA QUELQUE PART. Son but est en mètres VRAIS et la barre
           travaille en local : l'origine flottante glisse sous lui, donc on le
           ramène à chaque image plutôt que de retenir un point qui se périmerait
           au premier déplacement du zéro. */
        if (_bound.TryGetValue(s, out var port))
        {
            var o = _sea.Core.Origin;
            var h = HelmOf(s);
            h.Standoff = 0;                         // il va AU port, il ne tourne pas autour
            h.Target = new Vec3d(port.X - o.X, 0, port.Z - o.Z);
            h.Update(dt, _sea.Core, s.Ctrl);
        }
    }

    /* IL PARAÎT AU VENT, ET CAP SUR VOUS : un pirate tient l'AVANTAGE DU VENT —
       c'est lui qui choisit d'engager ou non, et vous qui devez remonter vers lui
       pour lui échapper ou le combattre. Paru sous le vent, il mettrait une
       demi-heure à louvoyer jusqu'à vous, gagnant sept dixièmes de nœud au vent. */
    /// <summary>Faire paraître un pirate à <paramref name="dist"/> mètres, au vent de vous, sa soute pleine.</summary>
    void SpawnPirate(double dist)
    {
        int idx = _paths.FindIndex(p => System.IO.Path.GetFileName(p) == "pirate.json");
        if (idx < 0) { GD.PushWarning("ships/pirate.json introuvable"); return; }
        int before = _others.Count;
        SpawnFleet(1, idx);
        if (_others.Count == before) return;
        var s = _others[^1];
        var b = s.Physics.Body;
        var me = _ship.Physics.Body.Pos;
        // d'où vient le vent, en relèvement : l'avant est +z, l'est −x
        double wf = _sea.Core.WindDeg * Math.PI / 180;
        b.Pos = new Vec3d(me.X - Math.Sin(wf) * dist, b.Pos.Y, me.Z + Math.Cos(wf) * dist);
        // cap sur vous : le vent dans le dos, ou presque
        double toMe = Math.Atan2(-(me.X - b.Pos.X), me.Z - b.Pos.Z);
        b.Quat = Quatd.FromAxisAngle(new Vec3d(0, 1, 0), -toMe);
        s.SyncTransform();
        Arm(s);
        Say("Une voile sous pavillon noir !");
    }
}
