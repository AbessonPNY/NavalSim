/* LE RUBAN SOUS TAA — opaque et tramé (rope_ribbon.gdshaderinc).
 *
 * Le TAA mêle chaque image aux précédentes en suivant les vecteurs de mouvement,
 * que seuls les objets OPAQUES écrivent. Un ruban mélangé n'en a pas : quand la
 * caméra tourne ou que le navire roule, le TAA déplaçait ses pixels comme ceux du
 * ciel derrière lui et les étalait en traînées. Opaque, il a les siens.
 */
shader_type spatial;
render_mode depth_draw_opaque, cull_disabled, skip_vertex_transform, shadows_disabled;

#define RIBBON_HASHED
#include "res://shaders/rope_ribbon.gdshaderinc"
