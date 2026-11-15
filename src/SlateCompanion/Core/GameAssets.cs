using System.Buffers.Binary;
using System.Text;

namespace SlateCompanion;

/// <summary>
/// Readers for the player's own Breath of the Wild game files (Switch romfs): Yaz0, SARC, BNTX textures, the Tegra
/// block-linear layout, and BC1/BC3/BC4/BC5/RGBA8/R8/RG8 pixel formats. Read only; nothing here writes game files.
/// </summary>
public static class GameAssets
{
    // ------------------------------------------------------------------ Yaz0

    public static byte[] Yaz0(byte[] d)
    {
        if (d.Length < 16 || d[0] != 'Y' || d[1] != 'a' || d[2] != 'z' || d[3] != '0') return d;
        int size = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4));
        var o = new byte[size];
        int s = 16, p = 0;
        while (p < size)
        {
            int code = d[s++];
            for (int i = 0; i < 8 && p < size; i++)
            {
                if ((code & (0x80 >> i)) != 0) { o[p++] = d[s++]; continue; }
                int b1 = d[s++], b2 = d[s++];
                int dist = ((b1 & 0xF) << 8 | b2) + 1;
                int n = b1 >> 4;
                n = n == 0 ? d[s++] + 0x12 : n + 2;
                int from = p - dist;
                if (n > size - p) n = size - p;
                if (dist >= n) { Buffer.BlockCopy(o, from, o, p, n); p += n; }
                else for (int k = 0; k < n; k++) o[p++] = o[from + k];
            }
        }
        return o;
    }

    // ------------------------------------------------------------------ SARC

    /// <summary>The files of a SARC (Yaz0 or not), by name.</summary>
    public static Dictionary<string, byte[]> Sarc(byte[] raw)
    {
        var d = Yaz0(raw);
        if (d.Length < 0x20 || Encoding.ASCII.GetString(d, 0, 4) != "SARC") throw new InvalidDataException("not a SARC archive");
        bool le = d[6] == 0xFF && d[7] == 0xFE;
        uint U32(int at) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        ushort U16(int at) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
        int dataOff = (int)U32(0x0C);
        const int sfat = 0x14;
        int count = U16(sfat + 6);
        int names = sfat + 0x0C + count * 0x10 + 8;
        var files = new Dictionary<string, byte[]>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int e = sfat + 0x0C + i * 0x10;
            uint hash = U32(e), attr = U32(e + 4), start = U32(e + 8), end = U32(e + 12);
            string name;
            if ((attr & 0x01000000) != 0)
            {
                int at = names + (int)(attr & 0xFFFF) * 4, z = at;
                while (d[z] != 0) z++;
                name = Encoding.UTF8.GetString(d, at, z - at);
            }
            else name = $"0x{hash:X8}";
            files[name] = d.AsSpan(dataOff + (int)start, (int)(end - start)).ToArray();
        }
        return files;
    }

    // ------------------------------------------------------------------ BNTX

    public sealed record Texture(string Name, uint Format, int Width, int Height, int BlockHeightLog2, int TileMode, byte[] Data)
    {
        public int Kind => (int)(Format >> 8);
    }

    /// <summary>Every texture in the first BNTX inside <paramref name="d"/> (a BNTX, or a BFRES that embeds one).</summary>
    public static List<Texture> Textures(byte[] d)
    {
        int b = IndexOf(d, "BNTX"u8, 0);
        if (b < 0) throw new InvalidDataException("no BNTX texture container");
        var list = new List<Texture>();
        int i = b;
        while ((i = IndexOf(d, "BRTI"u8, i + 4)) >= 0)
        {
            var s = d.AsSpan();
            int tile = BinaryPrimitives.ReadUInt16LittleEndian(s[(i + 0x12)..]);
            uint fmt = BinaryPrimitives.ReadUInt32LittleEndian(s[(i + 0x1C)..]);
            int w = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[(i + 0x24)..]);
            int h = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[(i + 0x28)..]);
            int layout = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[(i + 0x34)..]);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(s[(i + 0x50)..]);
            long nameOff = (long)BinaryPrimitives.ReadUInt64LittleEndian(s[(i + 0x60)..]);
            long ptrs = (long)BinaryPrimitives.ReadUInt64LittleEndian(s[(i + 0x70)..]);
            if (w <= 0 || h <= 0 || w > 16384 || h > 16384 || nameOff <= 0 || b + nameOff >= d.Length || ptrs <= 0 || b + ptrs + 8 > d.Length) continue;
            int nl = BinaryPrimitives.ReadUInt16LittleEndian(s[(int)(b + nameOff)..]);
            string name = Encoding.ASCII.GetString(d, (int)(b + nameOff + 2), nl);
            long mip0 = (long)BinaryPrimitives.ReadUInt64LittleEndian(s[(int)(b + ptrs)..]);
            if (b + mip0 + size > d.Length) continue;
            list.Add(new Texture(name, fmt, w, h, layout & 7, tile, d.AsSpan((int)(b + mip0), size).ToArray()));
        }
        return list;
    }

    static int IndexOf(byte[] d, ReadOnlySpan<byte> what, int from) =>
        from >= d.Length ? -1 : d.AsSpan(from).IndexOf(what) is int r && r >= 0 ? r + from : -1;

    // ------------------------------------------------------------------ Tegra block-linear

    /// <summary>Byte offset of element (ex, ey) in a block-linear surface (elements are pixels or 4x4 blocks).</summary>
    public static int SwizzledOffset(int ex, int ey, int widthElements, int bpe, int blockHeightLog2)
    {
        int bh = 1 << blockHeightLog2;
        int gobsX = (widthElements * bpe + 63) / 64;
        int x = ex * bpe, y = ey;
        int gob = (y / (8 * bh)) * (512 * bh * gobsX) + (x / 64) * (512 * bh) + ((y % (8 * bh)) / 8) * 512;
        int xx = x % 64, yy = y % 8;
        return gob + (xx / 32) * 256 + (yy / 2) * 64 + ((xx % 32) / 16) * 32 + (yy % 2) * 16 + (xx % 16);
    }

    /// <summary>A texture's elements in row-major order (blocks for BC formats, pixels otherwise).</summary>
    public static byte[] Linear(Texture t, int bpe, bool blocks)
    {
        int ew = blocks ? (t.Width + 3) / 4 : t.Width, eh = blocks ? (t.Height + 3) / 4 : t.Height;
        var o = new byte[ew * eh * bpe];
        if (t.TileMode != 0) { Buffer.BlockCopy(t.Data, 0, o, 0, Math.Min(o.Length, t.Data.Length)); return o; }
        for (int y = 0; y < eh; y++)
            for (int x = 0; x < ew; x++)
            {
                int src = SwizzledOffset(x, y, ew, bpe, t.BlockHeightLog2);
                if (src + bpe <= t.Data.Length) Buffer.BlockCopy(t.Data, src, o, (y * ew + x) * bpe, bpe);
            }
        return o;
    }

    // ------------------------------------------------------------------ pixel formats

    /// <summary>
    /// RGBA8 pixels of a UI texture. Single-channel masks (BC4, R8) become white with that channel as alpha; two-channel
    /// textures (BC5, RG8) become grey (R) with G as alpha, as the game's layout shaders read them.
    /// </summary>
    public static byte[] DecodeRgba(Texture t)
    {
        int w = t.Width, h = t.Height;
        var px = new byte[w * h * 4];
        switch (t.Kind)
        {
            case 0x1A: Blocks(t, 8, px, (b, o, x, y, ww) => Bc1Block(b, o, x, y, px, w, h, false)); break;
            case 0x1C:
                Blocks(t, 16, px, (b, o, x, y, ww) =>
                {
                    Bc1Block(b, o + 8, x, y, px, w, h, true);
                    Bc4Block(b, o, x, y, ww, px, 3, w, h);
                });
                break;
            case 0x1D:
                Blocks(t, 8, px, (b, o, x, y, ww) => { Bc4Block(b, o, x, y, ww, px, 3, w, h); });
                for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = 255; }
                break;
            case 0x1E:
                Blocks(t, 16, px, (b, o, x, y, ww) => { Bc4Block(b, o, x, y, ww, px, 0, w, h); Bc4Block(b, o + 8, x, y, ww, px, 3, w, h); });
                for (int i = 0; i < px.Length; i += 4) { px[i + 1] = px[i + 2] = px[i]; }
                break;
            case 0x0B:
                {
                    var lin = Linear(t, 4, false);
                    Buffer.BlockCopy(lin, 0, px, 0, Math.Min(px.Length, lin.Length));
                    break;
                }
            case 0x02:
                {
                    var lin = Linear(t, 1, false);
                    for (int i = 0; i < w * h; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = 255; px[i * 4 + 3] = lin[i]; }
                    break;
                }
            case 0x09:
                {
                    var lin = Linear(t, 2, false);
                    for (int i = 0; i < w * h; i++) { px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = lin[i * 2]; px[i * 4 + 3] = lin[i * 2 + 1]; }
                    break;
                }
            default: throw new NotSupportedException($"texture format 0x{t.Format:X}");
        }
        return px;
    }

    delegate void BlockFn(byte[] blocks, int offset, int bx, int by, int widthBlocks);

    static void Blocks(Texture t, int bpe, byte[] px, BlockFn fn)
    {
        var lin = Linear(t, bpe, true);
        int wb = (t.Width + 3) / 4, hb = (t.Height + 3) / 4;
        for (int by = 0; by < hb; by++)
            for (int bx = 0; bx < wb; bx++)
                fn(lin, (by * wb + bx) * bpe, bx, by, wb);
    }

    /// <summary>One BC1 block into an RGBA image of w x h. bc3: the colour half of a BC3 block (alpha comes separately).</summary>
    static void Bc1Block(byte[] b, int o, int bx, int by, byte[] px, int w, int h, bool bc3)
    {
        Span<int> pal = stackalloc int[16];
        Bc1Palette(b, o, pal, bc3);
        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o + 4));
        for (int i = 0; i < 16; i++)
        {
            int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
            if (x >= w || y >= h) continue;
            int sel = (int)(bits >> (2 * i)) & 3, p = (y * w + x) * 4;
            px[p] = (byte)pal[sel * 4]; px[p + 1] = (byte)pal[sel * 4 + 1]; px[p + 2] = (byte)pal[sel * 4 + 2];
            if (!bc3) px[p + 3] = (byte)pal[sel * 4 + 3];
        }
    }

    /// <summary>The 4 RGBA colours of a BC1 block. forceFour: BC3's colour part always uses the 4-colour mode.</summary>
    public static void Bc1Palette(byte[] b, int o, Span<int> pal, bool forceFour)
    {
        int c0 = b[o] | b[o + 1] << 8, c1 = b[o + 2] | b[o + 3] << 8;
        static void Rgb(int c, Span<int> p, int at) { p[at] = ((c >> 11) & 31) * 255 / 31; p[at + 1] = ((c >> 5) & 63) * 255 / 63; p[at + 2] = (c & 31) * 255 / 31; p[at + 3] = 255; }
        Rgb(c0, pal, 0); Rgb(c1, pal, 4);
        if (c0 > c1 || forceFour)
            for (int k = 0; k < 3; k++) { pal[8 + k] = (2 * pal[k] + pal[4 + k]) / 3; pal[12 + k] = (pal[k] + 2 * pal[4 + k]) / 3; }
        else
            for (int k = 0; k < 3; k++) { pal[8 + k] = (pal[k] + pal[4 + k]) / 2; pal[12 + k] = 0; }
        pal[11] = 255; pal[15] = c0 > c1 || forceFour ? 255 : 0;
    }

    static void Bc4Block(byte[] b, int o, int bx, int by, int wb, byte[] px, int channel, int w, int h)
    {
        int r0 = b[o], r1 = b[o + 1];
        Span<int> v = stackalloc int[8];
        v[0] = r0; v[1] = r1;
        if (r0 > r1) for (int k = 1; k < 7; k++) v[k + 1] = ((7 - k) * r0 + k * r1) / 7;
        else { for (int k = 1; k < 5; k++) v[k + 1] = ((5 - k) * r0 + k * r1) / 5; v[6] = 0; v[7] = 255; }
        ulong bits = 0;
        for (int k = 0; k < 6; k++) bits |= (ulong)b[o + 2 + k] << (8 * k);
        for (int i = 0; i < 16; i++)
        {
            int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
            if (x >= w || y >= h) continue;
            px[(y * w + x) * 4 + channel] = (byte)v[(int)(bits >> (3 * i)) & 7];
        }
    }
}
