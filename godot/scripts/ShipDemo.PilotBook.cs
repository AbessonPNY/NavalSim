using Godot;
using System;
using NavalSim.Core;

namespace NavalSim;

/// <summary>
/// LE LIVRE DU PILOTE (demandé) : il s'ouvre de lui-même quand on vient de lire une hauteur
/// (le calcul posé à droite, la table qui a servi à gauche), et se consulte depuis la carte.
/// Les nombres sont ceux que le jeu emploie : la déclinaison du calendrier
/// (<see cref="Calendar.DeclinationOn"/>), la correction des Gardes de <see cref="Sights"/>.
/// </summary>
public partial class ShipDemo
{
    CanvasLayer? _pilotLayer;
    PilotBook? _pilotBook;
    bool _pilotOpen;
    /// <summary>Le calcul de la dernière hauteur, pour la page de droite.</summary>
    string _lastSightCalc = "";

    static readonly string[] Wheel =
    {
        "à la tête", "au bras d'ouest", "à la ligne d'ouest", "sous le bras d'ouest",
        "aux pieds", "sous le bras d'est", "à la ligne d'est", "au bras d'est",
    };
    static readonly string[] WheelShort = { "tête", "bras d'ouest", "ligne d'ouest", "sous le bras d'ouest", "pieds", "sous le bras d'est", "ligne d'est", "bras d'est" };

    /// <summary>Ouvre le livre : 0 le régiment du soleil, 1 la règle de l'étoile du Nord.</summary>
    void OpenPilotBook(int page)
    {
        var geo = Instruments();
        if (geo == null || geo.LSize.X <= 0) return;
        if (_pilotLayer == null)
        {
            _pilotLayer = new CanvasLayer { Layer = 9, Visible = false };
            AddChild(_pilotLayer);
            _pilotBook = new PilotBook { Geo = geo, Picture = InventoryImage(geo.LImage), MouseFilter = Control.MouseFilterEnum.Ignore };
            _pilotBook.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _pilotLayer.AddChild(_pilotBook);
        }
        _pilotOpen = true;
        _pilotLayer.Visible = true;
        _pilotBook!.Page = page;
        FillPilotBook();
    }

    void ClosePilotBook()
    {
        if (!_pilotOpen) return;
        _pilotOpen = false;
        if (_pilotLayer != null) _pilotLayer.Visible = false;
    }

    void FillPilotBook()
    {
        var b = _pilotBook!;
        var date = _calendar.Date;
        // LE RÉGIMENT : le mois en cours, chaque jour sa déclinaison
        int days = DateTime.DaysInMonth(date.Year, date.Month);
        var jan1 = new DateTime(date.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new (string, string)[days];
        for (int d = 1; d <= days; d++)
        {
            double doy = (new DateTime(date.Year, date.Month, d, 0, 0, 0, DateTimeKind.Utc) - jan1).TotalDays;
            double decl = Calendar.DeclinationOn(doy);
            rows[d - 1] = (d.ToString(), $"{Sights.Dm(Math.Abs(decl))} {(decl >= 0 ? "N" : "S")}");
        }
        b.RegRows = rows;
        b.RegToday = date.Day - 1;
        string month = Months[date.Month - 1];
        b.RegTitle = $"{char.ToUpper(month[0])}{month[1..]} {date.Year}";

        // LA RÈGLE DE L'ÉTOILE DU NORD : la Garde aux huit places, et la correction de chacune
        var (pra, pdec) = Sights.Polaris(StarYear);
        var (kra, _) = Sights.Kochab(StarYear);
        var g = new (string, string, string)[8];
        for (int i = 0; i < 8; i++)
        {
            // la place i : la Garde à i × 45° d angle horaire (0, la tête : au-dessus du pôle)
            double corr = Sights.GuardsTable(i, pra, pdec, kra);
            g[i] = ("La Garde " + Wheel[i], WheelShort[i], Words(corr));
        }
        b.Guards = g;
        var (_, lon) = TrueLatLon();
        double kNow = Sights.HourAngle(date, _sky.Core.DayTime, lon, kra), pNow = Sights.HourAngle(date, _sky.Core.DayTime, lon, pra);
        b.GuardDeg = (float)(-90 - kNow);
        b.PolarisDeg = (float)(-90 - pNow);
        int place = Sights.GuardPlace(kNow);
        b.GuardNow = $"À cette heure, la Garde est {Wheel[place]} : la table dit {Words(Sights.GuardsTable(place, pra, pdec, kra))}";
        b.PoleNote = $"La Polaire tourne à {Sights.Dm(90 - pdec)} du pôle. Selon la place de la Garde,\najoutez ou ôtez à sa hauteur pour avoir la latitude.";

        b.RightTitle = "La dernière hauteur";
        b.RightText = _lastSightCalc.Length > 0 ? _lastSightCalc : "Pas encore de hauteur prise.\n\nLe soleil se prend à midi, au quartier de Davis ;\nla Polaire la nuit, à l'arbalestrille.";
        b.QueueRedraw();
    }

    /// <summary>« ôtez 1° 40′ » — une correction comme la table l'écrit.</summary>
    static string Words(double corr) =>
        Math.Abs(corr) < 0.5 / 60 ? "rien" : (corr > 0 ? "ajoutez " : "ôtez ") + Sights.Dm(Math.Abs(corr));

    /// <summary>Le livre ouvert prend les touches et les clics ; vrai si l'événement est pris.</summary>
    bool PilotBookInput(InputEvent e)
    {
        if (!_pilotOpen) return false;
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { ClosePilotBook(); return true; }
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            Key key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
            switch (key)
            {
                case Key.Left or Key.Right: _pilotBook!.Page = 1 - _pilotBook.Page; _pilotBook.QueueRedraw(); return true;
                case Key.Escape or Key.Enter or Key.KpEnter: ClosePilotBook(); return true;
            }
        }
        return e is InputEventKey or InputEventMouseButton;
    }
}
