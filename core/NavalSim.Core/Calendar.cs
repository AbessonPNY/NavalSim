namespace NavalSim.Core;

/// <summary>
/// LA DATE — calendar.js. Un calendrier qui tourne à minuit de l'horloge du
/// jour, depuis un départ réglé dans settings.json (calendar.start). Grégorien
/// proleptique des deux côtés : le Date de JavaScript comme le DateTime de .NET
/// le remontent jusqu'en 1598 sans broncher.
///
/// Porté pour le climat, qui en tire la saison. La déclinaison du soleil y est,
/// comme dans la page ; le ciel du portage ne s'en sert pas encore.
/// </summary>
public sealed class Calendar
{
    public DateTime Start { get; private set; }
    public int Day { get; private set; }

    public Calendar(string? startIso = null) => SetStart(startIso ?? "1598-10-08");

    public void SetStart(string iso)
    {
        var m = System.Text.RegularExpressions.Regex.Match(iso.Trim(), @"^(\d{3,4})-(\d{1,2})-(\d{1,2})$");
        int y = m.Success ? int.Parse(m.Groups[1].Value) : 1598;
        int mo = m.Success ? int.Parse(m.Groups[2].Value) : 10;
        int d = m.Success ? int.Parse(m.Groups[3].Value) : 8;
        Start = new DateTime(y, mo, d, 0, 0, 0, DateTimeKind.Utc);
        Day = 0;
    }

    public void NextDay() => Day++;

    public DateTime Date => Start.AddDays(Day);

    /// <summary>Le départ en millisecondes depuis 1970, comme le t0 de la page (négatif avant).</summary>
    public double T0Ms => (Start - DateTime.UnixEpoch).TotalMilliseconds;

    /// <summary>Le jour de l'année, fractionnaire : depuis le 1er janvier de la même année.</summary>
    public double DayOfYear(double hour) =>
        (Date - new DateTime(Date.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays + hour / 24;

    /// <summary>La déclinaison du soleil, en degrés, pour ce jour — l'approximation en cosinus.</summary>
    public double Declination() => -23.44 * Math.Cos(2 * Math.PI * (Math.Floor(DayOfYear(0)) + 10) / 365);
}
