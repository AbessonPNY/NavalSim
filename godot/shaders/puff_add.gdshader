/* Les bouffées qui s'AJOUTENT : flamme de bouche, boule de feu, étincelles. */
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

#include "res://shaders/puff.gdshaderinc"
