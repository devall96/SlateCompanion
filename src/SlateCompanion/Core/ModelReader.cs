using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// A reader for the parts of a Breath of the Wild (Switch) BFRES model that an offline renderer needs: models, shapes
/// (LOD 0 triangles), vertex positions / normals / UVs, the albedo texture name of each material, and the skeleton's
/// bind pose for placing rigid and single-bone meshes. Read only; works on the player's own game files.
///
/// Layout notes (BFRES version 0.5.0.3, as BotW Switch ships it; little endian, every "ptr" is a u64 file offset, every
/// string is a u16 length followed by the characters). Field order follows the public structure of the Switch BFRES
/// format as described by Switch Toolbox / BfresLibrary (ResFile, Model, Shape, Mesh, VertexBuffer, Material,
/// Skeleton, Bone); every offset below was then checked by hand against hex dumps of Weapon_Sword_001.sbfres (counts,
/// sizes and strides add up: e.g. FVTX buffer sizes = vertex count x stride, LOD 1 index data starts right after LOD 0's).
///
///   File header: 0x28 model array ptr, 0x90 buffer-info ptr, 0xBC u16 model count.
///   Buffer info: 0x08 u64 offset of the GPU data block; vertex and index offsets below are relative to it.
///   Dictionary (_DIC, magic may be 0): u32 magic, u32 count, then count+1 entries of 0x10 (u32 ref, u16 left,
///     u16 right, ptr key); entry 0 is the root, entry i+1 names item i.
///   FMDL (0x78): 0x10 name, 0x20 skeleton, 0x28 vertex buffer array, 0x30 shape array, 0x40 material array,
///     0x68 u16 vertex buffer count, 0x6A u16 shape count, 0x6C u16 material count.
///   FSHP (0x70): 0x10 name, 0x18 FVTX ptr, 0x20 mesh array (one per LOD), 0x5E u16 material index, 0x60 u16 bone
///     index, 0x66 u8 vertex skin count, 0x67 u8 mesh count.
///   Mesh (0x38): 0x20 u32 index data offset, 0x24 u32 primitive (3 = triangles), 0x28 u32 index format
///     (0 u8, 1 u16, 2 u32), 0x2C u32 index count, 0x30 u32 first vertex (added to each index).
///   FVTX (0x60): 0x10 attribute array, 0x38 buffer size array (0x10 each, u32 size), 0x40 stride array (0x10 each,
///     u32 stride), 0x50 u32 data offset, 0x54 u8 attribute count, 0x55 u8 buffer count, 0x58 u32 vertex count.
///     The buffers follow each other from the data offset (8-byte aligned).
///   Attribute (0x10): 0x00 name (_p0, _n0, _u0, _i0, _w0, ...), 0x08 u16 format (high byte = component layout,
///     low byte = 1 unorm / 2 snorm / 3 uint / 4 sint / 5 float), 0x0C u16 offset in vertex, 0x0E u16 buffer index.
///   FMAT (0xB8): 0x10 name, 0x18 render info array (0x18 each: name, data ptr, u16 count, u8 type 0 int / 1 float /
///     2 string), 0x38 texture name array (ptrs), 0x50 sampler dictionary (_a0, _n0, ... in texture order),
///     0xA6 u16 render info count, 0xA8 u8 texture count.
///   FSKL: 0x18 bone array, 0x20 matrix-to-bone table (u16), 0x38 u32 flags, 0x3C u16 bone count,
///     0x3E u16 smooth matrix count, 0x40 u16 rigid matrix count.
///   Bone (0x50): 0x00 name, 0x1A s16 parent, 0x24 u32 flags (bits 12-14: 0 quaternion, 1 Euler XYZ), 0x28 scale,
///     0x34 rotation (xyzw), 0x44 translation.
///
/// Skinning in the bind pose: vertex skin count 0 = the whole shape is in the space of the shape's bone; 1 = each
/// vertex is in the space of the bone _i0.x names through the matrix-to-bone table; 2+ = already in model space.
/// </summary>
public static class ModelReader
{
    public sealed class Mesh
    {
        public required string Model, Shape, Material;
        public required Vector3[] Positions;
        public Vector3[]? Normals;
        public Vector2[]? Uvs;
        public required int[] Indices;
        public string? Albedo;
        public bool AlphaTest;
        public int SkinCount;
        public Dictionary<string, string> RenderInfo = new();
        public int Triangles => Indices.Length / 3;
        /// <summary>The model's skeleton (bind pose) and, per vertex, <see cref="Influences"/> skeleton bone indices and
        /// weights (Bones[v * Influences + k]). Positions and normals are already in model space (bind pose).</summary>
        public Skeleton? Skeleton;
        public int Influences;
        public int[]? Bones;
        public float[]? Weights;
    }

    /// <summary>A model's bones in the bind pose: local transforms and model-space (world) matrices, parents first.</summary>
    public sealed class Skeleton
    {
        public required string[] Names;
        public required int[] Parents;
        public required Matrix4x4[] Local, World;
        public int Find(string name) => Array.IndexOf(Names, name);
    }

    // ------------------------------------------------------------------ finding files

    /// <summary>
    /// A file from the romfs by its romfs path (e.g. "Model/Link.sbfres"): loose, or inside one of the always-loaded
    /// packs (Link's body lives in Pack/TitleBG.pack). Null when it is nowhere.
    /// </summary>
    public static byte[]? LoadRomfsFile(string romfs, string rel)
    {
        var loose = Path.Combine(romfs, rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose)) return File.ReadAllBytes(loose);
        foreach (var pack in new[] { "TitleBG.pack", "Bootup.pack" })
        {
            var p = Path.Combine(romfs, "Pack", pack);
            if (!File.Exists(p)) continue;
            var index = PackIndex(p);
            if (!index.TryGetValue(rel, out var e)) continue;
            using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Position = e.Offset;
            var buf = new byte[e.Length];
            fs.ReadExactly(buf);
            return buf;
        }
        return null;
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Stamp, Dictionary<string, (long Offset, int Length)> Files)> packIndexes = new();

    /// <summary>
    /// The file table of an uncompressed SARC pack (name -> offset, length), read from the head of the file only, so a
    /// lookup in the 147 MB TitleBG.pack reads its table plus the one file. Cached per pack and file time.
    /// </summary>
    static Dictionary<string, (long Offset, int Length)> PackIndex(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (packIndexes.TryGetValue(path, out var c) && c.Stamp == stamp) return c.Files;
        var files = new Dictionary<string, (long, int)>(StringComparer.Ordinal);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var head = new byte[0x20];
            fs.ReadExactly(head);
            if (head[0] == 'S' && head[1] == 'A' && head[2] == 'R' && head[3] == 'C' && head[6] == 0xFF && head[7] == 0xFE)
            {
                long dataOff = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(0x0C));
                var d = new byte[Math.Min(dataOff, fs.Length)];
                fs.Position = 0;
                fs.ReadExactly(d);
                const int sfat = 0x14;
                int count = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(sfat + 6));
                int names = sfat + 0x0C + count * 0x10 + 8;
                for (int i = 0; i < count; i++)
                {
                    int e = sfat + 0x0C + i * 0x10;
                    uint attr = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(e + 4));
                    if ((attr & 0x01000000) == 0) continue;
                    int at = names + (int)(attr & 0xFFFF) * 4, z = at;
                    while (z < d.Length && d[z] != 0) z++;
                    uint start = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(e + 8)), end = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(e + 12));
                    files[Encoding.UTF8.GetString(d, at, z - at)] = (dataOff + start, (int)(end - start));
                }
            }
        }
        packIndexes[path] = (stamp, files);
        return files;
    }

    /// <summary>One file out of a SARC in memory (Yaz0 or not), without copying the others (GameAssets.Sarc copies all).</summary>
    public static byte[]? SarcFile(byte[] raw, string name)
    {
        var d = GameAssets.Yaz0(raw);
        if (d.Length < 0x20 || d[0] != 'S' || d[1] != 'A' || d[2] != 'R' || d[3] != 'C' || d[6] != 0xFF || d[7] != 0xFE) return null;
        uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at));
        int dataOff = (int)U32(0x0C);
        const int sfat = 0x14;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(sfat + 6));
        int names = sfat + 0x0C + count * 0x10 + 8;
        var want = Encoding.UTF8.GetBytes(name);
        for (int i = 0; i < count; i++)
        {
            int e = sfat + 0x0C + i * 0x10;
            uint attr = U32(e + 4);
            if ((attr & 0x01000000) == 0) continue;
            int at = names + (int)(attr & 0xFFFF) * 4;
            if (!d.AsSpan(at, want.Length).SequenceEqual(want) || d[at + want.Length] != 0) continue;
            int start = (int)U32(e + 8), end = (int)U32(e + 12);
            return d.AsSpan(dataOff + start, end - start).ToArray();
        }
        return null;
    }

    // ------------------------------------------------------------------ BFRES

    /// <summary>Every LOD-0 triangle mesh of every model in a BFRES (Yaz0 or not), placed in the bind pose.</summary>
    public static List<Mesh> Read(byte[] raw, List<string>? notes = null)
    {
        var d = GameAssets.Yaz0(raw);
        if (d.Length < 0xD0 || Encoding.ASCII.GetString(d, 0, 4) != "FRES" || d[0x0C] != 0xFF || d[0x0D] != 0xFE)
            throw new InvalidDataException("not a Switch BFRES");
        var r = new R(d);
        uint ver = r.U32(0x08);
        if ((ver >> 16) != 5 && (ver >> 16) != 8) notes?.Add($"BFRES version 0.{ver >> 16}.{(ver >> 8) & 0xFF}.{ver & 0xFF}: untested layout");
        long dataStart = r.P(r.P(0x90) + 8);
        long models = r.P(0x28);
        int modelCount = r.U16(0xBC);
        var meshes = new List<Mesh>();
        for (int mi = 0; mi < modelCount; mi++)
        {
            long m = models + mi * 0x78;
            string modelName = r.Str(r.P(m + 0x10));
            var skel = ReadSkeleton(r, r.P(m + 0x20), out var matrixToBone);
            var bones = skel.World;
            long shapes = r.P(m + 0x30), mats = r.P(m + 0x40);
            int shapeCount = r.U16(m + 0x6A), matCount = r.U16(m + 0x6C);
            var materials = new List<(string Name, string? Albedo, Dictionary<string, string> Info)>();
            for (int i = 0; i < matCount; i++) materials.Add(ReadMaterial(r, mats + i * 0xB8));

            for (int si = 0; si < shapeCount; si++)
            {
                long s = shapes + si * 0x70;
                string shapeName = r.Str(r.P(s + 0x10));
                int matIdx = r.U16(s + 0x5E), boneIdx = r.U16(s + 0x60), vsc = d[s + 0x66], meshCount = d[s + 0x67];
                if (meshCount == 0) continue;
                long lod0 = r.P(s + 0x20);
                uint prim = r.U32(lod0 + 0x24), ifmt = r.U32(lod0 + 0x28), icount = r.U32(lod0 + 0x2C), first = r.U32(lod0 + 0x30);
                if (prim != 3) { notes?.Add($"{modelName}/{shapeName}: primitive type {prim} skipped (only triangle lists)"); continue; }
                long ib = dataStart + r.U32(lod0 + 0x20);
                if (ib < 0 || icount > (d.Length - ib) / (ifmt == 0 ? 1 : ifmt == 1 ? 2 : 4)) throw new InvalidDataException($"{modelName}/{shapeName}: index buffer runs past the end of the file (damaged)");
                var idx = new int[icount];
                for (int k = 0; k < icount; k++)
                    idx[k] = (int)first + ifmt switch
                    {
                        0 => d[ib + k],
                        1 => r.U16(ib + k * 2),
                        _ => (int)r.U32(ib + k * 4),
                    };

                var attrs = ReadVertices(r, dataStart, r.P(s + 0x18), out int vcount);
                if (!attrs.TryGetValue("_p0", out var p0)) { notes?.Add($"{modelName}/{shapeName}: no positions"); continue; }
                var pos = new Vector3[vcount];
                var nrm = attrs.TryGetValue("_n0", out var n0) ? new Vector3[vcount] : null;
                var uv = attrs.TryGetValue("_u0", out var u0) ? new Vector2[vcount] : null;
                attrs.TryGetValue("_i0", out var i0);
                attrs.TryGetValue("_w0", out var w0);
                attrs.TryGetValue("_i1", out var i1);
                attrs.TryGetValue("_w1", out var w1);
                int inf = Math.Clamp(vsc, 1, i1 != null ? 8 : 4);
                var vb = new int[vcount * inf];
                var vw = new float[vcount * inf];
                for (int v = 0; v < vcount; v++)
                {
                    // influences as skeleton bone indices (through the matrix-to-bone table)
                    if (vsc == 0 || i0 == null) { vb[v * inf] = boneIdx < bones.Length ? boneIdx : 0; vw[v * inf] = 1; }
                    else
                    {
                        float sum = 0;
                        for (int k = 0; k < inf; k++)
                        {
                            var iv = k < 4 ? i0 : i1; var wv = k < 4 ? w0 : w1;
                            if (iv == null) break;
                            int mx = (int)Comp(iv[v], k & 3);
                            float w = vsc == 1 ? (k == 0 ? 1 : 0) : wv != null ? Comp(wv[v], k & 3) : (k == 0 ? 1 : 0);
                            int bi = mx < matrixToBone.Length ? matrixToBone[mx] : 0;
                            vb[v * inf + k] = bi < bones.Length ? bi : 0;
                            vw[v * inf + k] = w;
                            sum += w;
                        }
                        if (sum > 0) for (int k = 0; k < inf; k++) vw[v * inf + k] /= sum;
                        else vw[v * inf] = 1;
                    }
                    var p = new Vector3(p0[v].X, p0[v].Y, p0[v].Z);
                    Vector3 n = nrm != null ? new Vector3(n0![v].X, n0[v].Y, n0[v].Z) : Vector3.UnitZ;
                    Matrix4x4? bind = vsc switch
                    {
                        0 => boneIdx < bones.Length ? bones[boneIdx] : null,
                        1 when i0 != null && (int)i0[v].X < matrixToBone.Length && matrixToBone[(int)i0[v].X] < bones.Length => bones[matrixToBone[(int)i0[v].X]],
                        _ => null,
                    };
                    if (bind is { } b)
                    {
                        p = Vector3.Transform(p, b);
                        n = Vector3.TransformNormal(n, b);
                    }
                    pos[v] = p;
                    if (nrm != null) nrm[v] = n;
                    if (uv != null) uv[v] = new Vector2(u0![v].X, u0[v].Y);
                }
                var mat = matIdx < materials.Count ? materials[matIdx] : ("?", null, new Dictionary<string, string>());
                meshes.Add(new Mesh
                {
                    Model = modelName, Shape = shapeName, Material = mat.Name, Positions = pos, Normals = nrm, Uvs = uv,
                    Indices = idx, Albedo = mat.Albedo, SkinCount = vsc, RenderInfo = mat.Info,
                    Skeleton = skel, Influences = inf, Bones = vb, Weights = vw,
                    AlphaTest = mat.Info.TryGetValue("gsys_alpha_test_enable", out var at) && at == "true",
                });
            }
        }
        return meshes;
    }

    /// <summary>The bind pose: each bone's model-space matrix (System.Numerics row-vector convention).</summary>
    static Skeleton ReadSkeleton(R r, long sk, out int[] matrixToBone)
    {
        matrixToBone = [];
        if (sk <= 0) return new Skeleton { Names = [], Parents = [], Local = [], World = [] };
        long arr = r.P(sk + 0x18);
        int count = r.U16(sk + 0x3C), smooth = r.U16(sk + 0x3E), rigid = r.U16(sk + 0x40);
        long mtb = r.P(sk + 0x20);
        matrixToBone = new int[smooth + rigid];
        for (int i = 0; i < matrixToBone.Length; i++) matrixToBone[i] = r.U16(mtb + i * 2);
        var world = new Matrix4x4[count];
        var locals = new Matrix4x4[count];
        var names = new string[count];
        var parents = new int[count];
        for (int i = 0; i < count; i++)
        {
            long b = arr + i * 0x50;
            names[i] = r.Str(r.P(b));
            int parent = r.S16(b + 0x1A);
            parents[i] = parent < i ? parent : -1;
            uint flags = r.U32(b + 0x24);
            var scale = new Vector3(r.F(b + 0x28), r.F(b + 0x2C), r.F(b + 0x30));
            var rot = new Vector4(r.F(b + 0x34), r.F(b + 0x38), r.F(b + 0x3C), r.F(b + 0x40));
            var tr = new Vector3(r.F(b + 0x44), r.F(b + 0x48), r.F(b + 0x4C));
            Matrix4x4 rm = ((flags >> 12) & 7) == 1
                // Euler XYZ: X applied first, then Y, then Z (row vectors: Rx * Ry * Rz)
                ? Matrix4x4.CreateRotationX(rot.X) * Matrix4x4.CreateRotationY(rot.Y) * Matrix4x4.CreateRotationZ(rot.Z)
                : Matrix4x4.CreateFromQuaternion(new Quaternion(rot.X, rot.Y, rot.Z, rot.W));
            var local = Matrix4x4.CreateScale(scale) * rm * Matrix4x4.CreateTranslation(tr);
            // bones are stored parents first
            locals[i] = local;
            world[i] = parent >= 0 && parent < i ? local * world[parent] : local;
        }
        return new Skeleton { Names = names, Parents = parents, Local = locals, World = world };
    }

    static float Comp(Vector4 v, int k) => k switch { 0 => v.X, 1 => v.Y, 2 => v.Z, _ => v.W };

    static (string Name, string? Albedo, Dictionary<string, string> Info) ReadMaterial(R r, long m)
    {
        string name = r.Str(r.P(m + 0x10));
        var info = new Dictionary<string, string>();
        long ri = r.P(m + 0x18);
        int riCount = r.U16(m + 0xA6);
        for (int i = 0; i < riCount; i++)
        {
            long e = ri + i * 0x18;
            string key = r.Str(r.P(e));
            long data = r.P(e + 8);
            int n = r.U16(e + 0x10), type = r.D[e + 0x12];
            if (n == 0 || data <= 0) continue;
            info[key] = type switch
            {
                2 => r.Str(r.P(data)),
                1 => r.F(data).ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => r.S32(data).ToString(),
            };
        }
        int texCount = r.D[m + 0xA8];
        long texNames = r.P(m + 0x38);
        var textures = new List<string>();
        for (int i = 0; i < texCount; i++) textures.Add(r.Str(r.P(texNames + i * 8)));
        var samplers = r.DictKeys(r.P(m + 0x50));
        string? albedo = null;
        int a0 = samplers.IndexOf("_a0");
        if (a0 >= 0 && a0 < textures.Count) albedo = textures[a0];
        albedo ??= textures.FirstOrDefault(t => t.EndsWith("_Alb", StringComparison.Ordinal));
        return (name, albedo, info);
    }

    /// <summary>Every attribute of an FVTX as up to four floats per vertex (normalised for unorm/snorm formats).</summary>
    static Dictionary<string, Vector4[]> ReadVertices(R r, long dataStart, long v, out int count)
    {
        long attrs = r.P(v + 0x10), sizes = r.P(v + 0x38), strides = r.P(v + 0x40);
        long off = dataStart + r.U32(v + 0x50);
        int numAttr = r.D[v + 0x54], numBuf = r.D[v + 0x55];
        uint vc = r.U32(v + 0x58);
        if (vc > r.D.Length) throw new InvalidDataException("more vertices than the file has bytes (damaged)");
        count = (int)vc;
        var bufStart = new long[numBuf];
        var bufStride = new int[numBuf];
        for (int i = 0; i < numBuf; i++)
        {
            bufStart[i] = off;
            bufStride[i] = (int)r.U32(strides + i * 0x10);
            off += r.U32(sizes + i * 0x10);
            off = (off + 7) & ~7L;
        }
        var result = new Dictionary<string, Vector4[]>();
        for (int a = 0; a < numAttr; a++)
        {
            long e = attrs + a * 0x10;
            string name = r.Str(r.P(e));
            int fmt = r.U16(e + 8), ofs = r.U16(e + 0x0C), buf = r.U16(e + 0x0E);
            if (buf >= numBuf) continue;
            var vals = new Vector4[count];
            try
            {
                for (int i = 0; i < count; i++) vals[i] = Attr(r.D, bufStart[buf] + (long)i * bufStride[buf] + ofs, fmt);
            }
            catch (NotSupportedException) { continue; }
            result[name] = vals;
        }
        return result;
    }

    /// <summary>One vertex attribute. Formats: high byte = layout, low byte = 1 unorm, 2 snorm, 3 uint, 4 sint, 5 float.</summary>
    static Vector4 Attr(byte[] d, long at, int fmt)
    {
        int layout = fmt >> 8, kind = fmt & 0xFF;
        var s = d.AsSpan((int)at);
        if (layout == 0x0E) // 10_10_10_2
        {
            uint x = BinaryPrimitives.ReadUInt32LittleEndian(s);
            if (kind == 2)
            {
                static float Sn(uint b) => Math.Max(-1f, ((int)(b << 22) >> 22) / 511f);
                return new Vector4(Sn(x), Sn(x >> 10), Sn(x >> 20), ((int)(x << 0) >> 30));
            }
            return new Vector4((x & 1023) / 1023f, ((x >> 10) & 1023) / 1023f, ((x >> 20) & 1023) / 1023f, (x >> 30) / 3f);
        }
        (int n, int bits) = layout switch
        {
            0x02 => (1, 8), 0x09 => (2, 8), 0x0B => (4, 8),
            0x0A => (1, 16), 0x12 => (2, 16), 0x15 => (4, 16),
            0x14 => (1, 32), 0x17 => (2, 32), 0x18 => (3, 32), 0x19 => (4, 32),
            _ => throw new NotSupportedException($"vertex format 0x{fmt:X4}"),
        };
        Span<float> c = stackalloc float[4];
        c[3] = 1;
        for (int i = 0; i < n; i++)
        {
            c[i] = (bits, kind) switch
            {
                (8, 1) => s[i] / 255f,
                (8, 2) => Math.Max(-1f, (sbyte)s[i] / 127f),
                (8, 4) => (sbyte)s[i],
                (8, _) => s[i],
                (16, 1) => BinaryPrimitives.ReadUInt16LittleEndian(s[(i * 2)..]) / 65535f,
                (16, 2) => Math.Max(-1f, BinaryPrimitives.ReadInt16LittleEndian(s[(i * 2)..]) / 32767f),
                (16, 5) => (float)BinaryPrimitives.ReadHalfLittleEndian(s[(i * 2)..]),
                (16, 4) => BinaryPrimitives.ReadInt16LittleEndian(s[(i * 2)..]),
                (16, _) => BinaryPrimitives.ReadUInt16LittleEndian(s[(i * 2)..]),
                (32, 5) => BinaryPrimitives.ReadSingleLittleEndian(s[(i * 4)..]),
                (32, 4) => BinaryPrimitives.ReadInt32LittleEndian(s[(i * 4)..]),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(s[(i * 4)..]),
            };
        }
        return new Vector4(c[0], c[1], c[2], c[3]);
    }

    sealed class R(byte[] d)
    {
        public readonly byte[] D = d;
        public long P(long at) => at <= 0 || at + 8 > D.Length ? 0 : (long)BinaryPrimitives.ReadUInt64LittleEndian(D.AsSpan((int)at));
        // (an offset past the end throws instead of wrapping around to a wrong place in the file)
        int At(long at, int len) => at >= 0 && at + len <= D.Length ? (int)at : throw new InvalidDataException($"model offset 0x{at:X} is outside the file (damaged)");
        public uint U32(long at) => BinaryPrimitives.ReadUInt32LittleEndian(D.AsSpan(At(at, 4)));
        public int S32(long at) => BinaryPrimitives.ReadInt32LittleEndian(D.AsSpan(At(at, 4)));
        public int U16(long at) => BinaryPrimitives.ReadUInt16LittleEndian(D.AsSpan(At(at, 2)));
        public int S16(long at) => BinaryPrimitives.ReadInt16LittleEndian(D.AsSpan(At(at, 2)));
        public float F(long at) => BinaryPrimitives.ReadSingleLittleEndian(D.AsSpan(At(at, 4)));
        public string Str(long at)
        {
            if (at <= 0 || at + 2 > D.Length) return "";
            int n = U16(at);
            return at + 2 + n <= D.Length ? Encoding.UTF8.GetString(D, (int)at + 2, n) : "";
        }
        public List<string> DictKeys(long at)
        {
            var keys = new List<string>();
            if (at <= 0) return keys;
            long n = U32(at + 4);
            if (at + 8 + (n + 1) * 0x10 > D.Length) throw new InvalidDataException("model dictionary is longer than the file (damaged)");
            for (int i = 1; i <= n; i++) keys.Add(Str(P(at + 8 + i * 0x10 + 8)));
            return keys;
        }
    }
}
