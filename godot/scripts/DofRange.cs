using Godot;
using System;

namespace NavalSim;

/// <summary>
/// LA ZONE NETTE, sur un seul curseur de 0 à l'infini : deux repères qu'on
/// déplace à la main, net entre les deux. Le repère proche à 0 coupe le flou de
/// près, le lointain à ∞ celui du lointain.
///
/// L'ÉCHELLE : d = K·(u/(1−u))², u la position de 0 à 1. Elle part de 0, file à
/// l'infini au bout, et donne à chaque ordre de grandeur une place lisible — un
/// mètre vers le cinquième du curseur, K = 30 m au milieu, deux kilomètres et
/// demi aux neuf dixièmes. Une échelle logarithmique ne sait montrer ni 0 ni ∞.
/// </summary>
public partial class DofRange : Control
{
    public const float Infinity = 1e6f;
    const float K = 30f;
    const float Pad = 10f;            // la demi-largeur d'un repère : les extrêmes restent saisissables

    public float Near, Far = Infinity;
    public event Action? Changed;

    int _drag = -1;                   // 0 le proche, 1 le lointain

    static readonly Color Track = new(0.35f, 0.4f, 0.46f), Sharp = new(0.55f, 0.85f, 0.6f),
        NearCol = new(0.55f, 0.95f, 0.6f), FarCol = new(1.0f, 0.55f, 0.15f), Text = new(0.75f, 0.8f, 0.85f);

    public DofRange()
    {
        CustomMinimumSize = new Vector2(0, 38);
        FocusMode = FocusModeEnum.None;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public static float ToDistance(float u)
    {
        if (u <= 0) return 0;
        if (u >= 0.999f) return Infinity;
        float r = u / (1 - u);
        return K * r * r;
    }

    public static float ToPosition(float d)
    {
        if (d <= 0) return 0;
        if (d >= Infinity) return 1;
        float r = Mathf.Sqrt(d / K);
        return r / (1 + r);
    }

    public static string Format(float d) =>
        d <= 0 ? "0" : d >= Infinity ? "∞" : d < 10 ? $"{d:F1} m" : d < 1000 ? $"{d:F0} m" : $"{d / 1000:F1} km";

    float X(float u) => Pad + u * (Size.X - 2 * Pad);
    float U(float x) => Math.Clamp((x - Pad) / Math.Max(1, Size.X - 2 * Pad), 0, 1);

    public override void _Draw()
    {
        float y = 12, xn = X(ToPosition(Near)), xf = X(ToPosition(Far));
        DrawLine(new Vector2(X(0), y), new Vector2(X(1), y), Track, 4);
        DrawLine(new Vector2(xn, y), new Vector2(xf, y), Sharp, 4);
        DrawCircle(new Vector2(xn, y), 7, NearCol);
        DrawCircle(new Vector2(xf, y), 7, FarCol);

        var font = GetThemeDefaultFont();
        foreach (float d in new[] { 0f, 1f, 10f, 100f, 1000f, Infinity })
        {
            string s = d == 0 ? "0" : d >= Infinity ? "∞" : d >= 1000 ? "1 km" : $"{d:F0} m";
            float x = X(ToPosition(d));
            DrawLine(new Vector2(x, y + 6), new Vector2(x, y + 10), Track, 1);
            float w = font.GetStringSize(s, HorizontalAlignment.Left, -1, 11).X;
            DrawString(font, new Vector2(Math.Clamp(x - w / 2, 0, Size.X - w), y + 24), s,
                HorizontalAlignment.Left, -1, 11, Text);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (!mb.Pressed) { _drag = -1; return; }
            // le repère le plus proche du clic ; à égalité (superposés), celui vers lequel on tire
            float u = U(mb.Position.X), un = ToPosition(Near), uf = ToPosition(Far);
            _drag = Math.Abs(u - un) < Math.Abs(u - uf) || (un == uf && u < un) ? 0 : 1;
            Move(u);
            AcceptEvent();
        }
        else if (e is InputEventMouseMotion mm && _drag >= 0)
        {
            Move(U(mm.Position.X));
            AcceptEvent();
        }
    }

    void Move(float u)
    {
        float d = ToDistance(u);
        // le proche ne passe pas le lointain, ni l'inverse : la zone nette ne se retourne pas
        if (_drag == 0) Near = Math.Min(d, Far >= Infinity ? d : Far);
        else Far = Math.Max(d, Near);
        if (_drag == 0 && Near >= Infinity) Near = ToDistance(0.998f);
        QueueRedraw();
        Changed?.Invoke();
    }
}
