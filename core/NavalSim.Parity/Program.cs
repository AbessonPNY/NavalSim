using System.Text.Json;
using NavalSim.Core;

// Le banc de PARITE : recalcule en C# ce que le JS a releve, et compare.
//
// C'est l'invariant « un seul plan de formes » etendu au portage. Tant qu'il
// passe, les deux decrivent la meme coque. S'ils divergent, ce qu'on verra sous
// Godot cessera de correspondre a ce qui flotte -- et rien a l'ecran ne le dira,
// ce qui est exactement la panne silencieuse que ce projet redoute le plus.

string root = AppContext.BaseDirectory;
string dumpPath = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "parity.json"));
string shipsDir = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..", "ships"));

if (!File.Exists(dumpPath))
{
    Console.Error.WriteLine($"releve introuvable : {dumpPath}");
    Console.Error.WriteLine("lancer d'abord :  node tools/parity-dump.js");
    return 1;
}

using var doc = JsonDocument.Parse(File.ReadAllText(dumpPath));

/* DEUX tolerances, et les confondre rendait le banc illisible.

   Les COURBES sont comparees en double des deux cotes : deckY, keelY, halfB et
   beamFactor sortent d'une formule et rien ne les arrondit. Tout ecart au-dela
   du bruit de la conversion decimale du JSON y est une divergence de FORMULE,
   qui est precisement ce que ce banc existe pour attraper.

   Le MAILLAGE, lui, est stocke en flottant 32 bits des deux cotes -- three.js
   par Float32BufferAttribute, le portage par float[]. Or `Math.Pow` de .NET et
   `Math.pow` de V8 ne sont pas tenus de s'accorder sur le dernier bit, et ils ne
   s'accordent pas. Un ecart de 1e-16 en relatif est rigoureusement invisible en
   double, mais il fait parfois basculer l'arrondi d'un cran de float32 -- soit
   1,2e-7 en relatif, ce qu'on mesure. Exiger 1e-9 la-dessus, ce serait exiger
   que deux bibliotheques mathematiques soient le meme code. */
const double TolCurve = 1e-9;
const double TolMesh = 1e-5;

int shipsChecked = 0, failures = 0;
double worstCurveAll = 0, worstMeshAll = 0;
string worstCurveWhere = "", worstMeshWhere = "";

foreach (var entry in doc.RootElement.EnumerateObject())
{
    string id = entry.Name;
    var rec = entry.Value;

    string specFile = Directory.GetFiles(shipsDir, "*.json")
        .FirstOrDefault(f => Path.GetFileName(f) != "index.json"
                          && JsonDocument.Parse(File.ReadAllText(f))
                                 .RootElement.GetProperty("id").GetString() == id) ?? "";
    if (specFile.Length == 0) { Console.Error.WriteLine($"  {id}: fiche introuvable"); failures++; continue; }

    var spec = ShipSpec.FromJson(File.ReadAllText(specFile));
    var hl = new HullLines(spec);

    double worstCurve = 0, worstMesh = 0;
    string whereCurve = "", whereMesh = "";

    void Check(string what, double js, double cs)
    {
        double d = Math.Abs(js - cs);
        if (d > worstCurve) { worstCurve = d; whereCurve = what; }
    }
    void CheckMesh(string what, double js, double cs)
    {
        double d = Math.Abs(js - cs);
        if (d > worstMesh) { worstMesh = d; whereMesh = what; }
    }

    // --- les grandeurs derivees par ShipSpec ---
    var s = rec.GetProperty("spec");
    Check("L", s.GetProperty("L").GetDouble(), spec.L);
    Check("B", s.GetProperty("B").GetDouble(), spec.B);
    Check("keel", s.GetProperty("keel").GetDouble(), spec.Keel);
    Check("D", s.GetProperty("D").GetDouble(), spec.D);
    Check("deckMid", s.GetProperty("deckMid").GetDouble(), spec.DeckMid);
    Check("massKg", s.GetProperty("massKg").GetDouble(), spec.MassKg);
    Check("cogY", s.GetProperty("cogY").GetDouble(), spec.Cog.Y);
    Check("cogZ", s.GetProperty("cogZ").GetDouble(), spec.Cog.Z);
    Check("drag", s.GetProperty("drag").GetDouble(), spec.Drag);
    Check("lateralLinear", s.GetProperty("lateralLinear").GetDouble(), spec.LateralLinear);
    Check("lateralQuad", s.GetProperty("lateralQuad").GetDouble(), spec.LateralQuad);
    Check("heaveDamp", s.GetProperty("heaveDamp").GetDouble(), spec.HeaveDamp);
    Check("topSpeed", s.GetProperty("topSpeed").GetDouble(), spec.TopSpeed);
    Check("maxThrust", s.GetProperty("maxThrust").GetDouble(), spec.MaxThrust);
    Check("sternPower", s.GetProperty("sternPower").GetDouble(), spec.SternPower);
    Check("rudderK", s.GetProperty("rudderK").GetDouble(), spec.RudderK);
    Check("rudderMax", s.GetProperty("rudderMax").GetDouble(), spec.RudderMax);
    Check("rudderZ", s.GetProperty("rudderZ").GetDouble(), spec.RudderZ);
    Check("rudderY", s.GetProperty("rudderY").GetDouble(), spec.RudderY);
    Check("sailArea", s.GetProperty("sailArea").GetDouble(), spec.SailArea);
    Check("ceHeight", s.GetProperty("ceHeight").GetDouble(), spec.CeHeight);
    Check("ceZ", s.GetProperty("ceZ").GetDouble(), spec.CeZ);
    Check("maxSheet", s.GetProperty("maxSheet").GetDouble(), spec.MaxSheet);
    Check("jibFootZ", s.GetProperty("jibFootZ").GetDouble(), spec.JibFootZ);

    var mastZ = s.GetProperty("mastZ");
    if (mastZ.GetArrayLength() != spec.Masts.Count)
    {
        Console.Error.WriteLine($"  {id}: {mastZ.GetArrayLength()} mats cote JS, {spec.Masts.Count} cote C#");
        failures++;
    }
    else
        for (int i = 0; i < spec.Masts.Count; i++)
            Check($"mastZ[{i}]", mastZ[i].GetDouble(), spec.Masts[i].Z);

    // --- le plan de formes, station par station ---
    void Curve(string name, Func<double, double> f)
    {
        var arr = rec.GetProperty(name);
        int n = arr.GetArrayLength() - 1;
        for (int i = 0; i <= n; i++)
            Check($"{name}({(double)i / n:F4})", arr[i].GetDouble(), f((double)i / n));
    }
    Curve("deckY", hl.DeckY);
    Curve("keelY", hl.KeelY);
    Curve("halfB", hl.HalfB);
    Curve("beamFactor", hl.BeamFactor);

    // --- et le maillage entier, qui est ce que l'oeil verra ---
    var mesh = rec.GetProperty("mesh");
    var jsPos = mesh.GetProperty("positions");
    var jsIdx = mesh.GetProperty("indices");
    var built = hl.BuildGeometry();

    if (jsPos.GetArrayLength() != built.Positions.Length)
    {
        Console.Error.WriteLine($"  {id}: {jsPos.GetArrayLength() / 3} sommets cote JS, "
                              + $"{built.Positions.Length / 3} cote C#");
        failures++;
    }
    else
        for (int i = 0; i < built.Positions.Length; i++)
            CheckMesh($"mesh.pos[{i}]", jsPos[i].GetDouble(), built.Positions[i]);

    if (jsIdx.GetArrayLength() != built.Indices.Length)
    {
        Console.Error.WriteLine($"  {id}: {jsIdx.GetArrayLength() / 3} triangles cote JS, "
                              + $"{built.Indices.Length / 3} cote C#");
        failures++;
    }
    else
        for (int i = 0; i < built.Indices.Length; i++)
            if (jsIdx[i].GetInt32() != built.Indices[i])
            {
                Console.Error.WriteLine($"  {id}: index {i} differe "
                                      + $"({jsIdx[i].GetInt32()} contre {built.Indices[i]})");
                failures++;
                break;
            }

    bool ok = worstCurve <= TolCurve && worstMesh <= TolMesh;
    if (!ok) failures++;
    if (worstCurve > worstCurveAll) { worstCurveAll = worstCurve; worstCurveWhere = $"{id}/{whereCurve}"; }
    if (worstMesh > worstMeshAll) { worstMeshAll = worstMesh; worstMeshWhere = $"{id}/{whereMesh}"; }
    shipsChecked++;

    Console.WriteLine($"  {(ok ? "OK   " : "ECART")} {id,-14}"
                    + $"  courbes {worstCurve:E2}   maillage {worstMesh:E2}");
}

Console.WriteLine();
Console.WriteLine($"{shipsChecked} navires. Par coque : 4 courbes x 65 stations, "
                + "195 sommets et 780 triangles.");
Console.WriteLine($"  courbes  (double, tol {TolCurve:E0})   pire ecart {worstCurveAll:E2}"
                + (worstCurveAll > 0 ? $"  {worstCurveWhere}" : "  -- identique au bit pres"));
Console.WriteLine($"  maillage (float32, tol {TolMesh:E0})  pire ecart {worstMeshAll:E2}"
                + (worstMeshAll > 0 ? $"  {worstMeshWhere}" : ""));
Console.WriteLine();
Console.WriteLine(failures == 0
    ? "PARITE TENUE -- le C# et le JS decrivent la meme coque."
    : $"{failures} DIVERGENCE(S) sur le plan de formes.");

Console.WriteLine();
string oceanDump = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "parity-ocean.json"));
int seaFailures = NavalSim.Parity.OceanParity.Run(oceanDump);

Console.WriteLine();
Console.WriteLine(seaFailures == 0
    ? "PARITE TENUE -- le C# et le JS voient la meme mer."
    : $"{seaFailures} DIVERGENCE(S) sur la mer.");

Console.WriteLine();
string physDump = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "parity-physics.json"));
int physFailures = NavalSim.Parity.PhysicsParity.Run(physDump, shipsDir);

Console.WriteLine();
string settleDump = Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "parity-settle.json"));
physFailures += NavalSim.Parity.PhysicsParity.RunSettle(settleDump, shipsDir);

Console.WriteLine();
Console.WriteLine(physFailures == 0
    ? "PARITE TENUE -- le C# et le JS integrent la meme coque."
    : $"{physFailures} DIVERGENCE(S) sur le solveur.");

return failures == 0 && seaFailures == 0 && physFailures == 0 ? 0 : 1;
