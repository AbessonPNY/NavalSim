using Godot;
using System;
using System.Collections.Generic;

namespace NavalSim;

/// <summary>
/// UNE TOILE QUI BOUGE À CHAQUE IMAGE, SANS REBÂTIR SA SURFACE.
///
/// Voiles et pavillons étaient renvoyés entiers à chaque image : ClearSurfaces
/// puis AddSurfaceFromArrays, donc un tampon GPU neuf, les UV et les indices
/// recopiés, le format revalidé. Mesuré à seize frégates : 5,5 ms d'un _Process
/// de 17 — la moitié — pour des sommets qui seuls avaient changé.
///
/// Ici la surface est bâtie une fois, marquée dynamique, puis seules les
/// positions et les normales sont réécrites sur place
/// (RenderingServer.MeshSurfaceUpdateVertexRegion). Le format de ce tampon est
/// celui du moteur — positions en flottants, normales en octaèdre sur deux fois
/// seize bits — et il n'est pas SUPPOSÉ : pendant les premières images, la même
/// toile est encodée ici ET rebâtie par Godot, et les octets sont comparés. Au
/// moindre écart, cette toile garde le chemin d'avant (et la console le dit) : un
/// moteur qui change son format coûte des millisecondes, pas une voile en charpie.
/// </summary>
public sealed class LiveCloth
{
    /// <summary>Formats déjà jugés : un même format se juge une fois pour toutes les toiles.</summary>
    static readonly Dictionary<ulong, bool> Verdict = new();
    const int Proofs = 3;               // images comparées avant de se fier à l'encodage

    readonly Godot.Collections.Array _arrays;
    ArrayMesh? _mesh;
    Material? _applied;
    byte[]? _buf;
    int _stride, _normalAt, _proved;
    bool _live;                          // on écrit sur place
    bool _failed;                        // l'encodage a été pris en défaut : chemin d'avant
    Aabb _box;

    public LiveCloth(Godot.Collections.Array arrays) { _arrays = arrays; }

    /// <summary>
    /// Pousser la toile. <paramref name="v"/> et <paramref name="n"/> sont remplis
    /// ici depuis les flottants du noyau ; UV et indices sont déjà dans les
    /// tableaux de surface, posés une fois à la création.
    /// </summary>
    public void Upload(ArrayMesh mesh, Material mat, float[] pos, float[] nrm, Vector3[] v, Vector3[] n)
    {
        int count = v.Length;
        float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue;
        float x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
        for (int k = 0; k < count; k++)
        {
            float px = pos[k * 3], py = pos[k * 3 + 1], pz = pos[k * 3 + 2];
            v[k] = new Vector3(px, py, pz);
            n[k] = new Vector3(nrm[k * 3], nrm[k * 3 + 1], nrm[k * 3 + 2]);
            if (px < x0) x0 = px; if (px > x1) x1 = px;
            if (py < y0) y0 = py; if (py > y1) y1 = py;
            if (pz < z0) z0 = pz; if (pz > z1) z1 = pz;
        }

        // a material that reads tangents needs the engine's, which only a rebuild writes
        if (_live && !ReferenceEquals(mat, _applied) && UsesTangents(mat)) _live = false;
        if (_live && ReferenceEquals(mesh, _mesh) && _buf != null)
        {
            Encode(v, n, _buf);
            RenderingServer.MeshSurfaceUpdateVertexRegion(mesh.GetRid(), 0, 0, _buf);
            if (!ReferenceEquals(mat, _applied)) { mesh.SurfaceSetMaterial(0, mat); _applied = mat; }
            /* La boîte ne suit pas une mise à jour sur place : celle du dernier
               montage resterait, et une voile qui se creuse au-delà serait
               écartée par le tri de visibilité ou ses ombres. Élargie quand la
               toile en sort, avec de la marge pour ne pas la reposer à chaque image. */
            var now = new Aabb(new Vector3(x0, y0, z0), new Vector3(x1 - x0, y1 - y0, z1 - z0));
            if (!_box.Encloses(now))
            {
                _box = _box.Merge(now).Grow(0.5f);
                mesh.CustomAabb = _box;
            }
            return;
        }

        // le chemin d'avant : la surface rebâtie entière
        _arrays.SetNow((int)Mesh.ArrayType.Vertex, v);
        _arrays.SetNow((int)Mesh.ArrayType.Normal, n);
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _arrays, null, null, Mesh.ArrayFormat.FlagUseDynamicUpdate);
        mesh.SurfaceSetMaterial(0, mat);
        _applied = mat;
        if (_failed) return;
        if (!ReferenceEquals(mesh, _mesh)) { _mesh = mesh; _proved = 0; _buf = null; }
        Prove(mesh, mat, v, n, new Aabb(new Vector3(x0, y0, z0), new Vector3(x1 - x0, y1 - y0, z1 - z0)));
    }

    /* LA PREUVE : ce que Godot vient d'écrire, comparé à ce qu'on aurait écrit. */
    void Prove(ArrayMesh mesh, Material mat, Vector3[] v, Vector3[] n, Aabb box)
    {
        ulong fmt = (ulong)mesh.SurfaceGetFormat(0);
        bool known = Verdict.TryGetValue(fmt, out bool ok);
        if (known && !ok) { _failed = true; return; }
        if (UsesTangents(mat)) return;              // stays on the rebuild path, quietly
        var godot = RenderingServer.MeshGetSurface(mesh.GetRid(), 0)["vertex_data"].AsByteArray();
        if (!Layout(fmt, v.Length, godot.Length))
        {
            Fail(fmt, $"disposition inattendue : format {fmt:X}, pas {_stride}, normales à {_normalAt}, {godot.Length} octets pour {v.Length} sommets");
            return;
        }
        if (known) { _buf = godot; Go(mesh, box); return; }

        /* Encodé sur les octets de l'image D'AVANT : ce qu'on n'écrit pas (une
           tangente, si le moteur en déduit une de la normale) y est resté tel
           quel, et doit donc ne pas avoir changé chez lui non plus. Une image où
           la toile n'a pas bougé ne prouve rien et ne compte pas. */
        if (_buf == null || _buf.Length != godot.Length) { _buf = godot; return; }
        var mine = (byte[])_buf.Clone();
        Encode(v, n, mine);
        bool moved = false;
        for (int i = 0; i < godot.Length; i++)
        {
            if (!Written(i)) continue;
            if (godot[i] != _buf[i]) moved = true;
            if (godot[i] != mine[i])
            {
                Fail(fmt, $"octet {i} sur {godot.Length} : {mine[i]} au lieu de {godot[i]}");
                return;
            }
        }
        _buf = godot;
        if (!moved || ++_proved < Proofs) return;
        Verdict[fmt] = true;
        Go(mesh, box);
    }

    void Go(ArrayMesh mesh, Aabb box)
    {
        _box = box.Grow(0.5f);
        mesh.CustomAabb = _box;
        _live = true;
    }

    void Fail(ulong fmt, string why)
    {
        Verdict[fmt] = false;
        _failed = true;
        GD.PushWarning($"toile : encodage des sommets non conforme ({why}) — retour à la reconstruction à chaque image");
    }

    /* Two layouts are understood: normals interleaved with the positions (offset
       inside the vertex stride), or — what Godot 4.7 does for an uncompressed
       surface — positions first, then the normals as their own block (offset past
       every position), each at a stride read off the buffer's size. Anything
       else is refused, and the proof checks the guess byte for byte anyway. */
    bool Layout(ulong fmt, int count, int bytes)
    {
        var f = (RenderingServer.ArrayFormat)fmt;
        if ((f & RenderingServer.ArrayFormat.FlagCompressAttributes) != 0 || count == 0) return false;
        _stride = (int)RenderingServer.MeshSurfaceGetFormatVertexStride(f, count);
        _normalAt = (int)RenderingServer.MeshSurfaceGetFormatOffset(f, count, (int)RenderingServer.ArrayType.Normal);
        if (_stride < 12) return false;
        if (_normalAt >= 12 && _normalAt + 4 <= _stride)
        {
            _interleaved = true;
            return bytes == _stride * count;
        }
        _interleaved = false;
        if (_normalAt < _stride * count || (bytes - _normalAt) % count != 0) return false;
        _nStride = (bytes - _normalAt) / count;
        return _nStride >= 4;
    }
    bool _interleaved;
    int _nStride;

    /* WHAT IS WRITTEN HERE: the positions and the four bytes of each normal.
       After a normal Godot 4.7 stores a tangent it derives from it; that one is
       NOT rewritten in place and keeps the value of the last rebuild — harmless,
       because no shader a sail or a flag wears reads TANGENT, BINORMAL or a
       normal map (UsesTangents keeps any that would on the rebuild path). */
    bool Written(int i)
    {
        if (_interleaved) { int r = i % _stride; return r < 12 || (r >= _normalAt && r < _normalAt + 4); }
        if (i < _normalAt) return i % _stride < 12;
        return (i - _normalAt) % _nStride < 4;
    }

    static readonly Dictionary<Material, bool> TangentReaders = new();
    static bool UsesTangents(Material? m)
    {
        for (; m != null; m = m.NextPass)
        {
            if (!TangentReaders.TryGetValue(m, out bool uses))
            {
                uses = m switch
                {
                    ShaderMaterial sm => sm.Shader is Shader sh
                        && (sh.Code.Contains("TANGENT") || sh.Code.Contains("BINORMAL") || sh.Code.Contains("NORMAL_MAP")),
                    BaseMaterial3D bm => bm.NormalEnabled || bm.AnisotropyEnabled,
                    _ => true,
                };
                TangentReaders[m] = uses;
            }
            if (uses) return true;
        }
        return false;
    }

    /* Positions en trois flottants, normales en octaèdre sur deux entiers de seize
       bits — Vector3::octahedron_encode puis la mise à l'échelle de
       RenderingServer::_surface_set_data, en flottants simples comme le moteur. */
    void Encode(Vector3[] v, Vector3[] n, byte[] buf)
    {
        var span = buf.AsSpan();
        for (int k = 0; k < v.Length; k++)
        {
            int o = k * _stride;
            int on = _interleaved ? o + _normalAt : _normalAt + k * _nStride;
            BitConverter.TryWriteBytes(span.Slice(o, 4), v[k].X);
            BitConverter.TryWriteBytes(span.Slice(o + 4, 4), v[k].Y);
            BitConverter.TryWriteBytes(span.Slice(o + 8, 4), v[k].Z);
            var q = n[k];
            float l1 = MathF.Abs(q.X) + MathF.Abs(q.Y) + MathF.Abs(q.Z);
            float nx = q.X / l1, ny = q.Y / l1, nz = q.Z / l1;
            float ox, oy;
            if (nz >= 0f) { ox = nx; oy = ny; }
            else
            {
                ox = (1f - MathF.Abs(ny)) * (nx >= 0f ? 1f : -1f);
                oy = (1f - MathF.Abs(nx)) * (ny >= 0f ? 1f : -1f);
            }
            ox = ox * 0.5f + 0.5f;
            oy = oy * 0.5f + 0.5f;
            ushort ex = (ushort)Math.Clamp(ox * 65535f, 0f, 65535f);
            ushort ey = (ushort)Math.Clamp(oy * 65535f, 0f, 65535f);
            BitConverter.TryWriteBytes(span.Slice(on, 2), ex);
            BitConverter.TryWriteBytes(span.Slice(on + 2, 2), ey);
        }
    }
}
