using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LE FLOU DE MOUVEMENT — Godot 4.7 n'en a pas ; il s'écrit en passe plein écran
/// (<see cref="ScreenEffect"/>).
///
/// Un flou de CAMÉRA, par reprojection : chaque pixel retrouve sa position par
/// la profondeur et la compare à la caméra de l'image précédente. Les vecteurs de
/// mouvement du moteur ne serviraient à rien ici : la mer est déformée dans son
/// shader et transparente, ils ne la voient pas bouger. Ce qui est à bord d'un
/// navire est ramené avec LUI (voir <see cref="AddShip"/>) : la coque que la
/// caméra suit, et le pont d'une vue à bord, restent nets.
///
/// La part d'obturateur est un 180° de cinéma par défaut (0,5) : le flou est le
/// déplacement pendant la moitié d'une image.
/// </summary>
public partial class MotionBlurEffect : ScreenEffect
{
    public float Shutter = 0.5f;
    public int Samples = 8;
    public float MaxLength = 0.06f;       // fraction de l'écran

    // std140 : voir Params dans motion_blur.glsl
    public const int MaxShips = 8;
    const int OffMisc = 192, OffToLocal = 208, OffPrevModel = OffToLocal + 64 * MaxShips,
              OffMin = OffPrevModel + 64 * MaxShips, OffMax = OffMin + 16 * MaxShips, Size = OffMax + 16 * MaxShips;

    /* LES NAVIRES, écrits par la démo sur le fil principal, lus ici sur celui du
       rendu. Chaque place garde la transformée de l'image d'avant : c'est d'elle
       qu'un point du pont venait. */
    readonly object _shipLock = new();
    readonly Transform3D[] _shipCur = new Transform3D[MaxShips], _shipPrev = new Transform3D[MaxShips];
    readonly Aabb[] _shipBox = new Aabb[MaxShips];
    readonly bool[] _shipHasPrev = new bool[MaxShips];
    int _shipCount, _shipFill;

    Projection _prevVP;
    bool _hasPrev;

    /* LA CONVENTION, VÉRIFIÉE À CHAQUE LANCEMENT. Godot passe à l'effet la projection
       DÉJÀ CORRIGÉE pour Vulkan — constaté : y à −1,92 là où la caméra a 1,92, et
       la profondeur inversée (5,5e−6 et 0,05 au lieu de −1,00001 et −0,1). La
       première écriture la corrigeait une seconde fois. On s'en sert donc telle
       quelle, et l'effet vérifie qu'elle vaut bien Correction × celle de Camera3D,
       posée par la démo sur le fil principal : si Godot changeait de convention, le
       flou partirait de travers et on le saurait. */
    public Projection? MainProjection;
    bool _checked;

    /* LA CORRECTION DE VULKAN, que Godot pose sur la projection de la caméra : y
       retourné (l'image a son origine en haut), profondeur ramenée à 0..1 ET
       INVERSÉE (1 au plan proche, 0 au lointain). */
    public static readonly Projection Correction = new(
        new Vector4(1, 0, 0, 0),
        new Vector4(0, -1, 0, 0),
        new Vector4(0, 0, -0.5f, 0),
        new Vector4(0, 0, 0.5f, 1));

    public MotionBlurEffect() : base("res://shaders/motion_blur.glsl", "naval_flou", Size) { }

    /// <summary>Une image : <see cref="AddShip"/> pour chaque navire, puis <see cref="EndShips"/>.</summary>
    public void BeginShips() => _shipFill = 0;

    /// <summary>Un navire, sa transformée de cette image et sa boîte dans son repère.</summary>
    public void AddShip(Transform3D xf, Aabb localBox)
    {
        if (_shipFill >= MaxShips) return;
        lock (_shipLock)
        {
            int i = _shipFill++;
            // la place a changé de navire (flotte recomposée) : pas d'image d'avant
            _shipHasPrev[i] = i < _shipCount && _shipBox[i] == localBox;
            _shipPrev[i] = _shipHasPrev[i] ? _shipCur[i] : xf;
            _shipCur[i] = xf;
            _shipBox[i] = localBox;
        }
    }

    public void EndShips() { lock (_shipLock) _shipCount = _shipFill; }

    protected override bool Prepare(RenderSceneData sd, Vector2I size)
    {
        Projection raw = sd.GetCamProjection();
        if (!_checked && MainProjection is Projection mp)
        {
            _checked = true;
            float e = 0;
            Projection want = Correction * mp;
            // sauf le décalage sous-pixel du TAA, que Godot ajoute à la seule projection du rendu
            for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++)
                if (c < 2 || r >= 2) e = Math.Max(e, Math.Abs(raw[c][r] - want[c][r]));
            GD.Print($"flou de mouvement : projection reçue {(e < 1e-4f ? "= correction Vulkan × caméra, comme attendu" : $"INATTENDUE (écart {e:E1}) — convention à revoir")}");
        }
        Projection P = raw;                  // déjà corrigée : voir plus haut
        Transform3D cam = sd.GetCamTransform();
        Projection VP = P * new Projection(cam.AffineInverse());
        if (!_hasPrev) { _prevVP = VP; _hasPrev = true; return false; }

        Put(0, P.Inverse());
        Put(64, new Projection(cam));
        Put(128, _prevVP);
        Float(OffMisc, Shutter);
        Float(OffMisc + 4, Samples);
        Float(OffMisc + 8, MaxLength);
        lock (_shipLock)
        {
            Float(OffMisc + 12, _shipCount);
            for (int i = 0; i < _shipCount; i++)
            {
                Put(OffToLocal + 64 * i, new Projection(_shipCur[i].AffineInverse()));
                Put(OffPrevModel + 64 * i, new Projection(_shipPrev[i]));
                Aabb b = _shipBox[i];
                Col(OffMin + 16 * i, new Vector4(b.Position.X, b.Position.Y, b.Position.Z, 0));
                Col(OffMax + 16 * i, new Vector4(b.End.X, b.End.Y, b.End.Z, 0));
            }
        }
        _prevVP = VP;
        return true;
    }
}
