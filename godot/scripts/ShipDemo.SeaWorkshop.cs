using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// L'ATELIER DE LA MER, sur Ctrl ⇧M (ou le bouton du panneau ⇧M) — demandé : « un écran
/// avec des curseurs pour régler notre shader encore plus précisément, le nombre de
/// composantes de Gerstner, les amplitudes des rides, tous les paramètres, en temps réel ».
///
/// Trois onglets :
/// — HOULE : les chiffres du spectre (<see cref="SeaTune"/>), le nombre de composantes
///   compris (8 à 32). C'est la mer que les coques sentent autant que celle qu'on voit :
///   les trois calculateurs lisent le même spectre, rien ne se désaccorde.
/// — RIDES : leur force et sa montée avec le vent, et les deux nappes de la carte de
///   spectre (globales naval_ride_b, posées par materiaux.json).
/// — SURFACE : TOUS les réglages du shader de la mer, lus sur le shader lui-même — nom,
///   défaut, plage (hint_range, sinon déduite du défaut), et le commentaire qui le précède
///   dans le source en bulle d'aide. Ceux que le jeu écrit lui-même (le soleil, les
///   navires, l'heure…) se REPÈRENT seuls : un curseur dont la valeur bouge sans lui, ou
///   revient après qu'on l'a bougé, passe en gris, « (le jeu) ». Ceux du panneau ⇧M n'y
///   sont pas : ils ont déjà leurs curseurs et leur place dans reglages.ini.
///
/// ENREGISTRER écrit user://atelier-mer.ini (relu au lancement), seulement ce qui diffère
/// du défaut. COPIER met ces mêmes valeurs dans le presse-papiers, prêtes à devenir les
/// nouveaux défauts dans le code. Défauts remet l'onglet ouvert.
/// </summary>
public partial class ShipDemo
{
    const string WorkshopFile = "user://atelier-mer.ini";

    sealed class WsRow
    {
        public string Section = "", Key = "", Name = "";
        public float Def, Last;
        public bool Int, Shader, Driven;
        public Func<float> Get = null!;
        public Action<float> Set = null!;
        public HSlider? S;
        public Label? V, N;
        public ColorPickerButton? Pick;     // une couleur de l'eau (vec3 linéaire)
        public Vector3 DefCol;
    }

    PanelContainer? _workshop;
    TabContainer? _wsTabs;
    readonly List<WsRow> _wsRows = new();
    /// <summary>La force des rides : au calme, par m/s de vent, et son plafond (ShipDemo.cs, avant figés).</summary>
    double _rippleBase = 0.40, _ripplePerMs = RipplePerMs0, _rippleMax = 2.6;
    /// <summary>La montée des rides avec le vent, réglée à l'atelier (12/10) : 0,039 par m/s, c'était 0,105 — le clapot ne grossit plus guère avec le vent.</summary>
    const double RipplePerMs0 = 0.039;
    Vector4? _rideBOverride;
    Label? _wsNote;

    /* Ce que le jeu pose à chaque image, ou qui n'est pas un réglage : pas de curseur. Le
       reste, s'il est piloté, se repère seul en jeu. */
    static readonly string[] WsSkip =
    {
        "u_time", "u_half", "u_seg", "u_centre", "u_wave_", "u_ship_", "u_lamp", "u_sun", "u_moon", "u_foam_",
        "u_submerged", "u_mirror_near", "u_amp_max", "u_sharp", "u_wind", "u_ripple", "u_storm", "u_flash",
        "u_rain", "u_sea_storm", "u_shore_dist", "u_sunlit", "u_water_light", "u_zenith", "u_horizon", "u_cloud",
        "u_sky_time", "u_shelter", "u_harbour", "u_haze", "u_fog", "u_cam", "u_size", "u_origin",
        // au panneau ⇧M (Settings) : ils y ont leurs curseurs
        "u_rough_base", "u_rough_wind", "u_sky_blur", "u_ride_gain", "u_jac_foam", "u_cap_gain", "u_foam_gain",
        "u_streak_gain", "u_kelvin_gain"
    };
    static readonly string[] WsColours = { "u_deep", "u_shallow", "u_sss" };

    void ToggleWorkshop()
    {
        if (_workshop == null) BuildWorkshop();
        _workshop!.Visible = !_workshop.Visible;
    }

    void BuildWorkshop()
    {
        _workshop = new PanelContainer
        {
            AnchorLeft = 0, AnchorRight = 0, AnchorTop = 0, AnchorBottom = 1,
            OffsetLeft = 12, OffsetRight = 492, OffsetTop = 60, OffsetBottom = -60,
            Visible = false
        };
        _workshop.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.06f, 0.09f, 0.80f),
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10
        });
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 6);
        _workshop.AddChild(outer);
        var title = new Label { Text = "Atelier de la mer (Ctrl ⇧M)" };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
        outer.AddChild(title);

        _wsTabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(_wsTabs);
        var houle = WsTab("Houle");
        var rides = WsTab("Rides");
        var surface = WsTab("Surface");

        // --- HOULE ---
        var T = _sea.Core.Tune;
        var D = new SeaTune();
        WsNote(houle, "Le spectre que lisent la mer, la flottaison et l'écume. Le changer fait sauter la mer une fois.");
        WsAdd(houle, "houle", "Count", "Composantes du spectre", 8, Config.NWavesMax, 1, D.Count, () => T.Count, v => { T.Count = (int)v; Restate(); }, true);
        WsAdd(houle, "houle", "CpuCount", "…dont la flottaison lit (coût par sonde)", 4, Config.NWavesMax, 1, D.CpuCount, () => T.CpuCount, v => { T.CpuCount = (int)v; Restate(); }, true);
        WsAdd(houle, "houle", "Swell", "Creux (× hauteur significative ; PageUp/PageDown)", 0.4, 2.6, 0.01, 1.35f, () => (float)_swell, v => { _swell = v; Restate(); });
        WsAdd(houle, "houle", "Gamma", "Pic JONSWAP γ (1 = Pierson-Moskowitz)", 1, 7, 0.05, (float)D.Gamma, () => (float)T.Gamma, v => { T.Gamma = v; Restate(); });
        WsAdd(houle, "houle", "PeakScale", "Pulsation de pic × (plus haut : vagues plus courtes)", 0.5, 2, 0.01, (float)D.PeakScale, () => (float)T.PeakScale, v => { T.PeakScale = v; Restate(); });
        WsAdd(houle, "houle", "BandLo", "Bande : plus longue (× pic)", 0.3, 1, 0.01, (float)D.BandLo, () => (float)T.BandLo, v => { T.BandLo = v; Restate(); });
        WsAdd(houle, "houle", "BandHi", "Bande : plus courte (× pic)", 1.5, 6, 0.05, (float)D.BandHi, () => (float)T.BandHi, v => { T.BandHi = v; Restate(); });
        WsAdd(houle, "houle", "SpreadBase", "Étalement directionnel : base", 0, 0.6, 0.005, (float)D.SpreadBase, () => (float)T.SpreadBase, v => { T.SpreadBase = v; Restate(); });
        WsAdd(houle, "houle", "SpreadShort", "Étalement : part des courtes", 0, 1.2, 0.01, (float)D.SpreadShort, () => (float)T.SpreadShort, v => { T.SpreadShort = v; Restate(); });
        WsAdd(houle, "houle", "SpreadState0", "Étalement : facteur au calme", 0.05, 1, 0.01, (float)D.SpreadState0, () => (float)T.SpreadState0, v => { T.SpreadState0 = v; Restate(); });
        WsAdd(houle, "houle", "SpreadState1", "Étalement : par Beaufort", 0, 0.15, 0.001, (float)D.SpreadState1, () => (float)T.SpreadState1, v => { T.SpreadState1 = v; Restate(); });
        WsAdd(houle, "houle", "ChopBase", "Raideur de Gerstner : base", 0, 1, 0.01, (float)D.ChopBase, () => (float)T.ChopBase, v => { T.ChopBase = v; Restate(); });
        WsAdd(houle, "houle", "ChopState", "Raideur : par Beaufort", 0, 0.2, 0.001, (float)D.ChopState, () => (float)T.ChopState, v => { T.ChopState = v; Restate(); });
        WsAdd(houle, "houle", "QMax", "Raideur : plafond par composante", 0.2, 1, 0.01, (float)D.QMax, () => (float)T.QMax, v => { T.QMax = v; Restate(); });
        WsAdd(houle, "houle", "SharpBase", "Aiguisage des crêtes : base", 0, 0.3, 0.002, (float)D.SharpBase, () => (float)T.SharpBase, v => { T.SharpBase = v; Restate(); });
        WsAdd(houle, "houle", "SharpState", "Aiguisage : par Beaufort", 0, 0.2, 0.001, (float)D.SharpState, () => (float)T.SharpState, v => { T.SharpState = v; Restate(); });
        WsAdd(houle, "houle", "SharpMax", "Aiguisage : plafond", 0, 1.5, 0.01, (float)D.SharpMax, () => (float)T.SharpMax, v => { T.SharpMax = v; Restate(); });

        // --- RIDES ---
        WsNote(rides, "Ce qui est plus fin que la maille : la normale seule, aucune coque n'en dépend.");
        WsAdd(rides, "rides", "Base", "Force au calme", 0, 1.5, 0.01, 0.40f, () => (float)_rippleBase, v => _rippleBase = v);
        WsAdd(rides, "rides", "PerMs", "Montée par m/s de vent", 0, 0.3, 0.001, (float)RipplePerMs0, () => (float)_ripplePerMs, v => _ripplePerMs = v);
        WsAdd(rides, "rides", "Max", "Plafond", 0.5, 5, 0.05, 2.6f, () => (float)_rippleMax, v => _rippleMax = v);
        WsAdd(rides, "rides", "Gain", "Gain à l'œil (le même qu'au ⇧M)", 0, 3, 0.05, new Settings().SeaRideGain, () => _settings.SeaRideGain, v => { _settings.SeaRideGain = v; ApplySettings(); });
        var rb0 = RideB();
        string[] rbName = { "Pente maximale de la carte", "Force de la carte", "Seconde nappe : échelle", "Seconde nappe : poids" };
        double[] rbMax = { Math.Max(1, rb0.X * 3), Math.Max(3, rb0.Y * 3), Math.Max(8, rb0.Z * 3), Math.Max(1.5, rb0.W * 3) };
        for (int c = 0; c < 4; c++)
        {
            int cc = c;
            WsAdd(rides, "rides", "naval_ride_b." + "xyzw"[c], rbName[c], 0, rbMax[c], rbMax[c] / 500, rb0[c],
                () => RideB()[cc], v => { var b = RideB(); b[cc] = v; _rideBOverride = b; RenderingServer.GlobalShaderParameterSet("naval_ride_b", b); });
        }

        // --- SURFACE : lue sur le shader ---
        WsNote(surface, "Tous les réglages du shader de la mer. En gris : écrits par le jeu à chaque image. Survolez un nom : son commentaire.");
        if (_sea.Material is ShaderMaterial sm && sm.Shader is Shader sh) WsShaderRows(surface, sm, sh);

        // --- les boutons ---
        var buttons = new HBoxContainer();
        outer.AddChild(buttons);
        Button B(string t, Action a) { var b = new Button { Text = t, FocusMode = Control.FocusModeEnum.None }; b.Pressed += a; buttons.AddChild(b); return b; }
        B("Enregistrer", () => { WorkshopSave(); WsSay("Enregistré dans " + ProjectSettings.GlobalizePath(WorkshopFile)); });
        B("Copier", () => { DisplayServer.ClipboardSet(WorkshopText()); WsSay("Les valeurs changées sont dans le presse-papiers"); });
        B("Défauts de l'onglet", () =>
        {
            string sec = _wsTabs.CurrentTab switch { 0 => "houle", 1 => "rides", _ => "surface" };
            foreach (var r in _wsRows) if (r.Section == sec && !r.Driven) WsReset(r);
            if (sec == "rides") _rideBOverride = null;
            WsSay("Onglet remis à ses défauts");
        });
        _wsNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _wsNote.AddThemeFontSizeOverride("font_size", 12);
        _wsNote.AddThemeColorOverride("font_color", new Color(0.75f, 0.82f, 0.9f));
        outer.AddChild(_wsNote);

        _hud.AddChild(_workshop);
    }

    void WsSay(string s) { if (_wsNote != null) _wsNote.Text = s; }

    VBoxContainer WsTab(string name)
    {
        var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(box);
        _wsTabs!.AddChild(scroll);
        return box;
    }

    static void WsNote(VBoxContainer box, string text)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.86f));
        box.AddChild(l);
    }

    WsRow WsAdd(VBoxContainer box, string section, string key, string name, double min, double max, double step,
                float def, Func<float> get, Action<float> set, bool isInt = false, string tip = "")
    {
        var r = new WsRow { Section = section, Key = key, Name = name, Def = def, Get = get, Set = set, Int = isInt };
        float cur = get();
        r.Last = cur;
        r.V = Row(box, name);
        r.N = (Label)r.V.GetParent().GetChild(0);
        r.N.TooltipText = tip.Length > 0 ? tip : key;
        r.N.MouseFilter = Control.MouseFilterEnum.Pass;
        r.N.AddThemeFontSizeOverride("font_size", 13);
        r.V.AddThemeFontSizeOverride("font_size", 13);
        r.V.Text = WsFmt(r, cur);
        r.S = Slider(box, Math.Min(min, cur), Math.Max(max, cur), step, cur);
        r.S.ValueChanged += x =>
        {
            if (r.Driven) return;
            float v = (float)x;
            r.Set(v);
            r.Last = r.Get();
            r.V!.Text = WsFmt(r, r.Last);
        };
        _wsRows.Add(r);
        return r;
    }

    static string WsFmt(WsRow r, float v) =>
        r.Int ? ((int)v).ToString(CultureInfo.InvariantCulture)
        : v.ToString(Math.Abs(v) >= 10 ? "F1" : "F3", CultureInfo.InvariantCulture) + (Math.Abs(v - r.Def) > 1e-5 ? "  ·" : "");

    void WsReset(WsRow r)
    {
        if (r.Pick != null)
        {
            r.Pick.Color = WsToSrgb(r.DefCol);
            r.Set(0);   // la couleur se pose par le sélecteur ; Set relit Pick
            return;
        }
        r.S!.Value = r.Def;
    }

    Vector4 RideB()
    {
        var v = RenderingServer.GlobalShaderParameterGet("naval_ride_b");
        return v.VariantType == Variant.Type.Vector4 ? v.AsVector4() : new Vector4(0.5f, 1, 3, 0.6f);
    }

    /* ------------------------------------------------------------------ */
    /* LA SURFACE : un curseur par réglage du shader, lu sur lui.         */
    /* ------------------------------------------------------------------ */

    void WsShaderRows(VBoxContainer box, ShaderMaterial m, Shader sh)
    {
        var notes = WsShaderNotes();
        var rid = sh.GetRid();
        foreach (var o in sh.GetShaderUniformList())
        {
            var u = o.AsGodotDictionary();
            string name = u["name"].AsString();
            if (Array.Exists(WsSkip, p => name.StartsWith(p))) continue;
            var type = (Variant.Type)u["type"].AsInt32();
            var hint = (PropertyHint)u["hint"].AsInt32();
            string hs = u["hint_string"].AsString();
            Variant def = RenderingServer.ShaderGetParameterDefault(rid, name);
            string tip = notes.TryGetValue(name, out var n) ? n : "";
            if (type == Variant.Type.Float)
            {
                float d = def.VariantType == Variant.Type.Nil ? 0 : def.AsSingle();
                var (lo, hi, st) = WsRange(hint, hs, d);
                string nm = name;
                WsAdd(box, "surface", nm, nm, lo, hi, st, d, () => WsGetF(m, nm, d), v => m.SetShaderParameter(nm, v), false, tip).Shader = true;
            }
            else if (type == Variant.Type.Vector3 || type == Variant.Type.Color)
            {
                Vector3 d = def.VariantType == Variant.Type.Vector3 ? def.AsVector3()
                          : def.VariantType == Variant.Type.Color ? new Vector3(def.AsColor().R, def.AsColor().G, def.AsColor().B) : Vector3.Zero;
                string nm = name;
                if (Array.IndexOf(WsColours, name) >= 0) { WsColour(box, m, nm, d, tip); continue; }
                for (int c = 0; c < 3; c++)
                {
                    int cc = c;
                    var (lo, hi, st) = WsRange(PropertyHint.None, "", d[c]);
                    WsAdd(box, "surface", nm + "." + "xyz"[c], nm + "." + "xyz"[c], lo, hi, st, d[c],
                        () => WsGetV(m, nm, d)[cc], v => { var w = WsGetV(m, nm, d); w[cc] = v; m.SetShaderParameter(nm, w); }, false, tip).Shader = true;
                }
            }
        }
    }

    static float WsGetF(ShaderMaterial m, string n, float d)
    {
        var v = m.GetShaderParameter(n);
        return v.VariantType == Variant.Type.Float ? v.AsSingle() : v.VariantType == Variant.Type.Int ? v.AsInt32() : d;
    }

    static Vector3 WsGetV(ShaderMaterial m, string n, Vector3 d)
    {
        var v = m.GetShaderParameter(n);
        return v.VariantType == Variant.Type.Vector3 ? v.AsVector3() : v.VariantType == Variant.Type.Color ? new Vector3(v.AsColor().R, v.AsColor().G, v.AsColor().B) : d;
    }

    /// <summary>La plage d'un curseur : le hint_range du shader s'il y en a un, sinon trois fois le défaut.</summary>
    static (double Lo, double Hi, double Step) WsRange(PropertyHint hint, string hs, float d)
    {
        if (hint == PropertyHint.Range)
        {
            var p = hs.Split(',');
            if (p.Length >= 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
                return (a, b, p.Length >= 3 && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : (b - a) / 500);
        }
        double m = Math.Abs(d);
        if (m < 1e-6) return (0, 1, 0.002);
        double hi = Math.Max(1, m * 3);
        return d < 0 ? (-hi, hi, hi / 500) : (0, hi, hi / 500);
    }

    /* LES COULEURS DE L'EAU sont écrites LINÉAIRES dans le shader (u_deep = 0x0e3347 décodé) :
       le sélecteur les montre en sRGB, comme un peintre les voit, et rend du linéaire. */
    void WsColour(VBoxContainer box, ShaderMaterial m, string name, Vector3 def, string tip)
    {
        var row = new HBoxContainer();
        var l = new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = tip, MouseFilter = Control.MouseFilterEnum.Pass };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f));
        var pick = new ColorPickerButton { Color = WsToSrgb(WsGetV(m, name, def)), EditAlpha = false, CustomMinimumSize = new Vector2(90, 22), FocusMode = Control.FocusModeEnum.None };
        row.AddChild(l); row.AddChild(pick);
        box.AddChild(row);
        var r = new WsRow { Section = "surface", Key = name, Name = name, Shader = true, Pick = pick, DefCol = def, N = l };
        r.Get = () => 0; r.Set = _ => m.SetShaderParameter(name, WsToLinear(pick.Color));
        pick.ColorChanged += _ => r.Set(0);
        _wsRows.Add(r);
    }

    static Color WsToSrgb(Vector3 lin) => new Color(lin.X, lin.Y, lin.Z).LinearToSrgb();
    static Vector3 WsToLinear(Color c) { var l = c.SrgbToLinear(); return new Vector3(l.R, l.G, l.B); }

    /// <summary>
    /// Le commentaire de chaque réglage, lu dans le source du shader et de ses inclusions :
    /// celui de sa ligne, sinon le bloc qui le précède. Il dit déjà, en français, ce que fait
    /// le réglage — c'est la bulle d'aide.
    /// </summary>
    static Dictionary<string, string> WsShaderNotes()
    {
        var notes = new Dictionary<string, string>();
        var files = new List<string> { "res://shaders/ocean.gdshader" };
        var seen = new HashSet<string>();
        var uni = new Regex(@"^\s*uniform\s+\w+\s+(\w+)");
        for (int f = 0; f < files.Count; f++)
        {
            if (!seen.Add(files[f]) || !FileAccess.FileExists(files[f])) continue;
            var lines = FileAccess.GetFileAsString(files[f]).Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var inc = Regex.Match(lines[i], "^#include \"(res://[^\"]+)\"");
                if (inc.Success) { files.Add(inc.Groups[1].Value); continue; }
                var mu = uni.Match(lines[i]);
                if (!mu.Success) continue;
                string name = mu.Groups[1].Value;
                int sl = lines[i].IndexOf("//", StringComparison.Ordinal);
                var sb = new StringBuilder();
                // le bloc qui précède : des lignes de commentaire, sans ligne vide
                var block = new List<string>();
                bool inBlock = false;
                for (int j = i - 1; j >= 0 && j >= i - 24; j--)
                {
                    string t = lines[j].Trim();
                    if (t.Length == 0) break;
                    if (t.EndsWith("*/")) inBlock = true;
                    if (inBlock) { block.Insert(0, t); if (t.StartsWith("/*")) break; continue; }
                    if (t.StartsWith("//")) { block.Insert(0, t); continue; }
                    break;
                }
                foreach (var b in block) sb.Append(b.TrimStart('/', '*', ' ').TrimEnd('*', '/', ' ')).Append(' ');
                if (sl >= 0) sb.Append(lines[i][(sl + 2)..].Trim());
                string note = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                if (note.Length > 600) note = note[..600] + "…";
                notes[name] = note;
            }
        }
        return notes;
    }

    /* ------------------------------------------------------------------ */
    /* CHAQUE IMAGE : ce que le jeu pilote se repère, ce qui est gardé se pose. */
    /* ------------------------------------------------------------------ */

    int _wsOpenTab = -1;

    bool _wsLoaded;

    void WorkshopTick()
    {
        /* CE QUE L'ATELIER A GARDÉ, relu à la PREMIÈRE IMAGE et non à la construction du HUD :
           la mer n'existe pas encore à ce moment-là, et la relecture y plantait le chargement
           dès qu'un fichier avait été enregistré (signalé : « le jeu bloque au chargement »). */
        if (!_wsLoaded) { _wsLoaded = true; WorkshopLoad(); }
        if (_wsOpenTab >= 0) { if (_workshop?.Visible != true) ToggleWorkshop(); _wsTabs!.CurrentTab = _wsOpenTab; _wsOpenTab = -1; }
        if (_rideBOverride is Vector4 rb) RenderingServer.GlobalShaderParameterSet("naval_ride_b", rb);
        if (_workshop?.Visible != true) return;
        foreach (var r in _wsRows)
        {
            if (!r.Shader || r.Pick != null || r.S == null) continue;
            float cur = r.Get();
            if (Math.Abs(cur - r.Last) > 1e-5 * Math.Max(1, Math.Abs(cur)))
            {
                if (!r.Driven)
                {
                    r.Driven = true;
                    r.S.Editable = false;
                    r.N!.Text = r.Name + "  (le jeu)";
                    r.N.AddThemeColorOverride("font_color", new Color(0.55f, 0.6f, 0.66f));
                }
                r.S.SetValueNoSignal(Math.Clamp(cur, r.S.MinValue, r.S.MaxValue));
                r.V!.Text = WsFmt(r, cur);
                r.Last = cur;
            }
        }
    }

    /* ------------------------------------------------------------------ */
    /* GARDER, RELIRE, COPIER                                              */
    /* ------------------------------------------------------------------ */

    void WorkshopSave()
    {
        var cf = new ConfigFile();
        foreach (var r in _wsRows)
        {
            if (r.Driven) continue;
            if (r.Pick != null)
            {
                var lin = WsToLinear(r.Pick.Color);
                if ((lin - r.DefCol).Length() > 1e-4) cf.SetValue(r.Section, r.Key, lin);
                continue;
            }
            float v = r.Get();
            if (Math.Abs(v - r.Def) > 1e-6 && !(r.Section == "rides" && r.Key == "Gain")) cf.SetValue(r.Section, r.Key, v);
        }
        cf.Save(WorkshopFile);
    }

    /// <summary>Au lancement : ce que l'atelier a gardé. Sans fichier, rien ne change.</summary>
    void WorkshopLoad()
    {
        var cf = new ConfigFile();
        if (cf.Load(WorkshopFile) != Error.Ok) return;
        var T = _sea.Core.Tune;
        double H(string k, double d) => cf.GetValue("houle", k, d).AsDouble();
        T.Count = (int)H("Count", T.Count); T.CpuCount = (int)H("CpuCount", T.CpuCount);
        _swell = H("Swell", _swell);
        T.Gamma = H("Gamma", T.Gamma); T.PeakScale = H("PeakScale", T.PeakScale);
        T.BandLo = H("BandLo", T.BandLo); T.BandHi = H("BandHi", T.BandHi);
        T.SpreadBase = H("SpreadBase", T.SpreadBase); T.SpreadShort = H("SpreadShort", T.SpreadShort);
        T.SpreadState0 = H("SpreadState0", T.SpreadState0); T.SpreadState1 = H("SpreadState1", T.SpreadState1);
        T.ChopBase = H("ChopBase", T.ChopBase); T.ChopState = H("ChopState", T.ChopState); T.QMax = H("QMax", T.QMax);
        T.SharpBase = H("SharpBase", T.SharpBase); T.SharpState = H("SharpState", T.SharpState); T.SharpMax = H("SharpMax", T.SharpMax);
        _rippleBase = cf.GetValue("rides", "Base", _rippleBase).AsDouble();
        _ripplePerMs = cf.GetValue("rides", "PerMs", _ripplePerMs).AsDouble();
        _rippleMax = cf.GetValue("rides", "Max", _rippleMax).AsDouble();
        if (cf.HasSection("rides"))
            foreach (var k in cf.GetSectionKeys("rides"))
                if (k.StartsWith("naval_ride_b."))
                {
                    var b = _rideBOverride ?? RideB();
                    b["xyzw".IndexOf(k[^1])] = (float)cf.GetValue("rides", k).AsDouble();
                    _rideBOverride = b;
                }
        if (cf.HasSection("surface") && _sea.Material is ShaderMaterial m)
            foreach (var k in cf.GetSectionKeys("surface"))
            {
                var v = cf.GetValue("surface", k);
                int dot = k.IndexOf('.');
                if (dot < 0) m.SetShaderParameter(k, v);
                else
                {
                    string n = k[..dot];
                    var w = WsGetV(m, n, RenderingServer.ShaderGetParameterDefault(m.Shader.GetRid(), n).AsVector3());
                    w["xyz".IndexOf(k[^1])] = (float)v.AsDouble();
                    m.SetShaderParameter(n, w);
                }
            }
        Restate();
        GD.Print($"[atelier] {ProjectSettings.GlobalizePath(WorkshopFile)} relu");
    }

    /// <summary>Ce qui diffère du défaut, lisible et prêt à reporter dans le code.</summary>
    string WorkshopText()
    {
        var sb = new StringBuilder("# Atelier de la mer — valeurs changées\n");
        string sec = "";
        foreach (var r in _wsRows)
        {
            if (r.Driven) continue;
            string val;
            if (r.Pick != null)
            {
                var lin = WsToLinear(r.Pick.Color);
                if ((lin - r.DefCol).Length() <= 1e-4) continue;
                val = FormattableString.Invariant($"vec3({lin.X:F5}, {lin.Y:F5}, {lin.Z:F5})   // #{r.Pick.Color.ToHtml(false)}");
            }
            else
            {
                float v = r.Get();
                if (Math.Abs(v - r.Def) <= 1e-6) continue;
                val = FormattableString.Invariant($"{v:G5}   (défaut {r.Def:G5})");
            }
            if (r.Section != sec) { sec = r.Section; sb.Append('[').Append(sec).Append("]\n"); }
            sb.Append(r.Key).Append(" = ").Append(val).Append('\n');
        }
        return sb.ToString();
    }
}
