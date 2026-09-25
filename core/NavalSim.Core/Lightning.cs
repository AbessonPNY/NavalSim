using System.Text.Json;

namespace NavalSim.Core;

/// <summary>
/// LA FOUDRE QUI TROUVE UN NAVIRE — les règles de lightning.js (settings.json →
/// storm.lightning). Le ciel scintille déjà dans un grain et sur les bancs
/// lointains, sans jamais rien toucher ; celle-ci DESCEND, sur la chose la plus
/// haute qui sorte de la mer, et en mer c'est une tête de mât. Choisie navire par
/// navire, d'autant plus souvent qu'il est enfoncé dans la dépression.
/// </summary>
public sealed class LightningSettings
{
    public bool Enabled = true;
    public double MinInten = 0.4;          // enfoncement sous lequel rien ne tombe
    public double PerMinuteAtCore = 0.5;   // coups par minute sur un navire au centre même
    public double SplitChance = 0.5;       // une voile arrachée de ses ralingues
    public double WoundChance = 0.6;       // le mât blessé (trois blessures et il tombe)
    public double DismastChance = 0.12;    // le mât fendu d'un coup

    /// <summary>
    /// LE COUP COMME LUMIÈRE — ce que l'éclair jette sur le pont, et non ce qu'il
    /// casse. L'éclat du ciel (<c>Sky.Flash</c>) monte le HÉMISPHÉRIQUE : il
    /// blanchit la mer et les voiles également, de partout à la fois, ce qui est
    /// juste pour un coup à six milles et faux pour un coup au-dessus de la tête.
    /// Un vrai coup vient d'UN ENDROIT : il allume le bord qui le regarde, laisse
    /// l'autre dans le noir et couche l'ombre des mâts en travers du pont.
    ///
    /// energy : l'éclat de la lampe. range : sa portée en mètres — bornée, sinon
    /// un coup tombé sur une voile à un mille éclairerait la nôtre. shadow : ses
    /// ombres portées, qui sont l'essentiel de ce qu'on regarde et coûtent une
    /// carte cubique pendant le quart de seconde du coup.
    /// </summary>
    public double FlashEnergy = 400, FlashRange = 200;
    public bool FlashShadow = true;

    /// <summary>
    /// CE QUE DURE LE COUP, en secondes — et c'est très court.
    ///
    /// La lampe suivait la courbe d'éclat du TRAIT, qui vit un quart de seconde :
    /// le trait doit durer cela, parce qu'on le regarde et qu'un trait d'une
    /// image ne se voit pas. Mais la LUMIÈRE d'un coup de foudre est un coup de
    /// couteau — « un centième de seconde » (signalé) —, et une lueur qui traîne
    /// se lit comme un projecteur qu'on allume.
    ///
    /// C'est une constante de temps, pas une durée : l'éclat tombe en
    /// exp(−âge/flashLife), donc à 0,03 s il ne reste qu'un tiers après deux
    /// images et rien après cinq. Plus court que cela et une machine qui rend à
    /// trente images par seconde manquerait le coup une fois sur deux ; c'est
    /// pourquoi on ne descend pas au centième pour de bon.
    /// </summary>
    public double FlashLife = 0.03;

    /// <summary>
    /// SA COULEUR : un blanc FROID, celui de la lune plutôt que celui d'une
    /// flamme. Un arc est un plasma à vingt-quatre mille degrés, et tout ce
    /// qu'il éclaire prend cette teinte-là ; c'est ce qui le distingue d'un
    /// fanal, et pourquoi un pont aux feux couverts paraît soudain gris acier.
    /// </summary>
    public string FlashColour = "0xccdcff";

    /// <summary>
    /// ET CE QUE LE CIEL EN PREND, de 0 à 1.
    ///
    /// L'éclat du ciel monte l'hémisphérique et blanchit le dôme entier : tout
    /// s'éclaire à la fois, de partout, ce qui est le coup vu de LOIN. À pleine
    /// force il écrasait le coup vu de PRÈS — signalé sur une capture prise au
    /// bon moment : « les éclairs des tempêtes ne doivent pas illuminer autant
    /// tout le ciel ; c'est surtout le bateau, et la lumière vient d'un seul côté
    /// comme un spot qui s'allumerait soudain ».
    ///
    /// C'est exactement ce que fait la lampe du coup (<see cref="FlashEnergy"/>),
    /// et il suffisait de lui laisser la place. Le ciel garde de quoi dire qu'il
    /// s'est passé quelque chose partout — un coup de foudre éclaire réellement
    /// le dessous des nuages — mais il ne fait plus le jour.
    /// </summary>
    public double SkyFlash = 0.30;

    /// <summary>
    /// ET L'AUTRE SORTE D'ÉCLAIR : LE GRAIN LOINTAIN QUI S'ALLUME PAR EN DEDANS.
    ///
    /// Il y a bien deux sortes, et ce n'est pas un oubli (relevé) : un coup
    /// AU-DESSUS DE LA TÊTE éclaire le pont, la toile et le ciel ensemble — c'est
    /// <see cref="SkyFlash"/> et la lampe du trait ; un coup à six milles ne
    /// montre qu'une tache de nuage qui s'allume, sans une ombre qui bouge sur le
    /// bord. Le second ne sort jamais du shader de ciel, et c'est voulu.
    ///
    /// farFlash : l'éclat de cette tache, de 0 à 1. farPerSecond : combien de
    /// coups par seconde d'un grain bien noir (0,34 — un coup toutes les trois
    /// secondes environ).
    /// </summary>
    public double FarFlash = 1.0, FarPerSecond = 0.34;

    public static LightningSettings FromJson(JsonElement j)
    {
        var s = new LightningSettings();
        double D(string n, double v) => j.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : v;
        if (j.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            s.Enabled = en.GetBoolean();
        s.MinInten = D("minInten", s.MinInten); s.PerMinuteAtCore = D("perMinuteAtCore", s.PerMinuteAtCore);
        s.SplitChance = D("splitChance", s.SplitChance); s.WoundChance = D("woundChance", s.WoundChance);
        s.DismastChance = D("dismastChance", s.DismastChance);
        s.FlashEnergy = Math.Max(0, D("flashEnergy", s.FlashEnergy));
        s.FlashRange = Math.Max(5, D("flashRange", s.FlashRange));
        s.SkyFlash = Math.Clamp(D("skyFlash", s.SkyFlash), 0, 1);
        s.FarFlash = Math.Clamp(D("farFlash", s.FarFlash), 0, 1);
        s.FarPerSecond = Math.Max(0, D("farPerSecond", s.FarPerSecond));
        s.FlashLife = Math.Clamp(D("flashLife", s.FlashLife), 0.005, 1);
        if (j.TryGetProperty("flashColour", out var fc) && fc.ValueKind == JsonValueKind.String)
            s.FlashColour = fc.GetString()!;
        if (j.TryGetProperty("flashShadow", out var sh) && (sh.ValueKind == JsonValueKind.True || sh.ValueKind == JsonValueKind.False))
            s.FlashShadow = sh.GetBoolean();
        return s;
    }

    /// <summary>La chance qu'un navire enfoncé de <paramref name="inten"/> soit frappé pendant <paramref name="dt"/> secondes.</summary>
    public double StrikeChance(double inten, double dt)
    {
        if (!Enabled || !(inten > MinInten)) return 0;
        double k = (inten - MinInten) / (1 - MinInten);
        return PerMinuteAtCore * k * dt / 60;
    }
}
