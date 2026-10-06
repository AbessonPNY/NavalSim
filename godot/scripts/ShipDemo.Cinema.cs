using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE MODE CINÉMA — é (le 2 de la rangée du haut en AZERTY), ou le 2 du pavé
/// numérique : le drone, le masque 2,35 et une mise au point SUR LE NAVIRE.
/// Le même appui rend la vue d'avant.
///
/// RIEN N'EST ÉCRIT DANS LES RÉGLAGES. Le flou et le masque sont posés
/// PAR-DESSUS ce que dit reglages.ini, tant que le mode dure : un passage au
/// menu pendant le cinéma n'y enregistrerait pas une profondeur de champ de
/// cinéaste à la place de la sienne, et l'en sortir rend exactement ce qu'on
/// avait.
///
/// LA MISE AU POINT SUIT LE NAVIRE à chaque image, comme un pointeur qui tire le
/// point : la zone nette est la distance du drone à la coque, de sept dixièmes de
/// longueur de part et d'autre. Le navire est net d'un bout à l'autre ; la mer au
/// premier plan et l'horizon s'en vont dans le flou — c'est ce qui sépare le
/// sujet du décor, et ce qu'un objectif serré fait de lui-même.
/// </summary>
public partial class ShipDemo
{
    bool _cine;
    int _cineMode, _cineDeck;
    /* CE QUE MONTRAIENT H ET ⇧H AVANT LE FILM — les instruments, puis le reste
       (soleil, météo, bourse, comptoir) : le cinéma les cache et les rend tels
       qu'ils étaient, pas tous allumés. */
    bool _cineInfo, _cineHud;

    void CineHide()
    {
        _cineInfo = _info.Visible; _cineHud = _hudOn;
        _info.Visible = false;
        _hudOn = false; _sunPanel.Visible = false;
    }

    void CineShow()
    {
        _info.Visible = _cineInfo;
        _hudOn = _cineHud; _sunPanel.Visible = _cineHud;
    }

    /// <summary>Le masque 2,35, qu'il soit réglé ou imposé par le cinéma.</summary>
    bool FilmMaskOn => _settings?.FilmMask == true || _cine || _film != null;

    void ToggleCinema()
    {
        if (!_cine)
        {
            if (_glassUp) { Say("Baissez d abord la lunette"); return; }
            _cineMode = _camMode; _cineDeck = _deck;
            SetGunPost(null);
            DryLens();
            if (_camMode != CamFlyBy) EnterFlyBy();
            _cine = true;
            CineHide();
            ApplySettings();
        }
        else
        {
            _cine = false;
            _duelFoe = null; _battleUntil = -1;
            CineShow();
            // la vue d'avant, par le même chemin que la touche C
            switch (_cineMode)
            {
                case 1: _camMode = 1; _deck = Math.Clamp(_cineDeck, 0, Math.Max(0, _ship.Spec.Decks.Count - 1)); EnterDeck(); break;
                case 2: _camMode = 2; SetLens(OutsideFov, OutsideNear); Plant(); break;
                case 3: _camMode = 3; SetLens(OutsideFov, OutsideNear); break;
                case 4 when _jettyHeld != null: _camMode = 4; SetLens(OutsideFov, OutsideNear); break;
                case CamFlyBy: break;
                default: _camMode = 0; SetLens(OutsideFov, OutsideNear); break;
            }
            ApplySettings();
            Say("Fin du cinéma");
        }
        UpdateInfo();
    }

    /// <summary>Quitter le cinéma sans rendre la vue : C a déjà choisi la suivante.</summary>
    void LeaveCinema()
    {
        if (!_cine) return;
        _cine = false;
        _duelFoe = null; _battleUntil = -1;
        CineShow();
        ApplySettings();
    }

    /// <summary>
    /// La mise au point de l'image, après que la vue a posé la caméra. Posée sur
    /// les attributs de la caméra et non dans les réglages — voir plus haut.
    /// </summary>
    void CineFocus()
    {
        if (!_cine || _glassUp) return;
        // au contrechamp de bataille, le point est sur L'AUTRE : le nôtre, au premier plan, part dans le flou
        var focus = _duelFoe != null && IsInstanceValid(_duelFoe) ? _duelFoe : _ship;
        double L = focus.Spec.L;
        var mid = focus.Position + new Vector3(0, (float)(0.15 * L), 0);
        float d = _cam.GlobalPosition.DistanceTo(mid);
        float span = (float)(0.7 * L);
        float near = Math.Max(0.5f, d - span), far = d + span;
        _anamorphic.Enabled = false;
        _camAttr.DofBlurNearEnabled = true;
        _camAttr.DofBlurNearDistance = near;
        _camAttr.DofBlurNearTransition = Math.Max(0.5f, near * 0.5f);
        _camAttr.DofBlurFarEnabled = true;
        _camAttr.DofBlurFarDistance = far;
        _camAttr.DofBlurFarTransition = Math.Max(1f, far * 0.6f);
        _camAttr.DofBlurAmount = 0.10f;
    }
}
