using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using NavalSim.Core;

namespace NavalSim;

/// <summary>La flotte d'essai (--flotte N), pour le test de charge (sorti de ShipDemo.cs).</summary>
public partial class ShipDemo
{
    // ------------------------------------------------------------------
    //  LA FLOTTE D'ESSAI — --flotte N, pour le test de charge
    // ------------------------------------------------------------------

    /* D'AUTRES COQUES À FLOT, chacune entière : son solveur, sa toile, ses feux et
       son pendule, sa rangée dans la texture de profils, ses gerbes. Rangées par
       six, à 140 m par le travers et 160 m devant la nôtre, pour qu'elles soient
       dans l'image sans se toucher. La règle de la page tient : tout état d'un
       navire vit sur SON instance, et l'indice dans la flotte est la rangée de
       profil — au-delà de Config.MaxShips la mer ne les voit plus, mais elles
       flottent et se dessinent. */
    readonly List<ShipNode> _others = new();
    /* la frégate du XVIIe : modèle, toile, feux, lanterne pendue — cherchée par son
       FICHIER, un numéro glisse dès qu'une fiche s'ajoute (la caisse l'avait fait
       passer à la grande frégate) */
    int _flotteShip = -1;

    /* LE MÉNAGE AU CHARGEMENT. Mettre une coque à l'eau laisse derrière soi des
       dizaines de mégaoctets morts — sommets relus, images de rugosité, tableaux
       de mesure : 394 Mo pour neuf frégates. Laissés au ramasse-miettes, ils
       partaient en pleine course, dans une collecte complète qui gelait une image
       de 38 à 42 ms. On la fait ici, pendant que rien ne bouge. */
    static void Sweep()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    void SpawnFleet(int n, int specIndex, bool arm = true)
    {
        var spec = ShipLibrary.Load(_paths[(specIndex % _paths.Count + _paths.Count) % _paths.Count]);
        if (spec == null) return;
        for (int i = 0; i < n; i++)
        {
            /* LES AUTRES COQUES N OMBRENT PAS LEURS FANAUX, ET C EST MESURÉ.

               Une omni qui porte ombre rend un CUBE — six faces — à chaque image où
               elle est visible, et c est la dépense la plus chère du projet par
               lumière. La nuit, synchro coupée : un navire seul passe de 4,55 à
               5,56 ms quand on les allume, et une escarmouche de douze coques de
               14,78 à 16,67 — soit le budget entier atteint par cette seule case.

               Or l essentiel de ce qu elles donnent est sur LE PONT OÙ L ON EST :
               le mât dont l ombre tourne sur le gaillard, le pavois qui coupe la
               lumière du fanal. Les autres coques, la nuit, ne sont que des feux à
               distance — on ne lit pas l ombre d un hauban sur un navire qu on voit
               à deux encablures. On paie donc l effet là où il se voit, et pas
               ailleurs : la moitié de la dépense s en va sans qu il en manque rien. */
            var s = new ShipNode { LanternShadows = false, WithMastLantern = _settings.MastLantern };
            AddChild(s);
            /* UNE MISE À L EAU QUI ÉCHOUE NE LAISSE RIEN : sans cela, une coque à
               demi née restait dans la scène sans être de la flotte — immobile,
               intouchable, collée à l origine locale (signalé). */
            try
            {
            s.Build(spec);
            s.Physics.World = _world;
            // les pontons aussi : RefreshJetties ne les donne qu'à qui est déjà à l'eau
            s.Physics.Jetties = _jetties;
            s.Ctrl.SailsSet = true;
            s.Ctrl.Sheet = 0.6;
            double y = SettleAfloat(s, _sea.Core);
            var b = s.Physics.Body;
            b.Pos = new Vec3d((i % 6 - 2.5) * 140, b.Pos.Y, 160 + (i / 6) * 180);
            s.SyncTransform();
            int row = _fleet.Count;
            var prof = s.MakeProfile(-y);
            if (row < Config.MaxShips) _sea.SetHullProfile(row, prof);
            var col = s.MakeCollider(prof);
            _spray.Pool.Colliders.Add(col);
            _hulls[s] = (prof, col, y);
            _fleet.Add(s.Physics);
            s.Physics.OnSlam = QueueSlam;
            _others.Add(s);
            if (arm) Arm(s);
            Colours(s);
            }
            catch (Exception e)
            {
                GD.PushWarning($"[{spec.Id}] mise à l eau abandonnée : {e.Message}");
                if (_others.Remove(s)) _fleet.Remove(s.Physics);
                RemoveChild(s); s.QueueFree();
            }
        }
        GD.Print($"flotte d'essai : {n} × {spec.Name}");
        Sweep();
    }
}
