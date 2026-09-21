using Godot;
using System;
using System.Collections.Generic;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LES QUÊTES À L'ÉCRAN : la ligne dorée, les messages, et le fil qu'on suit.
///
/// Les règles sont dans le noyau, vérifiées contre la page par le banc de
/// parité ; il ne reste ici que ce qu'un écran sait faire — écrire l'objectif,
/// poser un message au milieu et attendre qu'on l'ait lu, cercler le lieu sur la
/// carte du capitaine.
///
/// LES FICHES VIVENT HORS DE <c>res://</c>, dans <c>quests/</c>, où la page les
/// lit aussi : un scénario écrit pour l'une vaut pour l'autre, et il n'y a rien
/// à réimporter quand on en ajoute un.
/// </summary>
public partial class ShipDemo : Node3D
{
    Quests? _quests;
    Label? _aimLine;
    PanelContainer? _msgBox;
    Label? _msgTitle, _msgText;
    readonly Queue<(string Title, string Text)> _msgs = new();
    OptionButton? _questPick;

    /// <summary>Où l'on en est, à côté du carnet de la carte et aussi lisible.</summary>
    static string QuestPath => "user://quetes.json";

    // ------------------------------------------------------------------
    //  LIRE LES FICHES
    // ------------------------------------------------------------------

    void LoadQuests()
    {
        if (_world == null) return;
        _quests = new Quests(_world) { OnShow = ShowNotice, OnChange = () => { AimLine(); SaveQuests(); } };
        string dir = System.IO.Path.Combine(WorldLoad.Folder, "quests");
        if (!System.IO.Directory.Exists(dir)) return;

        int kept = 0;
        foreach (string f in System.IO.Directory.GetFiles(dir, "*.json"))
        {
            if (System.IO.Path.GetFileName(f) == "index.json") continue;
            try
            {
                if (_quests.Add(QuestSpec.FromJson(System.IO.File.ReadAllText(f)), GD.PushWarning)) kept++;
            }
            catch (System.Text.Json.JsonException e)
            {
                // une fiche illisible ne doit pas emporter les autres avec elle
                GD.PushWarning($"[quêtes] {System.IO.Path.GetFileName(f)} illisible : {e.Message}");
            }
        }
        if (FileAccess.FileExists(QuestPath))
        {
            using var f = FileAccess.Open(QuestPath, FileAccess.ModeFlags.Read);
            if (f != null && _quests.FromJson(f.GetAsText()) && _quests.Active != null)
                GD.Print($"quête reprise : {_quests.Active.Title}, étape {_quests.Step + 1}");
        }
        GD.Print(FormattableString.Invariant($"{kept} quête(s) lue(s) dans {dir}"));
    }

    void SaveQuests()
    {
        if (_quests == null) return;
        using var f = FileAccess.Open(QuestPath, FileAccess.ModeFlags.Write);
        f?.StoreString(_quests.ToJson());
    }

    // ------------------------------------------------------------------
    //  CE QUE L'ÉCRAN EN MONTRE
    // ------------------------------------------------------------------

    void BuildQuestView(CanvasLayer layer)
    {
        /* LA LIGNE D'OBJECTIF, en doré, sous les instruments : elle se place à
           chaque image d'après la hauteur du bandeau, qui change avec l'état du
           navire — un nombre écrit en dur s'en décrocherait au premier échouage. */
        _aimLine = new Label { Position = new Vector2(18, 200), Visible = false };
        _aimLine.AddThemeFontSizeOverride("font_size", 16);
        _aimLine.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        _aimLine.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _aimLine.AddThemeConstantOverride("outline_size", 5);
        layer.AddChild(_aimLine);

        /* LE CADRE PREND LA HAUTEUR DE CE QU IL DIT : un panneau de hauteur fixe
           laisse un grand vide sous une phrase courte, et coupe une longue. Le
           CenterContainer donne au cadre sa taille minimale, et rien d autre. */
        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(centre);
        _msgBox = new PanelContainer
        {
            CustomMinimumSize = new Vector2(760, 0),
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _msgBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.93f),
            BorderColor = new Color(0.55f, 0.44f, 0.20f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 20, ContentMarginBottom = 20
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        _msgTitle = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _msgTitle.AddThemeFontSizeOverride("font_size", 21);
        _msgTitle.AddThemeColorOverride("font_color", new Color(0.92f, 0.78f, 0.36f));
        _msgText = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _msgText.AddThemeFontSizeOverride("font_size", 16);
        _msgText.AddThemeColorOverride("font_color", new Color(0.93f, 0.92f, 0.88f));
        var more = new Label
        {
            Text = "clic ou Entrée pour continuer",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        more.AddThemeFontSizeOverride("font_size", 12);
        more.AddThemeColorOverride("font_color", new Color(0.62f, 0.60f, 0.55f));
        box.AddChild(_msgTitle); box.AddChild(_msgText); box.AddChild(more);
        _msgBox.AddChild(box);
        centre.AddChild(_msgBox);
        /* UNE QUÊTE PEUT AVOIR COMMENCÉ AVANT CET ÉCRAN : --quete est lu pendant
           que le pont se monte, et son intro attendait alors dans la file sans
           que rien ne l affiche. */
        if (_msgs.Count > 0) NextMessage();
    }

    /* UNE FILE, et non un remplacement : une étape remplie affiche son message
       PUIS la consigne de la suivante, et la fin d'une quête son dernier mot puis
       l'outro. Écraser le premier par le second, c'est perdre la moitié de ce
       qu'on est venu chercher. */
    public void ShowNotice(string title, string text)
    {
        if (text.Length == 0) return;
        _msgs.Enqueue((title, text));
        if (_msgBox != null && !_msgBox.Visible) NextMessage();
    }

    void NextMessage()
    {
        if (_msgBox == null || _msgTitle == null || _msgText == null) return;
        if (_msgs.Count == 0)
        {
            _msgBox.Visible = false;
            AimLine();
            return;
        }
        var (title, text) = _msgs.Dequeue();
        _msgTitle.Text = title;
        _msgTitle.Visible = title.Length > 0;
        _msgText.Text = text;
        _msgBox.Visible = true;
    }

    /// <summary>Vrai si un message est ouvert et a pris l'événement.</summary>
    bool QuestInput(InputEvent e)
    {
        if (_msgBox == null || !_msgBox.Visible) return false;
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed) { NextMessage(); return true; }
        if (e is InputEventKey k && k.Pressed && !k.Echo)
            switch (k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode)
            {
                case Key.Enter or Key.KpEnter or Key.Space or Key.Escape: NextMessage(); return true;
            }
        return false;
    }

    /// <summary>
    /// LA LIGNE DORÉE : où aller, à quelle distance et par quel cap — et pour les
    /// objectifs qui se comptent, où l'on en est de ce compte.
    /// </summary>
    void AimLine()
    {
        /* AVANT MÊME QUE LA COQUE SOIT À L EAU : la reprise d une quête
           enregistrée arrive pendant que le pont se monte, et il n y a alors ni
           navire ni mer à interroger. La ligne se réécrira à la première image. */
        if (_aimLine == null || _quests == null || _ship == null) return;
        var wo = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        // compté depuis où l'on CROIT être : le pilote n'en sait pas davantage
        var (ax, az) = Believed();
        var aim = _quests.Aim(ax, az);
        // une quête d'une autre carte attend qu'on y retourne : on dit où
        if (aim == null && !_quests.HereNow && _quests.Current is QuestStep far && (_msgBox == null || !_msgBox.Visible))
        {
            _aimLine.Text = $"{(far.Title.Length > 0 ? far.Title : "Quête")} — dans les eaux de {RegionName(_quests.Active!.Region)}";
            _aimLine.Visible = _info.Visible;
            _aimLine.Position = new Vector2(18, _info.Position.Y + _info.Size.Y + 10 + TrimHeight);
            return;
        }
        if (aim == null || (_msgBox != null && _msgBox.Visible))
        {
            _aimLine.Visible = false;
            return;
        }
        var a = aim.Value;
        // les mêmes mots que la page : un joueur qui passe de l'une à l'autre lit la même ligne
        string tenir = a.Step.Goal == Goal.Stop && a.Hold > 0
            ? FormattableString.Invariant($" · {Math.Ceiling((a.Step.Hold ?? 8) - a.Hold):F0} s") : "";
        string where = a.Step.Goal == Goal.Leave
            // s'éloigner : le chemin déjà fait, contre celui qu'il faut faire
            ? $"{Mille(a.Dist)} sur {Mille(a.R)}"
            : a.Dist <= a.R ? "vous y êtes" + tenir
            : FormattableString.Invariant($"{Mille(a.Dist)} au {(int)Math.Round(a.Bearing) % 360:D3}°");
        string title = a.Step.Title.Length > 0 ? a.Step.Title : $"Étape {a.Index + 1}";
        _aimLine.Text = $"{title} — {where}   ({a.Index + 1}/{a.Count})";
        _aimLine.Visible = _info.Visible;
        // sous le bandeau ET sous le curseur d écoute, quelles que soient leurs hauteurs
        _aimLine.Position = new Vector2(18, _info.Position.Y + _info.Size.Y + 10 + TrimHeight);
    }

    /* EN MÈTRES SOUS LE DEMI-MILLE, en milles au-delà : un capitaine ne dit pas
       « 0,2 mille », il dit « deux cents mètres ». La bascule est celle de la
       page, au mètre près. */
    static string Mille(double d) => d < 926
        ? FormattableString.Invariant($"{Math.Round(d / 10) * 10:F0} m")
        : (d / 1852).ToString("F1", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " M";

    /// <summary>À chaque image : ce que les quêtes regardent du navire, et rien de plus.</summary>
    void QuestTick(double dt)
    {
        if (_quests == null || _world == null) return;
        var wo = _sea.Core.Origin;
        var b = _ship.Physics.Body;
        var p = _ship.Physics;
        _quests.Update(dt, new QuestState(
            wo.X + b.Pos.X, wo.Z + b.Pos.Z,
            // la vitesse SUR L'EAU, sans le pilonnement : une coque qui monte à
            // la lame ne s'en va nulle part, et « en panne » ne doit pas l'exclure
            Math.Sqrt(b.Vel.X * b.Vel.X + b.Vel.Z * b.Vel.Z),
            // amarré au ponton OU sur son ancre : les deux tiennent le navire
            p.Moorings.Count > 0,
            p.Foundered));
        AimLine();
    }

    /// <summary>Le lieu de l'étape, pour le cercle doré de la carte.</summary>
    (double X, double Z, double R, string Name)? QuestPlace()
    {
        var a = _quests?.Aim(0, 0);
        return a == null ? null : (a.Value.X, a.Value.Z, a.Value.R, a.Value.Step.Title);
    }

    /// <summary>
    /// SAUTER AU LIEU DE L ÉTAPE — le pendant de <c>allerQuete()</c> dans la
    /// console de la page, et le seul moyen d essayer la fin d un scénario sans
    /// refaire tout le chemin. Devant la tête du ponton, pas dessus : on ne se
    /// matérialise pas à couple.
    /// </summary>
    void GoToStep()
    {
        if (_quests?.Current is not QuestStep step || _world == null) return;
        var p = _quests.Place(step);
        double x = p.X, z = p.Z;
        if (_world.ByKey(step.At.Port) is Isle isl && !step.At.Offset)
        {
            x += Math.Cos(isl.Port.Ang) * 200;
            z += Math.Sin(isl.Port.Ang) * 200;
        }
        var o = _sea.Core.Origin;
        _sea.Core.Rebase(x - o.X, z - o.Z);
        var b = _ship.Physics.Body;
        b.Pos = new Vec3d(0, b.Pos.Y, 0);
        b.Vel = new Vec3d(0, 0, 0);
        b.AngVel = new Vec3d(0, 0, 0);
        _ship.SyncTransform();
        Say($"Vous voici : {(step.Title.Length > 0 ? step.Title : $"étape {_quests.Step + 1}")}.");
    }

    /// <summary>Mode libre, ou l'une des quêtes lues.</summary>
    void PickQuest(int i)
    {
        if (_quests == null) return;
        _msgs.Clear();
        if (_msgBox != null) _msgBox.Visible = false;
        if (i <= 0) _quests.Stop();
        else _quests.Start(_quests.List[i - 1].Id);
        SaveQuests();
    }
}
