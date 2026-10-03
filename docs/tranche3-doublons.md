# Tranche 3 — les doublons éliminés

Pour comparer anciens et nouveaux fichiers. **Les numéros de ligne « avant » sont
ceux du commit `59decfc`** (tranches 1 et 2), c'est-à-dire :

```bash
git show 59decfc:godot/scripts/LandNode.cs
```

Chemins : `godot/scripts/…` sauf mention `core/…` (`core/NavalSim.Core/`) ou
`shaders/…` (`godot/shaders/`).

Vérifié : compilation sans nouvel avertissement ; parité du noyau identique
octet pour octet à celle d'avant les tranches ; images comparées pixel à pixel
(après-midi et crépuscule, `--fixed-fps 60`) au niveau du bruit ; les 41 shaders
compilés ; performance inchangée (22,4 ms par image à seize navires).

---

## A. Scripts Godot (C#)

### 1. Ouvrir un `.glb` → `Assets.LoadGlb` (`Assets.cs`)
17 copies de « `GltfDocument` + `GltfState` + `AppendFromFile` + `GenerateScene` » :
`CoinNode.cs:85` · `DolphinNode.cs:55` · `FishNode.cs:56` · `FlotsamNode.cs:413` ·
`JettyNode.cs:393` · `KrakenNode.cs:142` · `LandNode.cs:325` · `LandNode.cs:379` ·
`MooredNode.cs:121` · `ShipDemo.DeckFish.cs:94` · `ShipDemo.EditorAdd.cs:287` ·
`ShipNode.cs:135` · `TownNode.Models.cs:80` · `Vat.cs:83` · `WhaleNode.cs:37` ·
`WreckSiteNode.cs:165` · `WreckSiteNode.cs:237`
(chaque appelant garde son propre message d'avertissement)

### 2. La passe de brume → `HazePass` (nouveau `HazePass.cs`)
**2a. `HazePass.New(priorité)`** — 21 créations `new ShaderMaterial { Shader = GD.Load(".../hull_haze.gdshader") … }` :
`AnchorNode.cs:87` · `DolphinNode.cs:50` · `FlotsamNode.cs:361` · `FolkNode.cs:92` ·
`GunFxNode.cs:84` · `JettyNode.cs:52` · `JettyNode.cs:398` · `JettyNode.cs:530` ·
`JettyNode.cs:749` · `KrakenNode.cs:195` · `LandNode.cs:84` · `LandNode.cs:340` ·
`LandNode.cs:399` · `MooredNode.cs:68` · `SerpentNode.cs:31` ·
`ShipDemo.EditorAdd.cs:294` · `ShipNode.cs:381` · `SplinterNode.cs:47` ·
`TownNode.cs:54` · `WhaleNode.cs:43` · `WreckSiteNode.cs:205`

**2b. `HazePass.Wear(maillage, passe)`** — dupliquer chaque matière et la coiffer de la passe :
`LandNode.cs:342-350` · `LandNode.cs:401-408` · `ShipDemo.EditorAdd.cs:303-309`

**2c. `HazePass.Chain(modèle, passe)`** — accrocher la passe au bout de chaque chaîne :
`FlotsamNode.cs:418-433` · `WreckSiteNode.cs:207-222`

*Laissés :* `KrakenNode.cs:196` (copie vers un autre nœud, saute les matières déjà
chaînées) · `WhaleNode.cs:55` (fabrique aussi la baleine blanche) ·
`JettyNode.cs:565` (travaille sur un maillage recuit, pas sur un MeshInstance).

### 3. Parcourir un modèle → `NodeWalk` (nouveau `NodeWalk.cs`)
- **`Bounds`** (boîte englobante) : `FlotsamNode.cs:386-402` (`Box`) · `WreckSiteNode.cs:227-239` (`Box`) ·
  `LandNode.cs:303-315` (`BoxOf`) · `LandNode.cs:373-381` (en ligne) · `ShipDemo.EditorAdd.cs:289-302` (en ligne)
- **`Meshes`** : `LandNode.cs:419-424` (`AllMeshes`) · `MooredNode.cs:84-88` (`Meshes`)
- **`FirstMesh`** : `CoinNode.cs:116-122` · `Vat.cs:474-480` (elles différaient d'un test
  `Mesh != null` : paramètre `withMesh`, chacune garde son comportement)

*Laissé :* `ShipNode.Guns.cs:272` `PieceBox` (travaille sur les sommets).

### 4. Le maillage du plan de formes → `ShipNode.ToArrayMesh` (déjà partagé)
`HullPreview.cs:128-145` — copie mot pour mot supprimée.

### 5. Les couleurs « 0xRRGGBB » → `ColorX` (nouveau `ColorX.cs`)
- `Hex` : `LightningNode.cs:96-101` · `ShipNode.Rig.cs:88-93`
- `Rgb` : `GunFxNode.cs:186` · `AnchorNode.cs:85` · `JettyNode.cs:57`
- `ParseHex` : `ShipNode.Lights.cs:294`

### 6. `Vec3d` ↔ `Vector3` → `VecX.ToGodot()` / `ToCore()` (nouveau `VecX.cs`)
**Vers Godot, 46** `new Vector3((float)v.X, (float)v.Y, (float)v.Z)` :
`AnchorNode.cs:658, 735, 739` · `GrappleNode.cs:85, 86` · `GunFxNode.cs:602` ·
`KrakenNode.cs:261, 262, 274, 275, 276` · `OceanNode.cs:214` · `SeaDemo.cs:200` ·
`ShipDemo.Breakup.cs:99` · `ShipDemo.DeckFish.cs:262` · `ShipDemo.Fire.cs:98, 140, 147` ·
`ShipDemo.Serpent.cs:93` · `ShipDemo.Wreck.cs:27, 37` ·
`ShipDemo.cs:1641, 1997, 2128, 2808, 3282 (×2), 3400, 3401, 3451, 3453, 3629, 4568` ·
`ShipNode.Lights.cs:477` · `ShipNode.Recoil.cs:192` · `ShipNode.Rings.cs:788` ·
`ShipNode.Storm.cs:118` · `ShipNode.cs:594` · `SkyNode.cs:211, 288, 309, 321` ·
`SoundNode.cs:288, 358, 396, 478`
+ les deux fonctions `V()` identiques : `SerpentNode.cs:171` · `ShipDemo.Whale.cs:39`

**Vers le noyau, 12** `new Vec3d(v.X, v.Y, v.Z)` :
`AnchorNode.cs:666` · `CordageNode.cs:113` · `ShipDemo.Editor.cs:349` ·
`ShipDemo.cs:1634, 3143, 3155, 3165` · `ShipNode.Guns.cs:197` ·
`ShipNode.Lights.cs:472, 846 (×2)` · `SplinterNode.cs:144`

### 7. L'interface → `ShipDemo.Ui.cs` (nouveau)
- **Étiquettes → `MkLabel`** : `ShipDemo.Yard.cs:42-54` (la fonction, déplacée) ·
  `ShipDemo.Market.cs:121-128` (`L`) · `ShipDemo.Market.cs:200-208` (`C`) ·
  `ShipDemo.Stow.cs:79-85` (`Hd`) · `ShipDemo.cs:803-810` (`Title`)
- **Cadre des panneaux de droite → `SidePanelStyle`** : `ShipDemo.Fleet.cs:32-40` · `ShipDemo.Market.cs:107-115`
- **Le nom `"font_color"` → `FontColor`** : `ShipDemo.GunSide.cs` (`FontColorName`, ajouté en tranche 2)

*Laissés :* la soixantaine d'autres `new Label` et les 13 autres `StyleBoxFlat`
(propriétés, couleurs ou marges différentes).

### 8. Déplacer le navire → `ShipDemo.Place.cs` (nouveau)
- **Saut au loin → `JumpBy`** : `ShipDemo.Ghosts.cs:140-147` · `ShipDemo.cs:3954-3961`
- **Un point vrai à l'origine → `RecentreOn`** : `ShipDemo.Fishing.cs:275-276` ·
  `ShipDemo.Passage.cs:278-279` · `ShipDemo.Quests.cs:329-330` · `ShipDemo.Save.cs:376-377` ·
  `ShipDemo.Title.cs:415-416` · `ShipDemo.cs:4317-4318`

### 9. `LoadClimate` (`ShipDemo.Weather.cs` désormais)
16 lectures « `TryGetProperty` + `ValueKind == Number` + `GetDouble()` » →
`x.Opt("clé") is double v` (dans `59decfc`, `ShipDemo.cs` vers les lignes 2676-2747).
*Laissée :* la lecture entière `whistle.oneIn` (`GetInt32` lève sur un nombre non
entier : c'est un comportement).

### 10. Le découpage de `ShipDemo.cs` (4 938 → 1 145 lignes)
Déplacement pur, vérifié ligne à ligne (aucune ligne perdue ni ajoutée, hors
en-têtes). Les sections de `59decfc` vont dans :

| nouveau fichier | section d'origine (bandeau dans `ShipDemo.cs`) |
|---|---|
| `ShipDemo.LoadTest.cs` | « LA FLOTTE D'ESSAI » |
| `ShipDemo.Settings.cs` | « LES RÉGLAGES » |
| `ShipDemo.SunPanel.cs` | « LE SOLEIL À LA MAIN » |
| `ShipDemo.Camera.cs` | de la vue fixe (`Plant`) à « LES VUES À BORD » … `UpdateCamera` |
| `ShipDemo.Input.cs` | `ReadKeys`, `UpdateInfo`, `_UnhandledInput` |
| `ShipDemo.Weather.cs` | « LE TEMPS QU'IL FAIT », « CE QUI TOMBE », puis `SetAutoWeather` … `GoToStorm` |
| `ShipDemo.Storm.cs` | « CE QUI VIENT AVEC LA TEMPÊTE », « PORTER DE LA TOILE COÛTE DE LA TOILE » |
| `ShipDemo.Guns.cs` | « LES GROSSES PIÈCES » |
| `ShipDemo.Pirates.cs` | « QUI SE BAT, ET POURQUOI » … `SpawnPirate` |
| `ShipDemo.Harbour.cs` | `RefreshJetties` … `BackToBerth` (pontons, livres, havre, mouillage) |
| `ShipDemo.Capture.cs` | « la capture en ligne de commande » jusqu'à la fin |
| `ShipDemo.Hud.cs` (ajout) | `CompassTick` |

Reste dans `ShipDemo.cs` : `_Ready`, le démarrage, la scène, les solveurs,
`Launch`, `_Process`, `Restate`.

---

## B. Noyau (`core/NavalSim.Core`)

### 11. Le cap au compas → `Compass.HeadingDeg` (nouveau `Compass.cs`)
`ShipDemo.Hud.cs:119` · `ShipDemo.Reckoning.cs:67` · `ShipDemo.Save.cs:147` ·
`ShipDemo.Save.cs:176` · `ShipDemo.cs:4407`
*Laissés :* `ShipNode.cs:636` (sans le `+360 % 360`) · `ShipDemo.Serpent.cs:53` et
`ShipDemo.Whale.cs:66` (relèvement relatif, pas un cap) · `core/AutoHelm.cs:65` (en radians).

### 12. Cap → lacet → `Compass.YawOf`
`JettyNode.cs:648` · `MooredNode.cs:226` · `ShipDemo.Passage.cs:281` ·
`ShipDemo.Save.cs:379` · `ShipDemo.Save.cs:404` · `ShipDemo.cs:4315`

### 13. La longueur horizontale → `Vec3d.LengthXZ` (`Vec3d.cs`)
32 `Math.Sqrt(v.X * v.X + v.Z * v.Z)` :
`core/Dolphins.cs:152, 155` · `core/Kraken.cs:213` · `core/ShipPhysics.Step.cs:794, 898, 1056` ·
`core/Sky.cs:125` · `core/WreckAir.cs:155, 268` · `FlotsamNode.cs:162` · `OceanNode.cs:221` ·
`ShipDemo.Boat.cs:68, 187, 194` · `ShipDemo.Dive.cs:149` · `ShipDemo.Encounters.cs:367` ·
`ShipDemo.Fishing.cs:97` · `ShipDemo.Fleet.cs:187` · `ShipDemo.Hud.cs:120` ·
`ShipDemo.Market.cs:337` · `ShipDemo.Quests.cs:298` · `ShipDemo.Rumour.cs:105` ·
`ShipDemo.Save.cs:148, 177` · `ShipDemo.Teleport.cs:156` · `ShipDemo.Whale.cs:83` ·
`ShipDemo.cs:1224, 1686, 2219, 4882, 4885, 4944`

### 14. Les constantes écrites en dur
- **Le mille `1852` → `Config.Mile`** (qui reprend `Geo.MPerMin`, la définition existante) :
  `core/Quest.cs:281, 344` · `CompassNode.cs:127` · `ShipDemo.Encounters.cs:151, 380 (×2)` ·
  `ShipDemo.Hud.cs:139` · `ShipDemo.Quests.cs:277` · `ShipDemo.Reckoning.cs:48` ·
  `ShipDemo.Save.cs:201` · `ShipDemo.Teleport.cs:154` · `ShipDemo.cs:3965`
- **Les nœuds `1.94384` → `Config.MsToKn`** (existait déjà) :
  `ShipDemo.Fishing.cs:122` · `ShipDemo.Fleet.cs:187` · `ShipDemo.Market.cs:350` · `ShipDemo.Teleport.cs:156`
- **La pesanteur `9.81` → `Config.G`** (existait déjà ; `9.81f` → `(float)Config.G`, identique au bit) :
  `core/Cordage.cs:157` · `core/Gunnery.cs:488` · `core/Pendulum.cs:46, 51` ·
  `core/SprayPool.cs:94, 165` · `core/WreckAir.cs:92` · `AnchorNode.cs:330, 469, 580 (×2)` ·
  `GunFxNode.cs:630` · `ShipDemo.DeckFish.cs:268` · `ShipNode.Damage.cs:94` · `SplinterNode.cs:138`

### 15. La rampe douce (smoothstep) → `MathX.Smooth01` / `MathX.SmoothStep` (nouveau `MathX.cs`)
- **Fonctions supprimées** : `core/HullLines.cs:29-33` (`Smooth01`) · `core/ShipPhysics.Step.cs:465-469` (`SmoothStep`) ·
  `core/Fishing.cs:114-118` (`Smooth`) · `ShipNode.Rings.cs:764-768` (`Ease`)
- **Copies en ligne** : `core/ShelterMap.cs:95-96` · `core/ShipPhysics.Step.cs:244-245` ·
  `core/World.cs:518-519` · `core/World.cs:598-599` · `core/Market.cs:187` · `core/Treasure.cs:114` ·
  `core/Storms.cs:138-139` · `SkyNode.cs:108-109` · `GroundPaint.cs:119` · `ShipNode.Recoil.cs:247`

*Laissées*, la variable n'y est pas bornée à [0, 1] sur place (la rampe changerait le
résultat) : `core/Gulls.cs:82` · `core/Sky.cs:259` · `core/World.cs:551` ·
`ShipNode.Damage.cs:500` · `ShipDemo.FlyBy.cs:99` · `ShipNode.Recoil.cs:246` ·
`ShipDemo.cs:4868` ; et `GunFxNode.cs:812` (en `float`).

### 16. Hypoténuse et tirage gaussien → `MathX.Hyp` / `MathX.Gauss`
- `Hyp` : `core/Grapple.cs:282` · `core/Pirate.cs:195` · `core/Storms.cs:158` (`Hypot`)
- `Gauss` : `core/Reckoning.cs:78-79` · `core/Weather.cs:89-90` (mêmes tirages, dans le même ordre)

### 17. Lire une fiche JSON → `Js.Str` / `Js.Num` / `Js.Opt` / `Js.True` (ajoutés à `core/Js.cs`, qui existait)
- **Accesseurs supprimés** : `core/Market.cs:96-97` (`Txt`) · `core/Quest.cs:137-140` (`Str`, `Opt`) ·
  `core/Quest.cs:209-210` (`Str`) · `core/World.cs:279` (`Str`) · `core/World.cs:281` (`Num`) ·
  `core/World.cs:282` (`Bool`) · `ShipLibrary.cs:185-186` (`Txt`)
- **Fonctions locales « nombre ou défaut »** qui délèguent désormais à `Js.Num` / `Js.Str`
  (leur nom court reste, pour ne pas toucher aux centaines d'appels) :
  `core/Caustics.cs:38` · `core/Climate.cs:27` · `core/Dolphins.cs:28` · `core/Encounters.cs:52` ·
  `core/Fire.cs:52` · `core/Fish.cs:52` · `core/Fishing.cs:47` · `core/Ghosts.cs:27` ·
  `core/Gunnery.cs:187` · `core/Kraken.cs:32` · `core/Lightning.cs:117` · `core/Reckoning.cs:22` ·
  `core/Reckoning.cs:157` · `core/SeaFog.cs:21` · `core/SeaSerpent.cs:30` · `core/Snow.cs:33` ·
  `core/Treasure.cs:63, 64` · `core/Whale.cs:32` · `core/Wraith.cs:37` · `FlotsamNode.cs:122` ·
  `ShipDemo.cs:2609` · `ShipDemo.Crew.cs:139`

---

## C. Shaders (`godot/shaders`)

Nouveaux : `naval_hash.gdshaderinc` (hachages à base de sinus, avec garde
d'inclusion) et `town_windows.gdshaderinc` (ce que les deux shaders de fenêtres
de la ville partagent).

### 18. Bruit de valeur 2D → `naval_value_noise(p, graine)` (`sky.gdshaderinc`)
`naval_noise2` (`sky.gdshaderinc:192`, graine 7) · `onde_hash` + `onde_noise`
(`ocean.gdshader:225-233`, graine 3) · `naval_grain` (`sail.gdshader:84-92`, graine 0)

### 19. Fresnel de Schlick → `naval_schlick` (`ocean.gdshader`)
`ocean.gdshader:519` · `:626` · `:649` · `:690`

### 20. Lobe GGX → `naval_ggx` (`ocean.gdshader`)
`ocean.gdshader:619-620` (soleil) · `:647-648` (lune) · `:688-689` (lanternes)

### 21. Phase de Gerstner → `gerstner_phase(i, xz, t)` (`gerstner.gdshaderinc`)
`gerstner.gdshaderinc:131` (et son `omega` devenu inutile, `:109`) ·
`gerstner.gdshaderinc:217` · `foam_field.gdshader:89` (et son `omega`, `:87`)

### 22. Hachages → `naval_hash.gdshaderinc`
- `naval_hash_sin3` : `scar_hash` (`ship_scar.gdshaderinc:34`) · `naval_snow_hash` (`ship_snow.gdshaderinc:15-17`)
- `naval_hash_sin2` : `hash1` (`beads.gdshaderinc:7`) · `boil_hash` (`foam_field.gdshader:43`)
- `naval_rand` : `grain` (`fish.gdshader:64`) · `grain` (`gull.gdshader:38`)

### 23. La ville → `town_windows.gdshaderinc`
- `hash21` : `town.gdshader:28` · `town_glass.gdshader:28`
- les 4 uniformes `u_night`, `u_warm`, `u_gain`, `u_lit_share` : `town.gdshader:19-23` · `town_glass.gdshader:16-19`

### 24. Constante morte
`LAMP_COL` (`ocean.gdshader:103-106`), jamais lue.

*Laissés (variantes, pas des doublons)* : `naval_burn_grain` (`sail`) contre
`naval_fbm` (`sky`), constantes différentes · `onde_fbm` (`ocean`) · le bruit de
`chart.gdshader:21` (`43758.5` au lieu de `43758.5453`) · `paint_noise` /
`paint_hash22` (`ground_paint`, une autre famille de hachage) · `naval_hash13`
(`sky`, sans sinus) · les `.glsl` du compositor (`underwater.glsl`,
`anamorphic_dof.glsl`) : compilés par RenderingDevice, ils ne peuvent pas inclure
un `.gdshaderinc`.

---

## D. Non touché, à décider

- **`ShipPhysics.WorsenBreach()`** (`core/ShipPhysics.cs:587`) n'a aucun appelant
  sous Godot, mais c'est le portage d'une commande de la page
  (`controls.onBreach`, une touche qui ouvre une voie d'eau) : pas un doublon,
  donc gardé. À brancher ou à retirer.
- **Le mille et l'échelle** : `ShipDemo.Reckoning.cs:48` multiplie le mille par
  `Region.Scale` (le monde à 0,4), les onze autres usages non. C'est une règle de
  jeu à trancher, pas une factorisation.
- **`RecentreOn`** ne fait glisser que la mer (comme les six copies d'avant), pas
  l'écume, les autres coques ni les bêtes, que la boucle d'origine flottante de
  `_Process` décale toutes. Peut-être voulu (un saut emmène tout le monde).
- **Signalés par l'agent des shaders** : le disque du soleil a deux valeurs
  (`sky_dome` et la mer) ; la luminance Rec.709 est écrite deux fois
  (`mist.gdshader:108`, `sail.gdshader:165`) ; `u_deep` est déclaré deux fois
  (`ocean.gdshader:45`, `sea_far.gdshader:21`). Y toucher changerait l'image.
- **Les scènes de nuit ne sont pas déterministes** : `Climate.cs:67` et
  `Weather.cs:57` tirent leur hasard sans graine (`new Random()`), une averse
  tombe ou non d'une course à l'autre. Pour comparer des images, il faut deux
  captures de chaque côté.
