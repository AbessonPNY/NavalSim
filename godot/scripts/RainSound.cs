using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// LE BRUIT DE L'AVERSE — sons.json → pluie. Une boucle partout, sans lieu : la
/// pluie tombe de tous les côtés à la fois. Son volume suit ce qui tombe, en fondu ;
/// sur le bus du dehors, si bien qu'une chambre fermée en fait un tambourinement
/// sourd sur le pont au-dessus. La neige ne fait pas de bruit. Liste vide : rien.
/// </summary>
public partial class RainSound : Node
{
    public readonly List<string> Loops = new();
    /// <summary>Son volume au plus fort de l'averse, 0 à 1.</summary>
    public double Gain = 0.8;

    AudioStreamPlayer _p = null!;
    double _now;

    public override void _Ready()
    {
        _p = new AudioStreamPlayer { Bus = SoundNode.OutBus, VolumeDb = -60 };
        AddChild(_p);
    }

    /// <param name="rain">Ce qui tombe en pluie, 0 à 1.</param>
    public void Update(double rain, bool on, double dt)
    {
        double goal = on && Loops.Count > 0 ? Gain * Math.Pow(Math.Clamp(rain, 0, 1), 0.8) : 0;
        // trois secondes pour venir ou partir : une averse ne s'allume pas
        _now += Math.Clamp(goal - _now, -dt / 3, dt / 3);
        if (_now < 0.003)
        {
            if (_p.Playing) _p.Stop();
            return;
        }
        if (!_p.Playing)
        {
            string file = Loops[(int)(GD.Randi() % (uint)Loops.Count)];
            string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", file);
            if (!System.IO.File.Exists(path) || SoundNode.Read(path) is not { } s) { Loops.Remove(file); GD.PushWarning($"son de pluie absent : {file}"); return; }
            if (s is AudioStreamOggVorbis o) o.Loop = true;
            else if (s is AudioStreamMP3 m) m.Loop = true;
            else if (s is AudioStreamWav w) w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            _p.Stream = s;
            _p.Play((float)(GD.Randf() * Math.Max(0, s.GetLength() - 1)));
        }
        _p.VolumeDb = Mathf.LinearToDb((float)_now);
    }
}
