using Godot;

namespace NavalSim;

/// <summary>
/// LES NOMS D'UNIFORMES, CRÉÉS UNE FOIS.
///
/// Écrire `SetShaderParameter("u_time", …)` avec une chaîne fabrique à chaque
/// appel un StringName neuf — un objet C# qui porte un finaliseur. Il y en avait
/// plus de cent par image, pour la mer, le ciel, l'écume et chaque matériau du
/// navire : vingt kilo-octets par image, des objets qui survivent aux petites
/// collectes à cause de leur finaliseur, et au bout du compte une collecte
/// complète qui gèle une image. Mesuré avant correction : 20 Ko alloués par
/// image dans _Process, une collecte de génération 2 en vingt-cinq secondes, et
/// une image de 31 ms au milieu d'images de 1,4. C'était l'à-coup « quasi
/// imperceptible » signalé à l'usage.
///
/// Un nom, une fois ; toute écriture d'uniforme passe par ici.
/// </summary>
public static class U
{
    public static readonly StringName LampReflection = new("u_lamp_reflection");
    public static readonly StringName LampWater = new("u_lamp_water");
    public static readonly StringName Lamp = new("u_lamp");
    public static readonly StringName LampRange = new("u_lamp_range");
    public static readonly StringName LampCol = new("u_lamp_col");
    public static readonly StringName LampSize = new("u_lamp_size");
    public static readonly StringName LampAim = new("u_lamp_aim");
    public static readonly StringName LampCount = new("u_lamp_count");
    public static readonly StringName WaterLight = new("u_water_light");
    public static readonly StringName Moon = new("u_moon");
    public static readonly StringName MoonLit = new("u_moon_lit");
    public static readonly StringName MoonDisc = new("u_moon_disc");
    public static readonly StringName MoonPhase = new("u_moon_phase");
    public static readonly StringName Dazzle = new("u_dazzle");
    public static readonly StringName Snow = new("u_snow");
    // ce qui tombe : la pluie et la neige
    public static readonly StringName Eye = new("u_eye"), Slant = new("u_slant"), Len = new("u_len"),
        Amount = new("u_amount"), Drift = new("u_drift"), Scale = new("u_scale"), Box = new("u_box");
    public static readonly StringName Color = new("u_color");
    public static readonly StringName Opacity = new("u_opacity");
    public static readonly StringName Fixed = new("u_fixed");
    public static readonly StringName Albedo = new("u_albedo");
    public static readonly StringName AmpMax = new("u_amp_max");
    public static readonly StringName Canvas = new("u_canvas");
    public static readonly StringName Centre = new("u_centre");
    public static readonly StringName Cloud = new("u_cloud");
    public static readonly StringName Decay = new("u_decay");
    public static readonly StringName Emissive = new("u_emissive");
    public static readonly StringName Flash = new("u_flash");
    public static readonly StringName FoamOn = new("u_foam_on");
    public static readonly StringName FoamOrigin = new("u_foam_origin");
    public static readonly StringName FoamSize = new("u_foam_size");
    public static readonly StringName FoamTex = new("u_foam_tex");
    public static readonly StringName Half = new("u_half");
    public static readonly StringName HasMap = new("u_has_map");
    public static readonly StringName Haze = new("u_haze");
    public static readonly StringName HazeH = new("u_haze_h");
    public static readonly StringName Horizon = new("u_horizon");
    public static readonly StringName HullEnds = new("u_hull_ends");
    public static readonly StringName HullProf = new("u_hull_prof");
    public static readonly StringName Map = new("u_map");
    public static readonly StringName OffsetUv = new("u_offset_uv");
    public static readonly StringName Origin = new("u_origin");
    public static readonly StringName Prev = new("u_prev");
    public static readonly StringName Ripple = new("u_ripple");
    public static readonly StringName Roughness = new("u_roughness");
    public static readonly StringName Seg = new("u_seg");
    public static readonly StringName Sharp = new("u_sharp");
    public static readonly StringName ShipAfloat = new("u_ship_afloat");
    public static readonly StringName ShipCount = new("u_ship_count");
    public static readonly StringName ShipFwd = new("u_ship_fwd");
    public static readonly StringName ShipHalf = new("u_ship_half");
    public static readonly StringName ShipPos = new("u_ship_pos");
    public static readonly StringName ShipSpeed = new("u_ship_speed");
    public static readonly StringName ShipTop = new("u_ship_top");
    public static readonly StringName Size = new("u_size");
    public static readonly StringName SkyTime = new("u_sky_time");
    public static readonly StringName Storm = new("u_storm");
    public static readonly StringName StormDir = new("u_storm_dir");
    public static readonly StringName StormFlash = new("u_storm_flash");
    public static readonly StringName StormFlashDir = new("u_storm_flash_dir");
    public static readonly StringName StormLoom = new("u_storm_loom");
    public static readonly StringName Sun = new("u_sun");
    public static readonly StringName SunCol = new("u_sun_col");
    public static readonly StringName Texel = new("u_texel");
    public static readonly StringName Time = new("u_time");
    public static readonly StringName WaveA = new("u_wave_a");
    public static readonly StringName WaveB = new("u_wave_b");
    public static readonly StringName WavePhase = new("u_wave_phase");
    public static readonly StringName Wind = new("u_wind");
    public static readonly StringName Zenith = new("u_zenith");
    // written every frame by the demo and its nodes (a string here would mint a StringName per call)
    public static readonly StringName Harbour = new("u_harbour");
    public static readonly StringName HarbourPass = new("u_harbour_pass");
    public static readonly StringName ShelterRect = new("u_shelter_rect");
    public static readonly StringName ShelterTex = new("u_shelter_map");
    public static readonly StringName Submerged = new("u_submerged");
    public static readonly StringName ShoreDist = new("u_shore_dist");
    public static readonly StringName Sunlit = new("u_sunlit");
    public static readonly StringName Kelvin = new("u_kelvin");
    public static readonly StringName Night = new("u_night");
    public static readonly StringName Hour = new("u_hour");
    public static readonly StringName SeaStorm = new("u_sea_storm");
    public static readonly StringName Wet = new("u_wet");
    public static readonly StringName Sheet = new("u_sheet");
    public static readonly StringName Aspect = new("u_aspect");
    public static readonly StringName Rain = new("u_rain");
    public static readonly StringName Mist = new("u_mist");
    public static readonly StringName Level = new("u_level");
    public static readonly StringName SeaY = new("u_sea_y");
    public static readonly StringName SeaPlane = new("u_sea_plane");
    public static readonly StringName Mask = new("u_mask");
    public static readonly StringName FadeOn = new("u_fade_on");
    public static readonly StringName Debug = new("u_debug");
    public static readonly StringName Chart = new("u_chart");
    public static readonly StringName Center = new("u_center");
    public static readonly StringName Span = new("u_span");
}

/// <summary>
/// PASSER UN TABLEAU À GODOT SANS LAISSER DE FINALISEUR DERRIÈRE SOI.
///
/// Convertir un tableau (Vector3[], float[], Godot.Collections.Array…) en Variant
/// crée un petit objet « libérateur » muni d'un finaliseur, qui rendra la mémoire
/// native quand le ramasse-miettes le trouvera. Un objet à finaliseur survit aux
/// petites collectes et s'entasse en génération 2 : à neuf navires, toile, flotte
/// et feux en créaient à chaque image, et la collecte complète qu'ils finissaient
/// par déclencher gelait une image de 38 ms en pleine course. Godot ayant RECOPIÉ
/// le tableau, on libère le Variant à l'instant : il meurt jeune, sans finaliseur.
/// </summary>
public static class VariantHandoff
{
    public static void SetNow(this ShaderMaterial m, StringName name, Variant v)
    {
        m.SetShaderParameter(name, v);
        v.Dispose();
    }

    public static void SetNow(this Godot.Collections.Array a, int index, Variant v)
    {
        a[index] = v;
        v.Dispose();
    }
}
