/* LE CORPS DU RUBAN, partagé par ses deux variantes — voir rope_ribbon.gdshader
 * (mélangé) et rope_ribbon_taa.gdshader (opaque, tramé), ShipNode.Ropes.cs. */
// a ship's rigging, pushed with her every frame: the sky goes global (sky.gdshaderinc)
#define NAVAL_SKY_GLOBAL
#include "res://shaders/ship_haze.gdshaderinc"

uniform vec3 u_color : source_color = vec3(0.29, 0.22, 0.16);   // chanvre goudronné, brun
uniform float u_min_px = 1.3;      // jamais plus mince que ceci à l'écran
/* LES TORONS, dessinés ici (demandé : « une texture de corde même basique ») : un cordage
   commis est fait de trois torons tordus en hélice, chacun faisant un tour en trois à quatre
   diamètres. UV2.x porte le chemin parcouru le long de la corde, EN DIAMÈTRES (ShipNode.Ribbon),
   si bien qu'une aussière a de longs torons et une ligne de courts. */
uniform float u_strands = 3.0;     // torons
uniform float u_lay = 3.5;         // diamètres pour un tour d'un toron
uniform float u_strand_gain = 1.0; // 0 : la corde unie d'avant

varying float v_fade;
varying float v_side;
varying vec3 v_dir;
varying vec3 v_world;
varying float v_along;
varying vec3 v_axis;

void vertex() {
	vec4 pv = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec3 tv = (MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
	// l'échelle de la pièce (uniforme) : le rayon est écrit dans son repère
	float scale = length(MODELVIEW_MATRIX[0].xyz);
	tv = dot(tv, tv) < 1e-12 ? vec3(0.0, 1.0, 0.0) : normalize(tv);
	vec3 eye = normalize(pv.xyz);
	vec3 dir = cross(tv, eye);
	float l = length(dir);
	dir = l < 1e-4 ? vec3(1.0, 0.0, 0.0) : dir / l;
	// mètres par pixel à cette profondeur, lus sur la projection et la taille de l'écran
	float mpp = 2.0 * max(0.001, -pv.z) / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);
	float r = UV.y * scale;
	float w = max(r, u_min_px * 0.5 * mpp);
	v_fade = r / w;
	v_side = UV.x;
	v_dir = dir;
	v_along = UV2.x;
	v_axis = tv;
	pv.xyz += dir * (w * UV.x);
	v_world = (INV_VIEW_MATRIX * pv).xyz;
	VERTEX = pv.xyz;
	NORMAL = -eye;
}

void fragment() {
	// la normale d'un cylindre vu de face, de bord à bord du ruban
	float s = clamp(v_side, -1.0, 1.0);
	vec3 n = v_dir * s + VIEW * sqrt(max(0.0, 1.0 - s * s));
	/* Le point de la circonférence que l'on voit, en tours (asin : la face vue va de −¼ à +¼),
	   plus le chemin le long de la corde en tours d'hélice : leur somme, fois le nombre de
	   torons, compte les torons — sa partie fractionnaire est la place dans le toron. Les
	   lignes de creux courent donc en biais, comme sur une vraie corde. */
	float c = u_strands * (v_along / u_lay + asin(s) * 0.15915494);
	float x = fract(c);
	float bulge = sin(3.14159265 * x);
	// plus fins qu'un pixel et demi, les torons scintilleraient : ils s'effacent vers la teinte moyenne
	float seen = u_strand_gain * (1.0 - smoothstep(0.35, 0.7, fwidth(c)));
	// le creux entre deux torons, sombre ; la moyenne ramenée à 1, pour garder la teinte de loin
	float shade = mix(0.5, 1.0, smoothstep(0.0, 0.45, bulge)) / 0.927;
	// le toron bombé penche la normale le long de la corde, d'un côté puis de l'autre
	n += v_axis * cos(3.14159265 * x) * 0.45 * seen;
	NORMAL = normalize(n);
	ALBEDO = u_color * mix(1.0, shade, seen);
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
#ifdef RIBBON_HASHED
	/* OPAQUE ET TRAMÉ : la pâleur au loin devient une part de pixels dessinés, que le
	   TAA moyenne d'une image à l'autre — et le ruban écrit sa profondeur ET son
	   mouvement, que le TAA suit au lieu de l'étaler comme le ciel derrière lui. */
	ALPHA = v_fade;
	ALPHA_HASH_SCALE = 1.0;
#else
	ALPHA = v_fade;
#endif
	// l'air entre l'œil et elle, comme sur le reste du navire (hull_haze.gdshader)
	FOG = vec4(ship_haze_colour(CAMERA_POSITION_WORLD, v_world), pow(haze_along(CAMERA_POSITION_WORLD, v_world), 1.5));
}
