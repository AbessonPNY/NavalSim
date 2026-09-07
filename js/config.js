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

  // ---- ocean mesh ----
  NWAVES: 6,
  OCEAN_SIZE: 2600,
  OCEAN_SEG: 320,

  CAM_NAMES: ['Poursuite','Orbite','Passerelle','Fixe'],

  SHIPS: ['ships/schooner.json', 'ships/frigate.json', 'ships/barge.json'],

  BEAUFORT: [
    [0,'Calme','0.0'],[1,'Très légère','0.1'],[2,'Belle vaguelette','0.3'],
    [3,'Petites vagues','0.8'],[4,'Belle brise','1.6'],[5,'Vagues modérées','2.5'],
    [6,'Grosse mer','3.5'],[7,'Mer très grosse','5.0'],[8,'Coup de vent','7.0'],
    [9,'Tempête','9.0']
  ]
};
