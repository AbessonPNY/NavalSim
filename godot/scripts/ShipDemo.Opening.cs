using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'OUVERTURE DU JEU LIBRE — un plan d'établissement, comme au cinéma, avant
/// qu'on prenne la barre (demandé) :
///  1. LA MONTÉE : l'œil, au ras de l'eau près d'un navire au mouillage au milieu
///     de la rade, monte en reculant ; la ville se découvre derrière lui.
///  2. LES QUAIS ET LES FORTS : il glisse le long des pontons du chenal, puis
///     passe au-dessus des forts.
///  3. LE DÉPART : un navire des horaires quitte la rade pour le large ; l'œil
///     passe par-dessus lui et regarde où il va.
///  4. LE TITRE : l'œil s'arrête sur son erre, le logo paraît, et la partie
///     commence.
///
/// Tout est dans <c>world/ouverture.json</c> — durées, fondus, points du chemin —
/// pour se caler sur la musique sans recompiler. Le monde tourne pendant le
/// film : c'est la partie elle-même qu'on regarde, déjà commencée, et la rendre
/// au joueur n'a qu'à rendre la caméra. Échap, Entrée ou Espace la passent.
/// </summary>
public partial class ShipDemo
{
    sealed class OpeningShot
    {
        public string Name = "", Type = "";
        public double Dur = 10, Fade;
        // la montée
        public double X, Z, Tx, Tz, H0, H1, R0, R1;
        // le chemin
        public readonly List<Vector3> Path = new(), Look = new();
        // le départ
        public double Already = 0.35, Back = -70, BackUp = 28, Ahead = 90, AheadUp = 42, Side = 12;
        // le titre
        public string Logo = "", Text = "", Size = "";
        public double Width = 0.55;
    }

    sealed class Opening
    {
        public string Port = "", Music = "";
        public double Volume = 1, Guard = 12;
        public bool ToTheEnd = true;
        public readonly List<OpeningShot> Shots = new();
        // l'état du film
        public int Shot = -1;
        public double T;
        public ShipNode? Leaver;
        public Vector3 EyeVel, LastEye;
        public bool HasLast;
        public Control? Logo;
        public (Vector3[] P, double[] S)? Curve, LookCurve;
    }

    Opening? _opening;
    /// <summary>L'instant (réel, en secondes) où le thème de l'ouverture finit ; il joue jusque-là.</summary>
    double _themeEnd = -1;
    string _themeFile = "";
    double _themeGain = 1;

    /// <summary>Un film se joue — celui d'un chapitre ou l'ouverture : la mer et ses rencontres se taisent.</summary>
    bool Filming => _film != null || _opening != null;

    /* ------------------------------------------------------------------ */
    /*  LIRE                                                               */
    /* ------------------------------------------------------------------ */

    static Opening? ReadOpening()
    {
        string path = System.IO.Path.Combine(WorldLoad.Folder, "world", "ouverture.json");
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var r = doc.RootElement;
            var o = new Opening
            {
                Port = r.Str("port"), Music = r.Str("musique"), Volume = r.Num("volume", 1),
                Guard = r.Num("garde", 12),
                ToTheEnd = !r.TryGetProperty("jusquauBout", out var tb) || tb.ValueKind != JsonValueKind.False
            };
            if (!r.TryGetProperty("plans", out var plans)) return o;
            foreach (var p in plans.EnumerateArray())
            {
                var s = new OpeningShot
                {
                    Name = p.Str("nom"), Type = p.Str("type"), Dur = Math.Max(1, p.Num("duree", 10)), Fade = p.Num("fondu"),
                    X = p.Num("x"), Z = p.Num("z"),
                    Already = p.Num("deja", 0.35), Side = p.Num("cote", 12),
                    Logo = p.Str("logo"), Text = p.Str("texte"), Width = p.Num("taille", 0.55)
                };
                (double, double) Pair(string k, double a, double b) =>
                    p.TryGetProperty(k, out var v) && v.GetArrayLength() >= 2 ? (v[0].GetDouble(), v[1].GetDouble()) : (a, b);
                (s.Tx, s.Tz) = Pair("vers", s.X, s.Z);
                (s.H0, s.H1) = Pair("hauteur", 5, 300);
                (s.R0, s.R1) = Pair("recul", 30, 400);
                (s.Back, s.BackUp) = Pair("de", -70, 28);
                (s.Ahead, s.AheadUp) = Pair("a", 90, 42);
                void Points(string k, List<Vector3> into)
                {
                    if (!p.TryGetProperty(k, out var v)) return;
                    foreach (var q in v.EnumerateArray())
                        into.Add(new Vector3((float)q[0].GetDouble(), (float)q[1].GetDouble(), (float)q[2].GetDouble()));
                }
                Points("chemin", s.Path);
                Points("regard", s.Look);
                o.Shots.Add(s);
            }
            return o;
        }
        catch (Exception e)
        {
            GD.PushWarning($"[ouverture] world/ouverture.json illisible : {e.Message}");
            return null;
        }
    }

    /* ------------------------------------------------------------------ */
    /*  LANCER                                                             */
    /* ------------------------------------------------------------------ */

    /// <summary>
    /// L'ouverture d'une partie neuve en jeu libre, si le port de départ en a une.
    /// Faux sans elle : l'appelant entre dans le jeu tout de suite.
    /// </summary>
    bool BeginOpening()
    {
        if (_noFilm || !_booted || _world == null) return false;
        var o = ReadOpening();
        if (o == null || o.Shots.Count == 0) return false;
        if (o.Port.Length > 0 && _world.StartPort?.Key != o.Port) return false;
        _opening = o;
        _inTitle = false;
        if (_titleLayer != null) _titleLayer.Visible = false;
        CineHide();
        _note.Visible = false;
        EnsureFilmLayer();
        // la mise au point du joueur, pas celle de l'affiche (le lointain y était flou)
        ApplySettings();
        _filmBlack = 1;
        if (_filmFade != null) _filmFade.Color = new Color(0, 0, 0, 1);
        o.Leaver = LaunchLeaver(o);
        if (o.Music.Length > 0) StartTheme(o.Music, o.Volume, o.ToTheEnd);
        StartShot(o, 0);
        return true;
    }

    /* LE THÈME : il joue pendant le film, et — « jusquauBout » — jusqu'à la fin du
       morceau une fois la partie commencée, même la musique d'ambiance coupée : il
       appartient à l'ouverture, pas à l'ambiance. Sa longueur se lit sur le fichier. */
    void StartTheme(string file, double gain, bool toTheEnd)
    {
        string path = System.IO.Path.Combine(WorldLoad.Folder, "medias", "sound", file);
        if (!System.IO.File.Exists(path)) { GD.PushWarning($"[ouverture] musique absente : {file}"); return; }
        _themeFile = file; _themeGain = gain;
        double len = 0;
        if (toTheEnd)
        {
            AudioStream? st = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioStreamMP3.LoadFromFile(path)
                            : path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioStreamOggVorbis.LoadFromFile(path) : null;
            len = st?.GetLength() ?? 0;
        }
        // rendue deux secondes avant la fin : le fondu de sortie couvre la reprise de la boucle
        _themeEnd = len > 4 ? Time.GetTicksMsec() / 1000.0 + len - 2 : -1;
        GD.Print(FormattableString.Invariant($"[ouverture] thème : {file}{(len > 0 ? $", {len:F1} s" : "")}"));
    }

    /// <summary>Le thème de l'ouverture, s'il doit jouer encore : son fichier, sinon rien.</summary>
    string? ThemeNow()
    {
        if (_themeFile.Length == 0) return null;
        // revenu au titre, la partie est finie : son thème aussi
        if (_inTitle) { _themeFile = ""; return null; }
        if (_opening != null || Time.GetTicksMsec() / 1000.0 < _themeEnd) return _themeFile;
        _themeFile = "";
        return null;
    }

    /// <summary>
    /// LE NAVIRE QUI PART : un départ des horaires vers le large, mis en route dès
    /// le début du film et déjà à « deja » de sa route, pour qu'il soit en pleine
    /// rade, sous voiles, quand vient son plan. Il appartient ensuite à la rade
    /// comme les autres (horaires, pilote, retrait passé l'horizon).
    /// </summary>
    ShipNode? LaunchLeaver(Opening o)
    {
        var plan = o.Shots.Find(s => s.Type == "depart");
        if (plan == null || _world?.StartPort is not Isle port) return null;
        if (!_timetableRead) _timetable = LoadTimetable();
        if (_timetable == null || !_timetable.Ports.TryGetValue(port.Key, out var pt)) return null;
        Sailing? pick = null;
        foreach (var sg in pt.Sailings)
        {
            if (sg.Arrival || sg.Other != "large") continue;
            // celui qui va le plus loin d'abord : il fait route droit vers le large
            if (pick == null || (pick.Beyond.Length == 0 && sg.Beyond.Length > 0)) pick = sg;
        }
        if (pick == null) { GD.Print("[ouverture] aucun départ vers le large aux horaires : le plan suit votre navire"); return null; }
        int before = _others.Count;
        Launch(port, pt, pick, 0, _sky.Core.DayTime, frac: Math.Clamp(plan.Already, 0.02, 0.95), seen: true);
        if (_others.Count <= before) { GD.Print("[ouverture] le départ n'a pas pu paraître : le plan suit votre navire"); return null; }
        return _others[^1];
    }

    void StartShot(Opening o, int i)
    {
        o.Shot = i; o.T = 0;
        if (i >= o.Shots.Count) { EndOpening(); return; }
        var s = o.Shots[i];
        o.Curve = s.Path.Count >= 2 ? ArcCurve(s.Path) : null;
        o.LookCurve = s.Look.Count >= 2 ? ArcCurve(s.Look) : null;
        if (s.Type == "titre") ShowLogo(o, s);
        GD.Print(FormattableString.Invariant($"[ouverture] plan {i + 1} : {s.Name} ({s.Dur:F0} s)"));
    }

    /* ------------------------------------------------------------------ */
    /*  À CHAQUE IMAGE                                                     */
    /* ------------------------------------------------------------------ */

    void OpeningTick(double dt)
    {
        var o = _opening!;
        o.T += dt;
        var s = o.Shots[o.Shot];
        double u = Math.Clamp(o.T / s.Dur, 0, 1);
        Vector3 eye, look;
        switch (s.Type)
        {
            case "montee": (eye, look) = RiseShot(s, u); break;
            case "chemin": (eye, look) = PathShot(o, u); break;
            case "depart": (eye, look) = LeaverShot(o, s, u); break;
            default: (eye, look) = HoldShot(o, s, dt); break;
        }
        eye = AboveGround(eye, o.Guard);
        bool cut = o.T <= dt * 1.01;
        if (!cut && o.HasLast && dt > 0) o.EyeVel = o.EyeVel.Lerp((eye - o.LastEye) / (float)dt, (float)Math.Min(1, dt * 4));
        if (cut && s.Type != "titre") o.EyeVel = Vector3.Zero;
        o.LastEye = eye; o.HasLast = true;
        _fixEye = eye; _fixLook = look;

        // le noir : il s'ouvre sur le fondu du plan, et se ferme à demi-fondu du suivant
        double black = s.Fade > 0 ? 1 - o.T / s.Fade : 0;
        double next = o.Shot + 1 < o.Shots.Count ? o.Shots[o.Shot + 1].Fade * 0.5 : 0;
        if (next > 0) black = Math.Max(black, (o.T - (s.Dur - next)) / next);
        if (s.Type == "titre") black = Math.Max(black, (o.T - (s.Dur - 1.2)) / 1.2);
        _filmBlack = Math.Clamp(black, 0, 1);
        if (_filmFade != null) _filmFade.Color = new Color(0, 0, 0, (float)_filmBlack);
        if (o.Logo != null) o.Logo.Modulate = new Color(1, 1, 1, (float)LogoAlpha(o.T, s.Dur));

        if (o.T >= s.Dur) StartShot(o, o.Shot + 1);
    }

    /// <summary>Le logo : monte de la demi-seconde à deux secondes et demie, s'en va dans les deux dernières.</summary>
    static double LogoAlpha(double t, double d) =>
        Math.Clamp(Math.Min((t - 0.5) / 2.0, (d - 0.3 - t) / 2.0), 0, 1);

    Vector3 Local(double x, double y, double z)
    {
        var o = _sea.Core.Origin;
        return new Vector3((float)(x - o.X), (float)y, (float)(z - o.Z));
    }

    /* LA MONTÉE : l'œil recule depuis le navire, À L'OPPOSÉ de la ville, et monte ;
       le regard passe de sa mâture à la ville — le navire au premier plan, puis la
       rade et Port-Royal derrière lui. */
    (Vector3, Vector3) RiseShot(OpeningShot s, double u)
    {
        var T = Local(s.X, 0, s.Z);
        var C = Local(s.Tx, 0, s.Tz);
        var away = T - C; away.Y = 0;
        away = away.LengthSquared() > 1 ? away.Normalized() : Vector3.Back;
        double e = MathX.Smooth01(u);
        // la hauteur monte plus tard que le recul : on voit la coque avant le pont
        double h = s.H0 + (s.H1 - s.H0) * MathX.Smooth01(Math.Pow(u, 1.25));
        double r = s.R0 + (s.R1 - s.R0) * e;
        var eye = T + away * (float)r + new Vector3(0, (float)h, 0);
        double k = MathX.Smooth01(Math.Clamp((u - 0.12) / 0.7, 0, 1));
        var look = (T + new Vector3(0, 12, 0)).Lerp(C + new Vector3(0, 6, 0), (float)k);
        return (eye, look);
    }

    /* LE CHEMIN : une courbe par les points de la fiche, parcourue à vitesse égale
       (abscisse curviligne), à peine adoucie aux deux bouts — une coupe franche
       demande déjà du mouvement. */
    (Vector3, Vector3) PathShot(Opening o, double u)
    {
        var s = o.Shots[o.Shot];
        double v = 0.6 * u + 0.4 * MathX.Smooth01(u);
        var o0 = _sea.Core.Origin;
        var shift = new Vector3((float)o0.X, 0, (float)o0.Z);
        var eye = (o.Curve is { } c ? AtArc(c, v) : s.Path.Count > 0 ? s.Path[0] : Vector3.Zero) - shift;
        var look = (o.LookCurve is { } l ? AtArc(l, v) : s.Look.Count > 0 ? s.Look[0] : eye + shift + Vector3.Forward) - shift;
        return (eye, look);
    }

    /* LE DÉPART : par-dessus lui, de l'arrière à l'avant, un peu sur son flanc ; le
       regard part de sa coque et finit loin devant, vers le large. On suit sa place
       à plat — sa houle ne secoue pas l'œil. */
    (Vector3, Vector3) LeaverShot(Opening o, OpeningShot s, double u)
    {
        var ship = o.Leaver != null && IsInstanceValid(o.Leaver) && _others.Contains(o.Leaver) ? o.Leaver : _ship;
        var P = ship.Position; P.Y = 0;
        var fw = ship.GlobalTransform.Basis.Z; fw.Y = 0;
        fw = fw.LengthSquared() > 1e-6 ? fw.Normalized() : Vector3.Back;
        var side = new Vector3(fw.Z, 0, -fw.X);
        double e = MathX.Smooth01(u);
        float along = (float)(s.Back + (s.Ahead - s.Back) * e);
        float up = (float)(s.BackUp + (s.AheadUp - s.BackUp) * e);
        var eye = P + fw * along + side * (float)s.Side + new Vector3(0, up, 0);
        var look = P + fw * (float)(15 + 380 * e) + new Vector3(0, (float)(6 * (1 - e)), 0);
        return (eye, look);
    }

    /* LE TITRE : l'œil garde l'erre du plan d'avant et s'arrête en deux secondes ;
       le regard reste dans la même direction. */
    (Vector3, Vector3) HoldShot(Opening o, OpeningShot s, double dt)
    {
        var cur = _fixEye ?? o.LastEye;
        var dir = (_fixLook ?? cur + Vector3.Forward) - cur;
        o.EyeVel *= (float)Math.Exp(-dt / 1.4);
        var eye = cur + o.EyeVel * (float)dt;
        return (eye, eye + dir);
    }

    /// <summary>Jamais sous le sol : à « garde » mètres au-dessus de la terre, deux au-dessus de l'eau.</summary>
    Vector3 AboveGround(Vector3 eye, double guard)
    {
        var o = _sea.Core.Origin;
        double g = _world!.HeightAt(o.X + eye.X, o.Z + eye.Z);
        double min = g > 0 ? g + guard : 2;
        if (eye.Y < min) eye.Y = (float)min;
        return eye;
    }

    /* ------------------------------------------------------------------ */
    /*  LA COURBE                                                          */
    /* ------------------------------------------------------------------ */

    /// <summary>Une Catmull-Rom par ces points, échantillonnée, et son abscisse curviligne cumulée.</summary>
    static (Vector3[] P, double[] S) ArcCurve(List<Vector3> pts)
    {
        const int per = 40;
        int n = pts.Count;
        var p = new Vector3[(n - 1) * per + 1];
        for (int i = 0; i < n - 1; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Math.Min(n - 1, i + 2)];
            for (int k = 0; k < per; k++)
            {
                float t = k / (float)per, t2 = t * t, t3 = t2 * t;
                p[i * per + k] = 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
            }
        }
        p[^1] = pts[n - 1];
        var s = new double[p.Length];
        for (int i = 1; i < p.Length; i++) s[i] = s[i - 1] + p[i].DistanceTo(p[i - 1]);
        return (p, s);
    }

    /// <summary>Le point de la courbe à la fraction <paramref name="v"/> de sa longueur.</summary>
    static Vector3 AtArc((Vector3[] P, double[] S) c, double v)
    {
        double want = Math.Clamp(v, 0, 1) * c.S[^1];
        int lo = 0, hi = c.S.Length - 1;
        while (hi - lo > 1) { int m = (lo + hi) / 2; if (c.S[m] < want) lo = m; else hi = m; }
        double seg = c.S[hi] - c.S[lo];
        float f = seg > 1e-9 ? (float)((want - c.S[lo]) / seg) : 0;
        return c.P[lo].Lerp(c.P[hi], f);
    }

    /* ------------------------------------------------------------------ */
    /*  LE LOGO                                                            */
    /* ------------------------------------------------------------------ */

    /* L'IMAGE DU JEU, au milieu du cadre : un PNG s'il y en a un (« logo », chemin
       depuis la racine du projet), sinon son nom en anglaise — Estonia, celle du
       titre. Sous le noir du film : le fondu de fin l'emporte avec l'image. */
    void ShowLogo(Opening o, OpeningShot s)
    {
        if (_filmLayer == null) return;
        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var vp = GetViewport().GetVisibleRect().Size;
        string path = s.Logo.Length > 0 ? System.IO.Path.Combine(WorldLoad.Folder, s.Logo) : "";
        Image? img = null;
        if (path.Length > 0 && System.IO.File.Exists(path))
        {
            img = new Image();
            if (img.Load(path) != Error.Ok) img = null;
        }
        if (img != null)
        {
            float w = (float)(vp.X * s.Width);
            var tex = new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(img),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(w, w * img.GetHeight() / Math.Max(1, img.GetWidth())),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            centre.AddChild(tex);
        }
        else
        {
            var label = new Label { Text = s.Text.Length > 0 ? s.Text : GameTitle, HorizontalAlignment = HorizontalAlignment.Center };
            if (((Font?)Cursive() ?? HandFont.Get()) is { } font) label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", (int)Math.Max(48, vp.Y * 0.13));
            label.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 0.88f));
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0.02f, 0.04f, 0.85f));
            label.AddThemeConstantOverride("outline_size", 12);
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.45f));
            label.AddThemeConstantOverride("shadow_offset_x", 3);
            label.AddThemeConstantOverride("shadow_offset_y", 4);
            centre.AddChild(label);
            if (path.Length > 0) GD.Print($"[ouverture] pas de logo en {s.Logo} : le titre est écrit");
        }
        _filmLayer.AddChild(centre);
        _filmLayer.MoveChild(centre, 0);
        o.Logo = centre;
    }

    /* ------------------------------------------------------------------ */
    /*  FINIR, OU PASSER                                                   */
    /* ------------------------------------------------------------------ */

    void EndOpening()
    {
        var o = _opening;
        if (o == null) return;
        if (o.Logo != null && IsInstanceValid(o.Logo)) o.Logo.QueueFree();
        _opening = null;
        if (!_planted) { _fixEye = null; _fixLook = null; }
        CineShow();
        Play();
        // l'image revient du noir sur le navire du joueur, à son ponton
        _filmBlack = 0;
        if (_filmFade != null)
        {
            _filmFade.Color = new Color(0, 0, 0, 1);
            var tw = _filmFade.CreateTween();
            tw.TweenProperty(_filmFade, "color:a", 0.0f, 1.5);
        }
        GD.Print("[ouverture] fin : la barre est à vous");
    }

    /// <summary>L'ouverture prend toutes les touches ; Échap, Entrée et Espace la passent.</summary>
    bool OpeningInput(InputEvent e)
    {
        if (_opening == null) return false;
        if (e is InputEventKey k && k.Pressed && !k.Echo
            && (k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode) is Key.Escape or Key.Enter or Key.KpEnter or Key.Space)
            EndOpening();
        return e is InputEventKey or InputEventMouseButton;
    }
}
