using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LA CARTE DES MISSIONS (demandé, d'après une maquette : un parchemin déroulé, un
/// chemin, des points rouges, les titres en anglaise). Ce qu'on a fini porte sa
/// coche, le trait est plein jusqu'à la mission à faire et en pointillé au-delà ;
/// une mission dont la quête n'est pas encore écrite paraît en encre pâle.
///
/// Le papier est un shader (parchment.gdshader) ; le reste est dessiné ici, en
/// coordonnées de l'image 16:9 (quests/carte-missions.json), si bien que la carte
/// garde ses proportions à toute taille d'écran : ce qui dépasse est de la table.
/// </summary>
public partial class MissionMap : Control
{
    sealed class Spot
    {
        public string Title = "", Quest = "";
        public Vector2 At, Label;
        public bool Centred, Exists, Done;
        public string Summary = "";
        public Rect2 Hit;
    }

    readonly List<Spot> _spots = new();
    readonly List<Vector2> _curve = new();
    /// <summary>Les points de la courbe les plus proches de chaque mission, dans l'ordre.</summary>
    readonly List<int> _at = new();
    /// <summary>Ce qui se choisit au clavier : les missions jouables, les autres quêtes, le retour.</summary>
    readonly List<(Rect2 Hit, Action Go, string Caption)> _items = new();
    readonly List<(string Title, string Id, string Summary)> _others = new();
    int _pick = -1;
    Font? _script;
    Font _plain = null!;
    ShaderMaterial _paper = null!;
    ColorRect _sheet = null!;
    /// <summary>L'image du parchemin vierge, si la fiche en nomme une qui existe ; sinon le shader.</summary>
    TextureRect _photo = null!;
    string _photoPath = "";

    /// <summary>Lancer une quête par son identifiant.</summary>
    public Action<string>? Start;
    /// <summary>Revenir au menu.</summary>
    public Action? Back;

    // ink and wax, as the mock-up draws them
    static readonly Color Ink = new(0.07f, 0.055f, 0.04f);
    static readonly Color PaleInk = new(0.07f, 0.055f, 0.04f, 0.38f);
    static readonly Color Trail = new(0.27f, 0.26f, 0.25f);
    static readonly Color Tick = new(0.90f, 0.86f, 0.78f);
    static readonly Color Wax = new(0.86f, 0.08f, 0.11f);
    static readonly Color WaxDark = new(0.62f, 0.03f, 0.06f);

    public MissionMap(Font? script)
    {
        _script = script;
        AnchorRight = 1; AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
    }

    public override void _Ready()
    {
        _plain = ThemeDB.FallbackFont;
        _paper = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/parchment.gdshader") };
        _sheet = new ColorRect { AnchorRight = 1, AnchorBottom = 1, Material = _paper, MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true };
        AddChild(_sheet);
        /* PAR-DESSUS LE PAPIER DESSINÉ, sous le trait : l'image vierge couvre le cadre
           16:9, et le shader ne montre plus que la table autour d'elle. */
        _photo = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true, Visible = false
        };
        AddChild(_photo);
        Resized += () => { Layout(); QueueRedraw(); };
    }

    /// <summary>Relire la carte et l'état des quêtes, puis l'ouvrir.</summary>
    public void Open(Quests? book)
    {
        Load(book);
        Layout();
        _pick = -1;
        Visible = true;
        QueueRedraw();
    }

    void Load(Quests? book)
    {
        _spots.Clear(); _curve.Clear(); _at.Clear(); _others.Clear();
        var path = new List<Vector2>();
        string file = System.IO.Path.Combine(WorldLoad.Folder, "quests", "carte-missions.json");
        if (System.IO.File.Exists(file))
        {
            try
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                var r = doc.RootElement;
                Photo(r.TryGetProperty("fond", out var fo) ? fo.GetString() ?? "" : "");
                if (r.TryGetProperty("chemin", out var ch))
                    foreach (var p in ch.EnumerateArray()) path.Add(new Vector2(p[0].GetSingle(), p[1].GetSingle()));
                if (r.TryGetProperty("missions", out var ms))
                    foreach (var m in ms.EnumerateArray())
                    {
                        var s = new Spot
                        {
                            Title = m.TryGetProperty("titre", out var t) ? t.GetString() ?? "" : "",
                            Quest = m.TryGetProperty("quete", out var q) ? q.GetString() ?? "" : "",
                            At = new Vector2(m.GetProperty("x").GetSingle(), m.GetProperty("y").GetSingle())
                        };
                        if (m.TryGetProperty("etiquette", out var e))
                        {
                            if (e.ValueKind == JsonValueKind.Array) s.Label = new Vector2(e[0].GetSingle(), e[1].GetSingle());
                            else { s.Label = s.At; s.Centred = true; }
                        }
                        else s.Label = s.At + new Vector2(0.02f, -0.02f);
                        var spec = s.Quest.Length > 0 ? book?.ById(s.Quest) : null;
                        s.Exists = spec != null;
                        s.Done = spec != null && book!.Done.Contains(spec.Id);
                        s.Summary = spec?.Summary ?? "";
                        _spots.Add(s);
                    }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                GD.PushWarning($"[missions] carte-missions.json illisible : {ex.Message}");
            }
        }
        // the quests that are not on the map are still offered, in a corner
        if (book != null)
            foreach (var q in book.List)
                if (!_spots.Exists(s => s.Quest == q.Id))
                    _others.Add((q.Title.Length > 0 ? q.Title : q.Id, q.Id, q.Summary));

        /* LE TRAIT, LISSÉ : une Catmull-Rom par les points du chemin, douze pas
           entre deux — assez pour qu'une boucle ne montre pas ses angles. */
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var p0 = path[Math.Max(0, i - 1)]; var p1 = path[i]; var p2 = path[i + 1]; var p3 = path[Math.Min(path.Count - 1, i + 2)];
            for (int k = 0; k < 12; k++)
            {
                float u = k / 12f, u2 = u * u, u3 = u2 * u;
                _curve.Add(0.5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3));
            }
        }
        if (path.Count > 0) _curve.Add(path[^1]);
        foreach (var s in _spots)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < _curve.Count; i++) { float d = _curve[i].DistanceSquaredTo(s.At); if (d < bd) { bd = d; best = i; } }
            _at.Add(best);
        }
    }

    /// <summary>Lire l'image du fond (un chemin depuis la racine du projet), une fois par fichier.</summary>
    void Photo(string rel)
    {
        string full = rel.Length > 0 ? System.IO.Path.Combine(WorldLoad.Folder, rel) : "";
        if (full == _photoPath) return;
        _photoPath = full;
        _photo.Texture = null;
        if (full.Length > 0 && System.IO.File.Exists(full))
        {
            var img = Image.LoadFromFile(full);
            if (img != null) _photo.Texture = ImageTexture.CreateFromImage(img);
            else GD.PushWarning($"[missions] fond illisible : {rel}");
        }
        _photo.Visible = _photo.Texture != null;
    }

    void Layout()
    {
        var f = Frame();
        _photo.Position = f.Position;
        _photo.Size = f.Size;
    }

    // ------------------------------------------------------------------
    //  LE CADRE : l'image 16:9 posée au milieu de l'écran
    // ------------------------------------------------------------------

    Rect2 Frame()
    {
        var size = Size;
        float w = size.X, h = size.Y;
        if (w / h > 16f / 9f) w = h * 16f / 9f; else h = w * 9f / 16f;
        return new Rect2((size.X - w) * 0.5f, (size.Y - h) * 0.5f, w, h);
    }

    Vector2 P(Rect2 f, Vector2 m) => f.Position + m * f.Size;

    public override void _Draw()
    {
        var f = Frame();
        if (Size.X > 0 && Size.Y > 0)
            _paper.SetShaderParameter("u_frame", new Vector4(f.Position.X / Size.X, f.Position.Y / Size.Y, f.Size.X / Size.X, f.Size.Y / Size.Y));
        float H = f.Size.Y;
        _items.Clear();

        // ---- the trail: drawn up to the first mission not yet done, dashed beyond ----
        int split = _curve.Count - 1;
        for (int i = 0; i < _spots.Count; i++) if (!_spots[i].Done) { split = _at[i]; break; }
        float w = 0.013f * H;
        if (_curve.Count > 1)
        {
            var solid = new List<Vector2>();
            for (int i = 0; i <= split && i < _curve.Count; i++) solid.Add(P(f, _curve[i]));
            if (solid.Count > 1) DrawPolyline(solid.ToArray(), Trail, w, true);
            var rest = new List<Vector2>();
            for (int i = split; i < _curve.Count; i++) rest.Add(P(f, _curve[i]));
            if (rest.Count > 1)
            {
                DrawPolyline(rest.ToArray(), Trail, w, true);
                // light bars across it, as the mock-up hatches the road still to go
                float step = w * 2.4f, run = step * 0.5f;
                for (int i = 0; i + 1 < rest.Count; i++)
                {
                    var a = rest[i]; var b = rest[i + 1];
                    float len = a.DistanceTo(b);
                    var dir = (b - a) / Math.Max(1e-4f, len);
                    var nrm = new Vector2(-dir.Y, dir.X);
                    while (run < len)
                    {
                        var c = a + dir * run;
                        DrawLine(c - nrm * w * 0.62f + dir * w * 0.15f, c + nrm * w * 0.62f - dir * w * 0.15f, Tick, w * 0.42f, true);
                        run += step;
                    }
                    run -= len;
                }
            }
        }

        // ---- the missions ----
        foreach (var s in _spots)
        {
            var at = P(f, s.At);
            float r = 0.017f * H;
            int me = _items.Count;
            bool picked = s.Exists && _pick == me;
            if (picked) r *= 1.3f;
            // la cire : un point plein, puis le trait de plume qui l'a tourné
            DrawCircle(at, r * 1.12f, new Color(WaxDark, s.Exists ? 0.45f : 0.25f));
            DrawCircle(at, r, s.Exists ? Wax : new Color(Wax, 0.55f));
            /* le cercle tourné à la plume, un peu plus d'un tour et décentré : une
               marque faite à la main, pas une pastille */
            DrawArc(at + new Vector2(r * 0.10f, -r * 0.06f), r * 0.80f, 0.3f, 0.3f + Mathf.Tau * 1.08f, 28, WaxDark, r * 0.16f, true);
            if (s.Done)
            {
                // la coche, d'un seul geste
                var c0 = at + new Vector2(-1.55f, -1.55f) * r;
                DrawPolyline(new[] { c0 + new Vector2(-0.9f, -0.2f) * r, c0 + new Vector2(0.0f, 1.0f) * r, c0 + new Vector2(1.6f, -1.7f) * r },
                             Ink, r * 0.32f, true);
            }
            if (_script != null)
            {
                int fs = Mathf.RoundToInt(0.050f * H);
                var lp = P(f, s.Label);
                float tw = _script.GetStringSize(s.Title, HorizontalAlignment.Left, -1, fs).X;
                if (s.Centred) lp.X -= tw * 0.5f;
                var col = !s.Exists ? PaleInk : picked ? new Color(0.45f, 0.04f, 0.05f) : Ink;
                DrawString(_script, lp, s.Title, HorizontalAlignment.Left, -1, fs, col);
                s.Hit = new Rect2(lp.X, lp.Y - fs, tw, fs * 1.25f).Merge(new Rect2(at - Vector2.One * r * 1.8f, Vector2.One * r * 3.6f));
            }
            if (s.Exists)
            {
                var id = s.Quest;
                _items.Add((s.Hit, () => Start?.Invoke(id), s.Summary));
            }
        }

        // ---- the quests that are not on the map, then the way back ----
        float y = f.Position.Y + 0.925f * H;
        int ofs = Mathf.RoundToInt(0.030f * H);
        if (_script != null && _others.Count > 0)
        {
            float x = f.Position.X + 0.84f * f.Size.X;
            for (int i = _others.Count - 1; i >= 0; i--)
            {
                var (title, id, sum) = _others[i];
                float tw = _script.GetStringSize(title, HorizontalAlignment.Left, -1, ofs).X;
                x -= tw;
                bool picked = _pick == _items.Count;
                DrawString(_script, new Vector2(x, y), title, HorizontalAlignment.Left, -1, ofs, picked ? new Color(0.45f, 0.04f, 0.05f) : Ink);
                _items.Add((new Rect2(x, y - ofs, tw, ofs * 1.3f), () => Start?.Invoke(id), sum));
                x -= 0.025f * f.Size.X;
            }
        }
        if (_script != null)
        {
            int bfs = Mathf.RoundToInt(0.040f * H);
            var bp = new Vector2(f.Position.X + 0.175f * f.Size.X, y);
            bool picked = _pick == _items.Count;
            DrawString(_script, bp, "Retour", HorizontalAlignment.Left, -1, bfs, picked ? new Color(0.45f, 0.04f, 0.05f) : Ink);
            float tw = _script.GetStringSize("Retour", HorizontalAlignment.Left, -1, bfs).X;
            _items.Add((new Rect2(bp.X, bp.Y - bfs, tw, bfs * 1.3f), () => Back?.Invoke(), ""));
        }

        // ---- what the picked mission is about, in plain letters under the trail ----
        string caption = "";
        if (_pick >= 0 && _pick < _items.Count) caption = _items[_pick].Caption;
        else
            foreach (var s in _spots)
                if (s.Hit.HasPoint(GetLocalMousePosition()) && !s.Exists) caption = "En préparation";
        if (caption.Length > 0)
        {
            int cfs = Mathf.RoundToInt(0.022f * H);
            var cp = new Vector2(f.Position.X + 0.2f * f.Size.X, f.Position.Y + 0.875f * H);
            DrawString(_plain, cp, caption, HorizontalAlignment.Center, 0.6f * f.Size.X, cfs, new Color(Ink, 0.85f));
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            int was = _pick;
            _pick = -1;
            for (int i = 0; i < _items.Count; i++) if (_items[i].Hit.HasPoint(mm.Position)) { _pick = i; break; }
            if (_pick != was) QueueRedraw();
            else if (_pick < 0) QueueRedraw();       // a mission en préparation may be under the mouse
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Hit.HasPoint(mb.Position)) { _items[i].Go(); AcceptEvent(); return; }
        }
    }

    /// <summary>Le clavier, tant que la carte est ouverte : les flèches vont d'une mission à l'autre.</summary>
    public bool Key(Godot.Key key)
    {
        int n = _items.Count;
        if (n == 0) return false;
        switch (key)
        {
            case Godot.Key.Right or Godot.Key.Down:
                _pick = (_pick + 1) % n; QueueRedraw(); return true;
            case Godot.Key.Left or Godot.Key.Up:
                _pick = (_pick + n - 1 + (_pick < 0 ? 1 : 0)) % n; QueueRedraw(); return true;
            case Godot.Key.Enter or Godot.Key.KpEnter or Godot.Key.Space:
                if (_pick >= 0 && _pick < n) _items[_pick].Go();
                return true;
            case Godot.Key.Escape:
                Back?.Invoke(); return true;
        }
        return false;
    }
}
