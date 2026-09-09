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

  // ---- how many hulls the sea and the foam can carry at once ----
  MAX_SHIPS: 4,

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
  SHIPS: ['ships/barge.json', 'ships/cotre.json', 'ships/frigate.json',
          'ships/frigate17e.json', 'ships/pirate.json', 'ships/schooner.json'],

  BEAUFORT: [
    [0,'Calme','0.0'],[1,'Très légère','0.1'],[2,'Belle vaguelette','0.3'],
    [3,'Petites vagues','0.8'],[4,'Belle brise','1.6'],[5,'Vagues modérées','2.5'],
    [6,'Grosse mer','3.5'],[7,'Mer très grosse','5.0'],[8,'Coup de vent','7.0'],
    [9,'Tempête','9.0']
  ]
};
