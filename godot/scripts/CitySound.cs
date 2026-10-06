using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// CE QU'ON ENTEND D'UNE VILLE (demandé) — sons.json → ville et mouettes :
/// <list type="bullet">
/// <item>la RUMEUR, posée au centre de la ville (la grille des rues) : celle du jour,
/// puis celle de la nuit — vide, la ville se tait la nuit ;</item>
/// <item>la FORGE, qui frappe de temps en temps et jamais sans arrêt, le jour seulement,
/// au pâté le plus proche du quai — le forgeron d'un port ferre les poulies, les
/// cercles de mât et les ancres, et il est où on les débarque ;</item>
/// <item>les MOUETTES LOINTAINES, au-dessus du port où tourne leur vol (GullNode.Port) :
/// on les entend du ponton de départ, et plus quand le vol n'y est plus.</item>
/// </list>
/// Des lecteurs À EUX, et non ceux de la réserve des coups : une boucle qui dure
/// des heures en tiendrait un pour toujours, et une ville est une source GRANDE —
/// sa taille d'unité est la moitié d'une rue, pas celle d'une pièce de canon.
/// Tous sur le bus du dehors : une chambre fermée ou l'eau les étouffent.
/// </summary>
public partial class CitySound : Node3D
{
    // ---- ce que dit le manifeste ----
    public readonly List<string> Day = new(), Night = new(), Sporadic = new(), GullsFar = new();
    public double DayGain = 0.55, NightGain = 0.45, SporadicGain = 0.7, GullsGain = 0.5;
    /// <summary>Le silence entre deux coups de la forge, en secondes : tiré entre les deux.</summary>
    public double QuietMin = 25, QuietMax = 80;

    /// <summary>Au-delà, la ville se tait — et ses lecteurs s'arrêtent : rien à décoder pour rien.</summary>
    const double Reach = 1800;
    /// <summary>Le fondu du jour à la nuit et à l'arrivée, en secondes.</summary>
    const double Fade = 4;

    AudioStreamPlayer3D _rumour = null!, _forge = null!, _gulls = null!;
    string _rumourSrc = "";
    double _rumourNow, _gullsNow;
    double _forgeAt = -1;   // l'heure de l'horloge où la forge reprend
    double _clock;
    bool _forgeSaid;
    readonly RandomNumberGenerator _rng = new();

    StreetGrid? _grid;
    Vec3d _centre, _smithy;
    bool _placed;

    public override void _Ready()
    {
        AudioStreamPlayer3D P(float unit, float max) => AddPlayer(new AudioStreamPlayer3D
        {
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,
            UnitSize = unit, MaxDistance = max, MaxDb = 0,
            // l'air mange les aigus d'une ville lointaine comme ceux d'un canon
            AttenuationFilterDb = -18,
            Bus = SoundNode.OutBus
        });
        // la rumeur vient de toute la ville : à cent mètres de son centre, on est dedans
        _rumour = P(110, (float)Reach);
        // une enclume s'entend à trois cents mètres, pas à deux kilomètres
        _forge = P(28, 700);
        // le vol tourne à deux cents mètres de son centre : la source est aussi large
        _gulls = P(90, 1500);
    }

    AudioStreamPlayer3D AddPlayer(AudioStreamPlayer3D p) { AddChild(p); return p; }

    static AudioStream? Open(string file, bool loop)
    {
        string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", file);
        if (!System.IO.File.Exists(path)) { GD.PushWarning($"son absent : {file}"); return null; }
        var s = SoundNode.Read(path);
        if (loop)
        {
            if (s is AudioStreamOggVorbis o) o.Loop = true;
            else if (s is AudioStreamMP3 m) m.Loop = true;
            else if (s is AudioStreamWav w) w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        }
        return s;
    }

    string Pick(List<string> l) => l.Count == 0 ? "" : l[(int)(_rng.Randf() * l.Count) % l.Count];

    /// <param name="here">Le navire, en mètres vrais : il décide de quelle ville on est près.</param>
    /// <param name="origin">L'origine flottante, en mètres vrais.</param>
    /// <param name="gullPort">Le port au-dessus duquel le vol tourne, s'il tourne.</param>
    public void Update(World world, Vec3d here, Vec3d origin, double night, Isle? gullPort, bool on, double dt)
    {
        _clock += dt;
        if (!_placed) Place(world);
        double k = Math.Clamp(dt / Fade, 0, 1);
        bool day = night < 0.5;

        // ---- la rumeur ----
        double dCentre = _grid == null ? double.MaxValue : Math.Sqrt(Sq(_centre.X - here.X) + Sq(_centre.Z - here.Z));
        bool near = on && dCentre < Reach;
        string want = near ? (day ? FirstOf(Day, _rumourSrc) : FirstOf(Night, _rumourSrc)) : "";
        double goal = want.Length == 0 ? 0 : day ? DayGain : NightGain;
        // le jour qui tombe : la rumeur s'éteint d'abord, la suivante monte ensuite
        if (want != _rumourSrc) goal = 0;
        _rumourNow += (goal - _rumourNow) * k;
        if (want != _rumourSrc && _rumourNow < 0.01)
        {
            _rumourSrc = want;
            _rumour.Stop();
            if (want.Length > 0 && Open(want, true) is { } s)
            {
                _rumour.Stream = s;
                GD.Print(FormattableString.Invariant($"ville : {want}, à {dCentre:F0} m du centre"));
                // pas au même endroit de la boucle à chaque arrivée
                _rumour.Play((float)(_rng.Randf() * Math.Max(0, s.GetLength() - 1)));
            }
        }
        // revenue d une sortie au large : la même boucle, qu on avait arrêtée en s éloignant
        else if (want.Length > 0 && want == _rumourSrc && !_rumour.Playing && _rumour.Stream != null)
            _rumour.Play((float)(_rng.Randf() * Math.Max(0, _rumour.Stream.GetLength() - 1)));
        Pose(_rumour, _centre, origin, _rumourNow, goal > 0);

        // ---- la forge : de jour, de temps en temps ----
        if (near && day && Sporadic.Count > 0)
        {
            if (_forgeAt < 0) _forgeAt = _clock + _rng.RandfRange((float)QuietMin * 0.3f, (float)QuietMax * 0.5f);
            if (!_forge.Playing && _clock >= _forgeAt && Open(Pick(Sporadic), false) is { } s)
            {
                _forge.Stream = s;
                if (!_forgeSaid) GD.Print(FormattableString.Invariant($"ville : la forge frappe, à {Math.Sqrt(Sq(_smithy.X - here.X) + Sq(_smithy.Z - here.Z)):F0} m"));
                _forgeSaid = true;
                _forge.Play();
                // le prochain coup après la fin de celui-ci, et un silence tiré
                _forgeAt = _clock + s.GetLength() + _rng.RandfRange((float)QuietMin, (float)QuietMax);
            }
        }
        else if (_forge.Playing && !near) _forge.Stop();
        Pose(_forge, _smithy, origin, SporadicGain, true);

        // ---- les mouettes lointaines, au-dessus du port où tourne le vol ----
        double gGoal = on && gullPort != null && day && GullsFar.Count > 0 ? GullsGain : 0;
        _gullsNow += (gGoal - _gullsNow) * k;
        if (gGoal > 0 && !_gulls.Playing && Open(Pick(GullsFar), true) is { } gs)
        {
            _gulls.Stream = gs;
            GD.Print($"mouettes : au-dessus de {gullPort!.Name}");
            _gulls.Play((float)(_rng.Randf() * Math.Max(0, gs.GetLength() - 1)));
        }
        if (gullPort != null) Pose(_gulls, new Vec3d(gullPort.X, 30, gullPort.Z), origin, _gullsNow, gGoal > 0);
        else Pose(_gulls, Vec3d.Zero, origin, _gullsNow, false);
    }

    static string FirstOf(List<string> l, string playing) => l.Count == 0 ? "" : l.Contains(playing) ? playing : l[0];
    static double Sq(double x) => x * x;

    /// <summary>Le lecteur à sa place contre l'origine, à son volume ; éteint en fondu (<paramref name="rising"/> faux), il s'arrête.</summary>
    static void Pose(AudioStreamPlayer3D p, Vec3d at, Vec3d origin, double gain, bool rising)
    {
        if (gain < 0.003 && !rising) { if (p.Playing && p.VolumeDb < -45) p.Stop(); p.VolumeDb = -60; return; }
        p.Position = new Vector3((float)(at.X - origin.X), (float)at.Y, (float)(at.Z - origin.Z));
        p.VolumeDb = Mathf.LinearToDb((float)Math.Max(gain, 0.001));
    }

    /// <summary>Le centre de la grille des rues, et le pâté le plus proche du quai pour la forge.</summary>
    void Place(World world)
    {
        _placed = true;
        foreach (var g in world.Grids)
        {
            // la ville de départ d'abord : c'est d'elle qu'on a parlé
            var isl = world.ByKey(g.Key);
            if (_grid == null || (isl != null && isl.Start)) _grid = g;
        }
        if (_grid == null) return;
        var port = world.ByKey(_grid.Key);
        double y = 0;
        foreach (var b in _grid.Blocks) y += b.Y;
        _centre = new Vec3d(_grid.Cx, y / Math.Max(1, _grid.Blocks.Count) + 6, _grid.Cz);
        _smithy = _centre;
        double best = double.MaxValue;
        foreach (var b in _grid.Blocks)
        {
            double d = port == null ? 0 : Sq(b.X - port.X) + Sq(b.Z - port.Z);
            if (d < best) { best = d; _smithy = new Vec3d(b.X, b.Y + 2, b.Z); }
        }
    }
}
