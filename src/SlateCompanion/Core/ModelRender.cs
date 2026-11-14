using System.Diagnostics;
using System.Numerics;
using SkiaSharp;

namespace SlateCompanion;

/// <summary>
/// A small offline software renderer for the paper doll: orthographic view, z-buffer, albedo texture (bilinear, wrap),
/// alpha test for cut-out materials, albedo x (0.35 ambient + 0.65 lambert) from a light at the front-top-left, two-sided
/// lighting, transparent background, 2x2 supersampling. No GPU. Output is straight-alpha RGBA8.
/// </summary>
public static partial class ModelRender
{
    /// <summary>
    /// The view: Front looks along -Z with Y up (Link faces +Z, so his left is on the image's right). ZUp turns the model
    /// so +Z points up and we look along +Y (weapons lie along Z in their files). Auto picks ZUp when the model is
    /// clearly longest along Z. ThreeQuarter turns the model 35 degrees and looks from 25 degrees above.
    /// </summary>
    public enum View { Auto, Front, ZUp, Back, Side, ThreeQuarter }

    public sealed record Result(byte[] Rgba, int Size, View View, Vector2 Min, Vector2 Max, int Triangles, int Pixels, TimeSpan Time);

    /// <summary>
    /// Renders the meshes into a square image. textures: decoded RGBA by name (missing ones draw light grey).
    /// frame: the view-space rectangle to fit (for layers that must line up); null frames the model's own bounds.
    /// </summary>
    public static Result Render(IReadOnlyList<ModelReader.Mesh> meshes, IReadOnlyDictionary<string, (int W, int H, byte[] Px)> textures,
        int size = 512, View view = View.Auto, (Vector2 Min, Vector2 Max)? frame = null, int supersample = 2)
    {
        var sw = Stopwatch.StartNew();
        if (view == View.Auto) view = PickView(meshes);
        Matrix4x4 vm = view switch
        {
            View.ZUp => Matrix4x4.CreateRotationX(-MathF.PI / 2),  // (x, y, z) -> (x, z, -y)
            View.Back => Matrix4x4.CreateRotationY(MathF.PI),
            View.Side => Matrix4x4.CreateRotationY(-MathF.PI / 2),
            // turn so the front-left shows, then tip the top towards the camera (seen from above)
            View.ThreeQuarter => Matrix4x4.CreateRotationY(35 * MathF.PI / 180) * Matrix4x4.CreateRotationX(25 * MathF.PI / 180),
            _ => Matrix4x4.Identity,
        };

        // bounds in view space
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        foreach (var m in meshes)
            foreach (int i in m.Indices)
            {
                var p = Vector3.Transform(m.Positions[i], vm);
                min = Vector2.Min(min, new Vector2(p.X, p.Y)); max = Vector2.Max(max, new Vector2(p.X, p.Y));
            }
        if (frame is { } f) { min = f.Min; max = f.Max; }
        if (min.X > max.X) { min = Vector2.Zero; max = Vector2.One; }

        int S = size * supersample;
        float extent = MathF.Max(max.X - min.X, max.Y - min.Y);
        float scale = S * 0.94f / MathF.Max(extent, 1e-6f);
        var center = (min + max) / 2;
        var color = new float[S * S * 4];
        var depth = new float[S * S];
        Array.Fill(depth, float.NegativeInfinity);
        var light = Vector3.Normalize(new Vector3(-1, 1, 1));
        int tris = 0;
        var projected = new Dictionary<Vector3[], (Vector3[] Sp, Vector3[] Nv)>(ReferenceEqualityComparer.Instance);

        foreach (var m in meshes)
        {
            (int W, int H, byte[] Px) tex = default;
            bool hasTex = m.Albedo != null && m.Uvs != null && textures.TryGetValue(m.Albedo, out tex);
            int n = m.Positions.Length;
            // meshes may share one vertex array (an outfit): transform each array once
            if (!projected.TryGetValue(m.Positions, out var pr))
            {
                pr = (new Vector3[n], new Vector3[n]);
                for (int i = 0; i < n; i++)
                {
                    var p = Vector3.Transform(m.Positions[i], vm);
                    pr.Sp[i] = new Vector3((p.X - center.X) * scale + S / 2f, S / 2f - (p.Y - center.Y) * scale, p.Z);
                    pr.Nv[i] = m.Normals != null ? Vector3.TransformNormal(m.Normals[i], vm) : Vector3.UnitZ;
                }
                projected[m.Positions] = pr;
            }
            var (sp, nv) = pr;
            for (int t = 0; t + 2 < m.Indices.Length; t += 3)
            {
                int i0 = m.Indices[t], i1 = m.Indices[t + 1], i2 = m.Indices[t + 2];
                if ((uint)i0 >= n || (uint)i1 >= n || (uint)i2 >= n) continue;
                tris++;
                Vector3 a = sp[i0], b = sp[i1], c = sp[i2];
                float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (MathF.Abs(area) < 1e-12f) continue;
                int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
                int x1 = Math.Min(S - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
                int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
                int y1 = Math.Min(S - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
                float inv = 1 / area;
                for (int y = y0; y <= y1; y++)
                {
                    float py = y + 0.5f;
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f;
                        float w0 = ((b.X - px) * (c.Y - py) - (b.Y - py) * (c.X - px)) * inv;
                        float w1 = ((c.X - px) * (a.Y - py) - (c.Y - py) * (a.X - px)) * inv;
                        float w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
                        int o = y * S + x;
                        if (z <= depth[o]) continue;
                        float r = 0.8f, g = 0.8f, bl = 0.8f, al = 1;
                        if (hasTex)
                        {
                            var uv = w0 * m.Uvs![i0] + w1 * m.Uvs[i1] + w2 * m.Uvs[i2];
                            Sample(tex, uv, out r, out g, out bl, out al);
                            if (m.AlphaTest && al < 0.5f) continue;
                        }
                        var nn = w0 * nv[i0] + w1 * nv[i1] + w2 * nv[i2];
                        float len = nn.Length();
                        nn = len > 1e-6f ? nn / len : Vector3.UnitZ;
                        if (nn.Z < 0) nn = -nn; // two-sided: the side we see faces us
                        float lit = 0.35f + 0.65f * MathF.Max(0, Vector3.Dot(nn, light));
                        depth[o] = z;
                        color[o * 4] = r * lit; color[o * 4 + 1] = g * lit; color[o * 4 + 2] = bl * lit; color[o * 4 + 3] = 1;
                    }
                }
            }
        }

        // downsample (coverage becomes alpha; colours averaged over covered samples)
        var rgba = new byte[size * size * 4];
        int pixels = 0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0, cov = 0;
                for (int sy = 0; sy < supersample; sy++)
                    for (int sx = 0; sx < supersample; sx++)
                    {
                        int o = ((y * supersample + sy) * S + x * supersample + sx) * 4;
                        if (color[o + 3] == 0) continue;
                        r += color[o]; g += color[o + 1]; b += color[o + 2]; cov++;
                    }
                if (cov == 0) continue;
                pixels++;
                int q = (y * size + x) * 4;
                rgba[q] = (byte)Math.Clamp(r / cov * 255 + 0.5f, 0, 255);
                rgba[q + 1] = (byte)Math.Clamp(g / cov * 255 + 0.5f, 0, 255);
                rgba[q + 2] = (byte)Math.Clamp(b / cov * 255 + 0.5f, 0, 255);
                rgba[q + 3] = (byte)(cov * 255 / (supersample * supersample));
            }
        return new Result(rgba, size, view, min, max, tris, pixels, sw.Elapsed);
    }

    static View PickView(IReadOnlyList<ModelReader.Mesh> meshes)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var m in meshes) foreach (var p in m.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        var e = max - min;
        // look along the thinnest axis: weapons and shields lie flat in X-Z (thin in Y), so they are seen from above
        return e.Y < e.X && e.Y < e.Z ? View.ZUp : View.Front;
    }

    static void Sample((int W, int H, byte[] Px) t, Vector2 uv, out float r, out float g, out float b, out float a)
    {
        float u = uv.X * t.W - 0.5f, v = uv.Y * t.H - 0.5f;
        int x0 = (int)MathF.Floor(u), y0 = (int)MathF.Floor(v);
        float fx = u - x0, fy = v - y0;
        r = g = b = a = 0;
        for (int k = 0; k < 4; k++)
        {
            int x = x0 + (k & 1), y = y0 + (k >> 1);
            float w = ((k & 1) != 0 ? fx : 1 - fx) * ((k >> 1) != 0 ? fy : 1 - fy);
            x = ((x % t.W) + t.W) % t.W; y = ((y % t.H) + t.H) % t.H;
            int o = (y * t.W + x) * 4;
            r += w * t.Px[o]; g += w * t.Px[o + 1]; b += w * t.Px[o + 2]; a += w * t.Px[o + 3];
        }
        r /= 255; g /= 255; b /= 255; a /= 255;
    }

    public static void SavePng(byte[] rgba, int size, string path)
    {
        using var img = SKImage.FromPixelCopy(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul), rgba);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir != null) Directory.CreateDirectory(dir);
        using var f = File.Create(path);
        data.SaveTo(f);
    }

    // ------------------------------------------------------------------ command line

    /// <summary>
    /// --render-model &lt;romfs&gt; &lt;file[:model,model...]&gt; &lt;out.png&gt; [size] [auto|front|zup|back|side]:
    /// loads Model/&lt;file&gt;.sbfres and its .Tex textures, renders, writes the PNG, prints what it did.
    /// </summary>
    public static int Cli(string[] args)
    {
        if (args[0] == "--model-info") return ModelInfo(args[1], args[2]);
        if (args[0] == "--actor-info") return ActorInfo(args[1], args[2]);
        if (args[0] == "--render-doll") return DollCli(args);
        if (args[0] == "--render-actor") return ActorCli(args);
        var total = Stopwatch.StartNew();
        string romfs = args[1], spec = args[2], outPath = args[3];
        int size = args.Length > 4 && int.TryParse(args[4], out var sz) ? sz : 512;
        var view = args.Length > 5 && Enum.TryParse<View>(args[5], true, out var vw) ? vw : View.Auto;
        string file = spec.Split(':')[0];
        var only = spec.Contains(':') ? spec[(spec.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;

        var t0 = Stopwatch.StartNew();
        var raw = ModelReader.LoadRomfsFile(romfs, $"Model/{file}.sbfres");
        if (raw == null) { Console.Error.WriteLine($"Model/{file}.sbfres not found (loose or in Pack/TitleBG.pack)"); return 2; }
        var notes = new List<string>();
        var meshes = ModelReader.Read(raw, notes);
        if (only != null) meshes = meshes.Where(m => only.Contains(m.Model) || only.Contains(m.Shape)).ToList();
        else
        {
            // armor heads come as variants (<name>_Head_A, _Head_B, ...): draw only the first unless asked
            var models = meshes.Select(m => m.Model).ToHashSet();
            meshes = meshes.Where(m => !(m.Model.Length > 2 && m.Model[^2] == '_' && m.Model[^1] > 'A'
                && models.Contains(m.Model[..^1] + "A"))).ToList();
        }
        var readTime = t0.Elapsed;

        t0.Restart();
        var decoded = new Dictionary<string, (int, int, byte[])>(StringComparer.Ordinal);
        var available = new Dictionary<string, GameAssets.Texture>(StringComparer.Ordinal);
        var texFiles = new List<string>();
        // armor reuses Link's own textures (skin, hair, belts) from Link.Tex.sbfres
        var texSources = new[] { ".Tex", ".Tex1", ".Tex2", "" }.Select(x => file + x).ToList();
        if (file != "Link") texSources.Add("Link.Tex");
        foreach (var source in texSources)
        {
            if (source == "Link.Tex" && meshes.All(m => m.Albedo == null || available.ContainsKey(m.Albedo))) continue;
            var tr = ModelReader.LoadRomfsFile(romfs, $"Model/{source}.sbfres");
            if (tr == null) continue;
            try
            {
                foreach (var t in GameAssets.Textures(GameAssets.Yaz0(tr))) available.TryAdd(t.Name, t);
                texFiles.Add($"{source}.sbfres");
            }
            catch (InvalidDataException) { }
        }
        var missing = new SortedSet<string>(); var failed = new SortedSet<string>();
        foreach (var name in meshes.Select(m => m.Albedo).OfType<string>().Distinct())
        {
            if (!available.TryGetValue(name, out var t)) { missing.Add(name); continue; }
            try
            {
                decoded[name] = (t.Width, t.Height, GameAssets.DecodeRgba(t));
                // MODEL_DUMP_TEX=<folder>: also writes each decoded albedo (debugging)
                if (Environment.GetEnvironmentVariable("MODEL_DUMP_TEX") is { Length: > 0 } dump && t.Width == t.Height)
                    SavePng(decoded[name].Item3, t.Width, Path.Combine(dump, name + $"_{t.Format:X}.png"));
            }
            catch (NotSupportedException e) { failed.Add($"{name} ({e.Message})"); }
        }
        var texTime = t0.Elapsed;

        // frame=minX,minY,maxX,maxY: a fixed view rectangle so separately rendered layers line up
        (Vector2, Vector2)? frame = null;
        if (args.FirstOrDefault(a => a.StartsWith("frame=", StringComparison.Ordinal)) is { } fa)
        {
            var v = fa[6..].Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            frame = (new Vector2(v[0], v[1]), new Vector2(v[2], v[3]));
        }
        var res = Render(meshes, decoded, size, view, frame);
        t0.Restart();
        SavePng(res.Rgba, size, outPath);
        var saveTime = t0.Elapsed;

        Console.WriteLine($"{file}: {meshes.Count} meshes, {res.Triangles} triangles, view {res.View}, bounds x {res.Min.X:0.###}..{res.Max.X:0.###} y {res.Min.Y:0.###}..{res.Max.Y:0.###}");
        foreach (var m in meshes)
            Console.WriteLine($"  {m.Model}/{m.Shape}  mat {m.Material}  tris {m.Triangles}  skin {m.SkinCount}  albedo {m.Albedo ?? "-"}{(m.AlphaTest ? "  alpha-test" : "")}{(m.Albedo != null && !decoded.ContainsKey(m.Albedo) ? "  [no texture]" : "")}");
        Console.WriteLine($"  texture files: {(texFiles.Count == 0 ? "none" : string.Join(", ", texFiles))}; textures decoded {decoded.Count}, missing {missing.Count}{(missing.Count > 0 ? ": " + string.Join(", ", missing) : "")}, unsupported {failed.Count}{(failed.Count > 0 ? ": " + string.Join(", ", failed) : "")}");
        foreach (var n in notes) Console.WriteLine("  note: " + n);
        Console.WriteLine($"  time: read {readTime.TotalMilliseconds:0} ms, textures {texTime.TotalMilliseconds:0} ms, render {res.Time.TotalMilliseconds:0} ms, png {saveTime.TotalMilliseconds:0} ms, total {total.Elapsed.TotalMilliseconds:0} ms");
        Console.WriteLine($"  wrote {outPath} ({size}x{size}, {res.Pixels} visible pixels)");
        return 0;
    }
}
