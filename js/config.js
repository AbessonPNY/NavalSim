/* Constants of the world, not of any one vessel.
   Everything that varies from ship to ship now lives in ships/*.json and is
   read through ShipSpec. */
window.Naval = window.Naval || {};

Naval.Config = {
  // ---- physical constants ----
  RHO: 1025.0,      // sea water density (kg/m³)
  G: 9.81,          // gravity (m/s²)
  RHO_AIR: 1.225,
  MS_TO_KN: 1.94384,

  // ---- buoyancy probe grid (cells across the hull envelope) ----
  PN_Z: 11, PN_X: 7, PN_Y: 7,

  /* How many hulls the sea and the foam can carry at once.

     Not a free number: it sizes the uniform ARRAYS both shaders index, the
     NSHIP they are compiled against, and the number of rows in the hull-profile
     texture. Raising it costs a little work in every fragment of sea, whether
     the hulls are there or not.

     Eight is where the measurement put the ceiling rather than where it felt
     right — see « Combien de coques » in CLAUDE.md. At eight she is exactly on
     the 60 fps budget with occasional overruns; six is the comfortable number
     and four was the old cap. */
  MAX_SHIPS: 8,

  /* CE QUE LA TOILE SUPPORTE, en newtons par mètre carré, et c'est une mesure
     plutôt qu'un réglage. Relevé sur le galion pirate à pleine voilure, bordée
     au mieux, quarante secondes par état de mer :

       force   5     6     7     8     9    11
       N/m²    89   154   215   175   437   561

     On ferlait à force 7, et c'est très exactement là que la pression franchit
     deux cents. Le seuil n'a donc pas été choisi : il a été trouvé, et il tombe
     où l'histoire le met.

     Une PRESSION et non une force, ce qui est le point : de la toile cède au
     newton par mètre carré, pas au newton. C'est pour cela que ferler ne
     protège pas ce qui reste dehors — le ris réduit la surface exposée, donc le
     NOMBRE d'occasions de déchirer, jamais la tension sur ce qui porte encore.
     Même famille que les coefficients hydro, qui sont par unité de surface pour
     servir une goélette de 24 m comme une frégate de 60. */
  CANVAS_STRENGTH: 220,

  /* How far she may stray from local zero before the world is slid back under
     her. Small enough that the Gerstner phase keeps its precision, large enough
     that rebasing is rare — 1500 m is about five minutes at hull speed. */
  REBASE_RADIUS: 1500,

  // ---- watertight compartments, along her length ----
  NCOMP: 5,

  // ---- ocean mesh ----
  // GPU carries the full spectrum; the solver and the foam pass take only the
  // longest components, which hold nearly all of a JONSWAP spectrum's energy.
  NWAVES: 18,
  NWAVES_CPU: 10,
  NWAVES_FOAM: 7,
  OCEAN_SIZE: 7000,
  OCEAN_SEG: 384,

  CAM_NAMES: ['Proue','Orbite','Passerelle','Fixe'],

  /* Last-resort list, used only when ships/index.json is missing too — the dev
     server lists the folder live and the build writes that index, so this is
     reached only on a static host that never got one. It must still match the
     folder: build.js compares the two and says so if they have drifted. */
  SHIPS: ['ships/barge.json', 'ships/bouee-canard.json', 'ships/cotre.json',
          'ships/frigate.json', 'ships/frigate17e.json', 'ships/pirate.json',
          'ships/schooner.json'],

  BEAUFORT: [
    [0,'Calme','0.0'],[1,'Très légère','0.1'],[2,'Belle vaguelette','0.3'],
    [3,'Petites vagues','0.8'],[4,'Belle brise','1.6'],[5,'Vagues modérées','2.5'],
    [6,'Grosse mer','3.5'],[7,'Mer très grosse','5.0'],[8,'Coup de vent','7.0'],
    [9,'Tempête','9.0']
  ]
};
