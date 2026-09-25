using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE JOURNAL DE BORD — la plume du capitaine, et le peu que le bord écrit pour
/// lui.
///
/// Il vit à côté de la carte et non dedans : la carte est un PLAN (des traits
/// posés sur des coordonnées du monde), le journal une SUITE DE FEUILLES où le
/// texte coule et où les croquis s'intercalent. Deux besoins, deux modèles — les
/// mêler aurait donné un objet qui ne sait faire ni l'un ni l'autre.
///
/// Ce fichier tient la page ouverte à l'écran, la frappe et la plume. Le modèle
/// est dans <see cref="Journal"/>, le dessin dans <see cref="JournalNode"/>.
/// </summary>
public partial class ShipDemo : Node3D
{
    Journal _journal = new();
    JournalNode? _journalNode;
    CanvasLayer? _journalLayer;
    TextureRect? _journalView;
    Label? _journalHint;
    bool _journalOpen;
    bool _journalPen;                 // la plume est posée : on trace
    string _portWas = "";             // le port où l'on était, pour la ligne d'escale

    void BuildJournal()
    {
        _journalNode = new JournalNode(_journal);
        AddChild(_journalNode);

        _journalLayer = new CanvasLayer { Layer = 8, Visible = false };
        AddChild(_journalLayer);
        var fond = new ColorRect
        {
            AnchorRight = 1, AnchorBottom = 1,
            Color = new Color(0.04f, 0.04f, 0.05f, 0.92f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _journalLayer.AddChild(fond);
        _journalView = new TextureRect
        {
            Texture = _journalNode.Texture,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _journalLayer.AddChild(_journalView);
        _journalHint = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _journalHint.AddThemeFontSizeOverride("font_size", 15);
        _journalHint.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.58f));
        if (HandFont.Get() is { } hand) _journalHint.AddThemeFontOverride("font", hand);
        _journalLayer.AddChild(_journalHint);
    }

    void LayoutJournal()
    {
        if (_journalView == null || _journalHint == null) return;
        var r = GetViewport().GetVisibleRect().Size;
        float h = r.Y * 0.92f;
        _journalView.Position = new Vector2((r.X - h * 1100 / 1500) * 0.5f, r.Y * 0.02f);
        _journalView.Size = new Vector2(h * 1100 / 1500, h);
        _journalHint.Position = new Vector2(0, r.Y - 30);
        _journalHint.Size = new Vector2(r.X, 24);
        string ou = _journal.Pages.Count > 0
            ? $"page {_journal.At + 1} sur {_journal.Pages.Count}  ·  " : "";
        _journalHint.Text = ou + (_journalNode?.OpenSketch != null
            ? "Croquis : tracez à la souris · E change d'encre · Tab referme le croquis · Échap ferme"
            : "Écrivez · Tab : croquis · ← → : tourner · ⇧Entrée : page neuve · molette : défiler · Échap : fermer");
    }

    /// <summary>La date du bord, écrite comme on l'écrivait, et sa forme rangeable.</summary>
    (string Dit, string Clef) Today()
    {
        var d = _calendar.Date;
        string[] mois = { "janvier", "février", "mars", "avril", "mai", "juin", "juillet",
                          "août", "septembre", "octobre", "novembre", "décembre" };
        return ($"{d.Day} {mois[d.Month - 1]} {d.Year}",
                d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    void ToggleJournal()
    {
        if (_journalLayer == null || _journalNode == null || _inTitle) return;
        _journalOpen = !_journalLayer.Visible;
        _journalLayer.Visible = _journalOpen;
        if (!_journalOpen) { _journalPen = false; return; }
        /* ON L'OUVRE SUR LE JOUR : s'il n'y a pas de page à cette date, on en prend
           une neuve. C'est le seul endroit où une page naît de la main du joueur —
           les lignes du bord, elles, prennent celle du jour ou la créent. */
        var (dit, clef) = Today();
        _journal.Today(dit, clef);
        _journalNode.Scroll = 0;
        _journalNode.Refresh();
        LayoutJournal();
    }

    /// <summary>
    /// UNE LIGNE DU BORD. Appareillage, mouillage, traversée : juste assez pour
    /// qu'un journal négligé garde la trace du voyage, et assez peu pour qu'il
    /// reste celui du capitaine.
    /// </summary>
    public void JournalLog(string line)
    {
        var (dit, clef) = Today();
        _journal.Log(dit, clef, line);
        _journalNode?.Refresh();
    }

    /// <summary>
    /// LES ESCALES, vues du bandeau du comptoir : c'est lui qui sait déjà si l'on
    /// est devant un port, et l'y demander évite un second calcul qui pourrait en
    /// dire autre chose.
    /// </summary>
    void JournalPortTick()
    {
        string now = _portHere?.Name ?? "";
        if (now == _portWas) return;
        if (now.Length > 0) JournalLog($"Mouillé à {now}.");
        else if (_portWas.Length > 0) JournalLog($"Appareillé de {_portWas}.");
        _portWas = now;
    }

    /// <summary>La frappe et la plume, tant que la page est ouverte. Vrai si l'on a pris l'événement.</summary>
    bool JournalInput(InputEvent e)
    {
        if (!_journalOpen || _journalNode == null || _journalView == null) return false;

        if (e is InputEventKey k && k.Pressed)
        {
            switch (k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode)
            {
                case Key.Escape: ToggleJournal(); return true;
                case Key.Tab: _journalNode.Sketch(); LayoutJournal(); return true;
                case Key.Backspace: _journalNode.Back(); return true;
                case Key.Enter:
                case Key.KpEnter:
                    /* ⇧ENTRÉE OUVRE UNE PAGE NEUVE, à la date du jour. « Des pages
                       libres » veut dire qu'on peut en tourner une quand on veut,
                       et pas seulement quand le calendrier l'a décidé. */
                    if (k.ShiftPressed) { var (d2, c2) = Today(); _journal.NewPage(d2, c2); _journalNode.Scroll = 0; }
                    else _journalNode.Type("\n");
                    _journalNode.Refresh();
                    LayoutJournal();
                    return true;
                /* TOURNER. Les flèches, parce qu'il n'y a rien d'autre à en faire
                   dans une page dont la plume est toujours au bout : le jour où le
                   curseur se déplacera dans le texte, il faudra les lui rendre et
                   prendre Page précédente / Page suivante. */
                case Key.Left:
                case Key.Pageup:
                    if (_journal.Turn(-1)) { _journalNode.Scroll = 0; _journalNode.Refresh(); LayoutJournal(); }
                    return true;
                case Key.Right:
                case Key.Pagedown:
                    if (_journal.Turn(+1)) { _journalNode.Scroll = 0; _journalNode.Refresh(); LayoutJournal(); }
                    return true;
            }
            /* E CHANGE D'ENCRE, MAIS SEULEMENT LA PLUME À LA MAIN : en écriture, E
               est une lettre, et rien n'est plus agaçant qu'une touche qui fait
               deux choses selon un état qu'on ne voit pas. */
            if (_journalNode.OpenSketch != null && k.Keycode == Key.E)
            { _journalNode.Ink = (_journalNode.Ink + 1) % 3; LayoutJournal(); return true; }
            /* ET LE RESTE EST DU TEXTE, pris à l'UNICODE et non au code de touche :
               c'est la seule façon d'écrire « é » sur un clavier français sans
               réécrire la disposition. */
            if (k.Unicode >= 32)
            { _journalNode.Type(char.ConvertFromUtf32((int)k.Unicode)); return true; }
            return true;                          // rien ne passe derrière une page ouverte
        }

        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && mb.Pressed)
            {
                float pas = mb.ButtonIndex == MouseButton.WheelUp ? -90 : 90;
                _journalNode.Scroll = Math.Clamp(_journalNode.Scroll + pas, 0,
                    Math.Max(0, _journalNode.Written - 1500 + 120));
                _journalNode.Refresh();
                return true;
            }
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _journalPen = mb.Pressed && _journalNode.OpenSketch != null;
                if (_journalPen && PageUv(mb.Position) is { } uv0 && _journalNode.InSketch(uv0) is { } p0)
                {
                    var s = new PageStroke { Ink = _journalNode.Ink };
                    s.Pts.Add(p0);
                    _journalNode.OpenSketch!.Strokes.Add(s);
                    _journalNode.Refresh();
                }
                return true;
            }
            return true;
        }

        if (e is InputEventMouseMotion mm && _journalPen)
        {
            if (_journalNode.OpenSketch is { } b && b.Strokes.Count > 0
                && PageUv(mm.Position) is { } uv && _journalNode.InSketch(uv) is { } p)
            {
                var pts = b.Strokes[^1].Pts;
                var last = pts[^1];
                // un point tous les quelques millièmes de page : la plume suit sans noyer le fichier
                if ((p.X - last.X) * (p.X - last.X) + (p.Y - last.Y) * (p.Y - last.Y) > 4e-5)
                { pts.Add(p); _journalNode.Refresh(); }
            }
            return true;
        }
        return false;
    }

    /// <summary>Où tombe un point de l'écran sur la feuille, de 0 à 1 — nul s'il est dehors.</summary>
    Vector2? PageUv(Vector2 at)
    {
        if (_journalView == null) return null;
        var r = _journalView.GetGlobalRect();
        if (!r.HasPoint(at)) return null;
        return (at - r.Position) / r.Size;
    }
}
