using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using SkiaSharp;

namespace SlateCompanion;

public static partial class ModelRender
{
    static readonly string[] KnownNames =
    [
        "ModelData", "ModelData_0", "ModelData_1", "Base", "Folder", "Unit", "Unit_0", "Unit_1", "Unit_2", "UnitName", "BindBone",
        "ControllerInfo", "Attention", "ModelList", "param_root", "Armor", "ArmorHead", "ArmorUpper", "ArmorLower", "ArmorEffect",
        "EarRot", "MantleType", "MaskType", "StarNum", "DefenceAddLevel", "WindScaleMesh", "WindScale", "NextRankName",
        "AffectTransOffsetShield1", "AffectTransOffsetShield2", "IsDispOffPorch", "ShiekerStoneTransBone", "DisableSelfMantle",
        "UseMantleType", "General", "WeaponCommon", "StandardSheath", "Sheath", "SubModelName", "Bow", "Shield", "Item",
        "SeriesArmor", "SeriesType", "EnableCompBonus", "AnimationInfo", "ArmorSeries",
        "SheathName", "PlayerHoldTransOffset", "PlayerHoldRotOffset", "PlayerEquipTransOffset", "PlayerEquipRotOffset",
        "SquatPlayerHoldTransAddOffset", "SquatPlayerHoldRotAddOffset", "NPCHoldTransOffset", "NPCHoldRotOffset", "NPCEquipTransOffset",
        "NPCEquipRotOffset", "EnemyEquipTransOffset", "EnemyEquipRotOffset", "StandEquipTransOffset", "StandEquipRotOffset",
        "Sword", "Shield", "Bow", "Spear", "Large", "Weapon", "WeaponOption", "Equip", "Attach", "Player", "QuiverName", "ArrowName",
        "AffectTransOffsetBow", "AffectRotOffsetBow", "AffectTransOffsetSpear", "AffectRotOffsetSpear", "AffectRotOffsetShield",
        "AffectTransOffsetShield", "ArmorHead", "HeadMantleType", "HeadSwitchType", "HeadModelInHead", "HeadEarRot", "HeadMaskType",
        "UpperDisableSelfMantle", "UpperMantleType", "UpperUseMantleType", "UpperIsDispOffPorch", "UpperShiekerStoneTransBone",
        "UpperShiekerStoneRotate", "LowerDisableSelfMantle", "LowerMantleType", "LowerModelInHead", "MantleType", "SwitchType", "ModelInHead",
        "AffectTransOffsetShield2", "AffectRotOffsetShield1", "AffectRotOffsetShield2", "ArmorUpper", "ArmorLower", "Rank",
    ];

    // ================================================================== the paper doll

    /// <summary>
    /// The fixed framing box for every outfit (metres, Link's model space): Link is 1.75 m with his feet at y = 0;
    /// the box leaves room for a helmet and for weapons on his back.
    /// </summary>
    public static readonly (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) DollBounds = (-0.95f, -0.04f, -0.6f, 0.95f, 1.86f, 0.6f);

    /// <summary>Arms lowered from the T-pose (degrees about the model's Z axis at the shoulder).</summary>
    const float ArmDown = 72;

    /// <summary>
    /// Link in the outfit, posed (arms down, weapons on his back), as one vertex array with a draw range per part.
    /// Pure apart from read-only caches of parsed models and decoded textures (thread-safe). Throws when Link's own
    /// model is missing; a missing armor or weapon is logged and left out.
    /// </summary>
    public static OutfitMesh BuildOutfit(string romfs, Outfit outfit)
    {
        var sw = Stopwatch.StartNew();
        var pieces = new List<(ModelReader.Mesh Mesh, string Folder, Matrix4x4? Attach)>();

        // Link's own body; the armor slots replace parts of it (each armor model brings the skin it shows)
        var head = Actor(romfs, outfit.Head ?? "Armor_Default_Head");
        var upper = Actor(romfs, outfit.Upper ?? "Armor_Default_Upper");
        var lower = Actor(romfs, outfit.Lower ?? "Armor_Default_Lower");
        bool bareUpper = outfit.Upper == null;
        var link = Model(romfs, "Link");
        if (link.Count == 0) throw new FileNotFoundException("Link's model (Model/Link.sbfres in Pack/TitleBG.pack) wasn't found in the game files");
        foreach (var m in link)
        {
            // the default lower piece (shorts) has its own legs; Link's own legs and underwear are never shown with it
            if (m.Material is "Mt_Lower_Skin" or "Mt_Underwear") continue;
            if (!bareUpper && m.Material is "Mt_Upper_Skin" or "Mt_Belt_A" or "Mt_Belt_C") continue;
            pieces.Add((m, "Link", null));
        }
        foreach (var a in new[] { head, upper, lower })
            pieces.AddRange(Units(romfs, a).Select(m => (m, a.Folder(m), (Matrix4x4?)null)));
        // the pouches on his belt, unless the body armor hides them (ArmorUpper.IsDispOffPorch)
        if (!upper.HidePouch)
            foreach (var m in Model(romfs, "Armor_Default").Where(m => m.Model.StartsWith("Armor_Default_Extra_", StringComparison.Ordinal)))
                pieces.Add((m, "Armor_Default", null));

        // pose: Link's skeleton with the arms lowered; every other skeleton follows by bone name
        var linkSkel = link[0].Skeleton!;
        var posed = PoseLink(linkSkel);
        var posedByName = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
        for (int i = 0; i < linkSkel.Names.Length; i++) posedByName.TryAdd(linkSkel.Names[i], posed[i]);

        // weapons, shield and bow sheathed on his back: hung from the Pod_A bone (upper back) with each item's own
        // PlayerHoldTrans/RotOffset (+ its AffectTrans/RotOffsetShield/Bow when those are carried too)
        var pod = posedByName.TryGetValue("Pod_A", out var pa) ? pa : Matrix4x4.CreateTranslation(0, 1.32f, -0.12f);
        foreach (var (slot, name) in new[] { ("weapon", outfit.Weapon), ("shield", outfit.Shield), ("bow", outfit.Bow) })
        {
            if (name == null) continue;
            var a = Actor(romfs, name);
            var hold = HoldMatrix(a, outfit.Shield != null && slot != "shield", outfit.Bow != null && slot != "bow") * pod;
            pieces.AddRange(Units(romfs, a).Select(m => (m, a.Folder(m), (Matrix4x4?)hold)));
            if (a.Sheath != null)
            {
                var s = Actor(romfs, a.Sheath);
                pieces.AddRange(Units(romfs, s).Select(m => (m, s.Folder(m), (Matrix4x4?)hold)));
            }
        }

        // one shared vertex array
        var pos = new List<float>(1 << 18); var nrm = new List<float>(1 << 18); var uvs = new List<float>(1 << 17);
        var result = new OutfitMesh { Bounds = DollBounds };
        var skinCache = new Dictionary<ModelReader.Skeleton, Matrix4x4[]>(ReferenceEqualityComparer.Instance);
        var missing = new SortedSet<string>();
        foreach (var (m, folder, attach) in pieces)
        {
            int baseVertex = pos.Count / 3, n = m.Positions.Length;
            Matrix4x4[]? skin = null;
            if (attach == null && m.Skeleton != null && !skinCache.TryGetValue(m.Skeleton, out skin))
                skinCache[m.Skeleton] = skin = SkinMatrices(m.Skeleton, posedByName);
            for (int v = 0; v < n; v++)
            {
                Vector3 p = m.Positions[v], nn = m.Normals?[v] ?? Vector3.UnitY;
                if (attach is { } at) { p = Vector3.Transform(p, at); nn = Vector3.TransformNormal(nn, at); }
                else if (skin != null && m.Bones != null)
                {
                    Vector3 sp = default, sn = default;
                    for (int k = 0; k < m.Influences; k++)
                    {
                        float w = m.Weights![v * m.Influences + k];
                        if (w <= 0) continue;
                        var bm = skin[m.Bones[v * m.Influences + k]];
                        sp += w * Vector3.Transform(p, bm);
                        sn += w * Vector3.TransformNormal(nn, bm);
                    }
                    p = sp; nn = sn;
                }
                float len = nn.Length();
                if (len > 1e-6f) nn /= len;
                pos.Add(p.X); pos.Add(p.Y); pos.Add(p.Z);
                nrm.Add(nn.X); nrm.Add(nn.Y); nrm.Add(nn.Z);
                var uv = m.Uvs?[v] ?? Vector2.Zero;
                uvs.Add(uv.X); uvs.Add(uv.Y);
            }
            var idx = new uint[m.Indices.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = (uint)(m.Indices[i] + baseVertex);
            var part = new OutfitPart { Indices = idx, AlphaTest = m.AlphaTest, Name = $"{m.Model}/{m.Shape}" };
            if (m.Albedo != null && m.Uvs != null)
            {
                var t = Texture(romfs, folder, m.Albedo);
                if (t != null) { part.Rgba = t.Px; part.TextureWidth = t.W; part.TextureHeight = t.H; }
                else missing.Add(m.Albedo);
            }
            result.Parts.Add(part);
        }
        result.Positions = pos.ToArray(); result.Normals = nrm.ToArray(); result.Uvs = uvs.ToArray();
        if (missing.Count > 0) Log.Warn($"Paper doll: textures not found: {string.Join(", ", missing)}");
        Log.Info($"Paper doll: {result.Parts.Count} parts, {result.Positions.Length / 3:N0} vertices, {result.Parts.Sum(p => p.Indices.Length) / 3:N0} triangles in {sw.ElapsedMilliseconds} ms ({outfit})");
        return result;
    }

    /// <summary>The outfit drawn in software: a transparent, square, front view with the fixed framing (one z-buffer).</summary>
    public static SKBitmap RenderDoll(string romfs, Outfit outfit, int size = 768) => RenderDoll(BuildOutfit(romfs, outfit), size, View.Front);

    public static SKBitmap RenderDoll(OutfitMesh o, int size, View view)
    {
        int n = o.Positions.Length / 3;
        var p = new Vector3[n]; var nv = new Vector3[n]; var uv = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            p[i] = new Vector3(o.Positions[i * 3], o.Positions[i * 3 + 1], o.Positions[i * 3 + 2]);
            nv[i] = new Vector3(o.Normals[i * 3], o.Normals[i * 3 + 1], o.Normals[i * 3 + 2]);
            uv[i] = new Vector2(o.Uvs[i * 2], o.Uvs[i * 2 + 1]);
        }
        var textures = new Dictionary<string, (int, int, byte[])>();
        var keys = new Dictionary<byte[], string>(ReferenceEqualityComparer.Instance);
        var meshes = new List<ModelReader.Mesh>();
        foreach (var part in o.Parts)
        {
            string? key = null;
            if (part.Rgba != null && !keys.TryGetValue(part.Rgba, out key))
            {
                keys[part.Rgba] = key = "t" + keys.Count;
                textures[key] = (part.TextureWidth, part.TextureHeight, part.Rgba);
            }
            meshes.Add(new ModelReader.Mesh
            {
                Model = "", Shape = part.Name, Material = "", Positions = p, Normals = nv, Uvs = uv,
                Indices = Array.ConvertAll(part.Indices, x => (int)x), Albedo = key, AlphaTest = part.AlphaTest,
            });
        }
        var b = o.Bounds;
        var frame = view is View.Side
            ? (new Vector2(b.minZ, b.minY), new Vector2(b.maxZ, b.maxY))
            : (new Vector2(b.minX, b.minY), new Vector2(b.maxX, b.maxY));
        if (view is View.Side) { float half = (b.maxY - b.minY) / 2; frame = (new Vector2(-half, b.minY), new Vector2(half, b.maxY)); }
        var r = Render(meshes, textures, size, view, frame);
        var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(r.Rgba, 0, bmp.GetPixels(), r.Rgba.Length);
        return bmp;
    }

    /// <summary>Link's bones posed: the bind pose with both arms lowered about the shoulder joints.</summary>
    static Matrix4x4[] PoseLink(ModelReader.Skeleton s)
    {
        float a = ArmDown * MathF.PI / 180;
        var turn = new Dictionary<string, Matrix4x4>
        {
            ["Arm_1_L"] = Matrix4x4.CreateRotationZ(-a), ["Arm_1_R"] = Matrix4x4.CreateRotationZ(a),
            // shoulder helper bones follow halfway, so the shoulder doesn't pinch
            ["Clavicle_Assist_L"] = Matrix4x4.CreateRotationZ(-a / 2), ["Clavicle_Assist_R"] = Matrix4x4.CreateRotationZ(a / 2),
        };
        var w = new Matrix4x4[s.Names.Length];
        for (int i = 0; i < w.Length; i++)
        {
            w[i] = s.Parents[i] >= 0 ? s.Local[i] * w[s.Parents[i]] : s.Local[i];
            if (turn.TryGetValue(s.Names[i], out var r))
            {
                var o = w[i].Translation;
                w[i] = w[i] * Matrix4x4.CreateTranslation(-o) * r * Matrix4x4.CreateTranslation(o);
            }
        }
        return w;
    }

    /// <summary>Skinning matrices (bind -> posed) for a model's skeleton, taking each bone's pose from Link's by name.</summary>
    static Matrix4x4[] SkinMatrices(ModelReader.Skeleton s, Dictionary<string, Matrix4x4> linkPosed)
    {
        var posed = new Matrix4x4[s.Names.Length];
        var skin = new Matrix4x4[s.Names.Length];
        for (int i = 0; i < posed.Length; i++)
        {
            posed[i] = linkPosed.TryGetValue(s.Names[i], out var lp) ? lp
                : s.Parents[i] >= 0 ? s.Local[i] * posed[s.Parents[i]] : s.Local[i];
            skin[i] = Matrix4x4.Invert(s.World[i], out var inv) ? inv * posed[i] : Matrix4x4.Identity;
        }
        return skin;
    }

    static Matrix4x4 HoldMatrix(ActorModel a, bool withShield, bool withBow)
    {
        static Vector3 V(object? o) => o is float[] { Length: >= 3 } f ? new Vector3(f[0], f[1], f[2]) : Vector3.Zero;
        if (a.Hold == null) return Matrix4x4.Identity;
        var t = V(a.Hold["PlayerHoldTransOffset"]);
        var r = V(a.Hold["PlayerHoldRotOffset"]);
        if (withShield) { t += V(a.Hold["AffectTransOffsetShield"]); r += V(a.Hold["AffectRotOffsetShield"]); }
        if (withBow) { t += V(a.Hold["AffectTransOffsetBow"]); r += V(a.Hold["AffectRotOffsetBow"]); }
        r *= MathF.PI / 180;
        return Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z) * Matrix4x4.CreateTranslation(t);
    }

    // ------------------------------------------------------------------ actors, models, textures (cached)

    /// <summary>What an actor draws: (model file, model) pairs from its actor pack's model list, plus what the doll needs
    /// from its general parameters.</summary>
    sealed record ActorModel(string Name, List<(string Folder, string? Unit)> Units, Aamp.Obj? Hold, string? Sheath, bool HidePouch)
    {
        public string Folder(ModelReader.Mesh m) => Units.FirstOrDefault(u => u.Unit == null || u.Unit == m.Model).Folder ?? Name;
    }

    const int CacheLimit = 32;
    const long TextureCacheBytes = 160L << 20;
    static readonly ConcurrentDictionary<(string, string), Lazy<ActorModel>> actors = new();
    static readonly ConcurrentDictionary<(string, string), Lazy<List<ModelReader.Mesh>>> models = new();
    static readonly ConcurrentDictionary<(string, string), Lazy<Dictionary<string, GameAssets.Texture>>> texFiles = new();
    static readonly ConcurrentDictionary<(string, string, string), Lazy<DecodedTexture?>> decoded = new();
    static long decodedBytes;

    sealed record DecodedTexture(int W, int H, byte[] Px);

    /// <summary>Forgets every cached model and texture (e.g. when the game files change).</summary>
    public static void ClearCaches() { actors.Clear(); models.Clear(); texFiles.Clear(); decoded.Clear(); Interlocked.Exchange(ref decodedBytes, 0); }

    static T Cached<TK, T>(ConcurrentDictionary<TK, Lazy<T>> cache, TK key, Func<T> make) where TK : notnull
    {
        if (cache.Count > CacheLimit * 4) cache.Clear();
        return cache.GetOrAdd(key, _ => new Lazy<T>(make, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    static ActorModel Actor(string romfs, string actor) => Cached(actors, (romfs, actor), () =>
    {
        var units = new List<(string, string?)>();
        Aamp.Obj? hold = null; string? sheath = null; bool hidePouch = false;
        var pack = ModelReader.LoadRomfsFile(romfs, $"Actor/Pack/{actor}.sbactorpack");
        if (pack != null)
        {
            foreach (var (name, data) in GameAssets.Sarc(pack))
            {
                if (data.Length < 8 || data[0] != 'A' || data[1] != 'A' || data[2] != 'M' || data[3] != 'P') continue;
                var root = Aamp.Parse(data);
                if (name.EndsWith(".bmodellist", StringComparison.Ordinal))
                {
                    void Walk(Aamp.PList l, string? folder)
                    {
                        folder = l.Object("Base")?["Folder"] as string ?? folder;
                        foreach (var o in l.Objects)
                            if (o["UnitName"] is string u && u.Length > 0 && folder is { Length: > 0 }) units.Add((folder, u));
                        foreach (var c in l.Lists) Walk(c, folder);
                    }
                    Walk(root, null);
                }
                else if (name.EndsWith(".bgparamlist", StringComparison.Ordinal))
                {
                    hold = root.AllObjects().FirstOrDefault(o => o["PlayerHoldTransOffset"] != null);
                    // the sheath's actor name (its parameter name isn't known; it is the one string param next to the
                    // hold offsets whose value names a Weapon_Sheath actor)
                    sheath = hold?.Params.Select(p => p.Value as string).FirstOrDefault(v => v != null && v.StartsWith("Weapon_Sheath", StringComparison.Ordinal));
                    hidePouch = root.Object("ArmorUpper")?["IsDispOffPorch"] is true;
                }
            }
        }
        if (units.Count == 0)
        {
            Log.Warn($"Paper doll: no model list for {actor}; using Model/{actor}.sbfres");
            units.Add((actor, null));
        }
        return new ActorModel(actor, units, hold, sheath, hidePouch);
    });

    /// <summary>The meshes an actor draws (its model list's units, all of a file when it has no list).</summary>
    static IEnumerable<ModelReader.Mesh> Units(string romfs, ActorModel a)
    {
        foreach (var g in a.Units.GroupBy(u => u.Folder))
        {
            var ms = Model(romfs, g.Key);
            if (ms.Count == 0) { Log.Warn($"Paper doll: Model/{g.Key}.sbfres not found for {a.Name}"); continue; }
            var want = g.Select(u => u.Unit).ToHashSet();
            foreach (var m in ms) if (want.Contains(null) || want.Contains(m.Model)) yield return m;
        }
    }

    static List<ModelReader.Mesh> Model(string romfs, string file) => Cached(models, (romfs, file), () =>
    {
        var raw = ModelReader.LoadRomfsFile(romfs, $"Model/{file}.sbfres");
        return raw == null ? [] : ModelReader.Read(raw);
    });

    /// <summary>A decoded albedo (at most 1024 px a side), from the model's own .Tex file, else Link's or the default
    /// armor's (armor reuses Link's skin, hair and belt textures). One shared array per texture.</summary>
    static DecodedTexture? Texture(string romfs, string folder, string name)
    {
        foreach (var file in new[] { folder + ".Tex", "Link.Tex", "Armor_Default.Tex" }.Distinct())
        {
            var list = Cached(texFiles, (romfs, file), () =>
            {
                var raw = ModelReader.LoadRomfsFile(romfs, $"Model/{file}.sbfres");
                var d = new Dictionary<string, GameAssets.Texture>(StringComparer.Ordinal);
                if (raw == null) return d;
                try { foreach (var t in GameAssets.Textures(GameAssets.Yaz0(raw))) d.TryAdd(t.Name, t); }
                catch (InvalidDataException) { }
                return d;
            });
            if (!list.TryGetValue(name, out var tex)) continue;
            if (Interlocked.Read(ref decodedBytes) > TextureCacheBytes) { decoded.Clear(); Interlocked.Exchange(ref decodedBytes, 0); }
            return decoded.GetOrAdd((romfs, file, name), _ => new Lazy<DecodedTexture?>(() =>
            {
                try
                {
                    int w = tex.Width, h = tex.Height;
                    var px = GameAssets.DecodeRgba(tex);
                    while (w > 1024 || h > 1024) (px, w, h) = Half(px, w, h);
                    Interlocked.Add(ref decodedBytes, px.Length);
                    return new DecodedTexture(w, h, px);
                }
                catch (NotSupportedException e) { Log.Warn($"Paper doll: texture {name}: {e.Message}"); return null; }
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        return null;
    }

    static (byte[], int, int) Half(byte[] px, int w, int h)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var o = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
                for (int c = 0; c < 4; c++)
                {
                    int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1), y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                    o[(y * nw + x) * 4 + c] = (byte)((px[(y0 * w + x0) * 4 + c] + px[(y0 * w + x1) * 4 + c] + px[(y1 * w + x0) * 4 + c] + px[(y1 * w + x1) * 4 + c] + 2) / 4);
                }
        return (o, nw, nh);
    }

    // ------------------------------------------------------------------ a single actor (e.g. the cooking pot)

    /// <summary>
    /// An actor's models (through its actor pack's model list) in their bind pose, transparent background, framed to
    /// their bounds. view: "threequarter" (default: turned 35 degrees and seen from 25 degrees above), "front", "back",
    /// "side", "zup" or "auto".
    /// </summary>
    public static SKBitmap RenderActor(string romfs, string actor, int size = 512, string view = "threequarter")
    {
        var sw = Stopwatch.StartNew();
        var a = Actor(romfs, actor);
        var meshes = new List<ModelReader.Mesh>();
        var textures = new Dictionary<string, (int, int, byte[])>(StringComparer.Ordinal);
        foreach (var m in Units(romfs, a))
        {
            string? key = null;
            if (m.Albedo != null)
            {
                var folder = a.Folder(m);
                if (Texture(romfs, folder, m.Albedo) is { } t) textures[key = folder + "/" + m.Albedo] = (t.W, t.H, t.Px);
                else Log.Warn($"Actor picture {actor}: texture {m.Albedo} not found");
            }
            meshes.Add(new ModelReader.Mesh
            {
                Model = m.Model, Shape = m.Shape, Material = m.Material, Positions = m.Positions, Normals = m.Normals, Uvs = m.Uvs,
                Indices = m.Indices, Albedo = key, AlphaTest = m.AlphaTest,
            });
        }
        if (meshes.Count == 0) throw new FileNotFoundException($"no model found for actor {actor}");
        var v = Enum.TryParse<View>(view, true, out var vv) ? vv : View.ThreeQuarter;
        var r = Render(meshes, textures, size, v);
        var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(r.Rgba, 0, bmp.GetPixels(), r.Rgba.Length);
        Log.Info($"Actor picture {actor}: {string.Join(", ", a.Units.Select(u => $"{u.Folder}/{u.Unit ?? "*"}"))}, {meshes.Count} meshes, {r.Triangles} triangles, {sw.ElapsedMilliseconds} ms");
        return bmp;
    }

    static int ActorCli(string[] args)
    {
        var sw = Stopwatch.StartNew();
        int size = args.Length > 4 && int.TryParse(args[4], out var s) ? s : 512;
        using var bmp = RenderActor(args[1], args[2], size, args.Length > 5 ? args[5] : "threequarter");
        using (var f = File.Create(args[3])) bmp.Encode(f, SKEncodedImageFormat.Png, 100);
        var a = Actor(args[1], args[2]);
        Console.WriteLine($"{args[2]}: {string.Join(", ", a.Units.Select(u => $"Model/{u.Folder}.sbfres : {u.Unit ?? "(all)"}"))}");
        foreach (var m in Units(args[1], a)) Console.WriteLine($"  {m.Model}/{m.Shape} tris {m.Triangles} albedo {m.Albedo ?? "-"}{(m.AlphaTest ? " alpha-test" : "")}");
        Console.WriteLine($"  {sw.ElapsedMilliseconds} ms -> {args[3]}");
        return 0;
    }

    // ------------------------------------------------------------------ command line

    /// <summary>--render-doll &lt;romfs&gt; &lt;out.png&gt; [head=.. upper=.. lower=.. weapon=.. shield=.. bow=.. size=768 view=front|back|side]</summary>
    static int DollCli(string[] args)
    {
        string romfs = args[1], outPath = args[2];
        var kv = args.Skip(3).Select(a => a.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToLowerInvariant(), p => p[1]);
        string? G(string k) => kv.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
        var outfit = new Outfit(G("head"), G("upper"), G("lower"), G("weapon"), G("shield"), G("bow"));
        int size = int.TryParse(G("size"), out var s) ? s : 768;
        var view = Enum.TryParse<View>(G("view") ?? "front", true, out var v) ? v : View.Front;
        var t = Stopwatch.StartNew();
        var mesh = BuildOutfit(romfs, outfit);
        var cold = t.Elapsed; t.Restart();
        mesh = BuildOutfit(romfs, outfit);
        var warm = t.Elapsed; t.Restart();
        using var bmp = RenderDoll(mesh, size, view);
        var render = t.Elapsed; t.Restart();
        using (var f = File.Create(outPath)) bmp.Encode(f, SKEncodedImageFormat.Png, 100);
        Console.WriteLine($"{outfit}");
        foreach (var p in mesh.Parts) Console.WriteLine($"  {p.Name}: {p.Indices.Length / 3} tris, {(p.Rgba != null ? $"{p.TextureWidth}x{p.TextureHeight}" : "no texture")}{(p.AlphaTest ? ", alpha test" : "")}");
        Console.WriteLine($"  {mesh.Parts.Count} parts, {mesh.Positions.Length / 3:N0} vertices, {mesh.Parts.Sum(p => p.Indices.Length) / 3:N0} triangles, {mesh.Parts.Select(p => p.Rgba).OfType<byte[]>().Distinct().Count()} textures");
        Console.WriteLine($"  BuildOutfit cold {cold.TotalMilliseconds:0} ms, warm {warm.TotalMilliseconds:0} ms; render {size}px {render.TotalMilliseconds:0} ms; png {t.Elapsed.TotalMilliseconds:0} ms -> {outPath}");
        return 0;
    }

    static string NameOf(uint crc) => KnownNames.FirstOrDefault(n => Aamp.Crc(n) == crc) ?? $"0x{crc:X8}";

    static int ModelInfo(string romfs, string file)
    {
        var raw = ModelReader.LoadRomfsFile(romfs, $"Model/{file}.sbfres") ?? throw new FileNotFoundException(file);
        var meshes = ModelReader.Read(raw);
        foreach (var g in meshes.GroupBy(m => m.Model))
        {
            var sk = g.First().Skeleton!;
            Console.WriteLine($"model {g.Key}: {sk.Names.Length} bones");
            foreach (var m in g)
            {
                var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
                foreach (var p in m.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                var used = new SortedSet<string>();
                for (int i = 0; i < m.Bones!.Length; i++) if (m.Weights![i] > 0) used.Add(sk.Names[m.Bones[i]]);
                Console.WriteLine($"  {m.Shape} mat {m.Material} skin {m.SkinCount} albedo {m.Albedo} box {min:0.00}..{max:0.00} bones {string.Join(",", used.Take(12))}{(used.Count > 12 ? "..." : "")}");
            }
            for (int i = 0; i < sk.Names.Length; i++)
                Console.WriteLine($"    bone {i} {sk.Names[i]} parent {(sk.Parents[i] >= 0 ? sk.Names[sk.Parents[i]] : "-")} at {sk.World[i].Translation:0.000}");
        }
        return 0;
    }

    static int ActorInfo(string romfs, string actor)
    {
        var pack = ModelReader.LoadRomfsFile(romfs, $"Actor/Pack/{actor}.sbactorpack") ?? throw new FileNotFoundException(actor);
        foreach (var (name, data) in GameAssets.Sarc(pack))
        {
            Console.WriteLine(name);
            if (data.Length < 4 || data[0] != 'A' || data[1] != 'A') continue;
            void Dump(Aamp.PList l, string ind)
            {
                Console.WriteLine($"{ind}[{NameOf(l.Name)}]");
                foreach (var o in l.Objects)
                {
                    Console.WriteLine($"{ind}  {NameOf(o.Name)}:");
                    foreach (var p in o.Params)
                        Console.WriteLine($"{ind}    {NameOf(p.Name)} = {(p.Value is float[] f ? string.Join(", ", f) : p.Value)}");
                }
                foreach (var c in l.Lists) Dump(c, ind + "  ");
            }
            Dump(Aamp.Parse(data), "  ");
        }
        return 0;
    }
}

/// <summary>What Link wears: inventory actor names from the save (e.g. "Armor_001_Head"); null = nothing in that slot.</summary>
public sealed record Outfit(string? Head, string? Upper, string? Lower, string? Weapon, string? Shield, string? Bow);

/// <summary>
/// A posed, resolved outfit ready to draw: one shared vertex array (Link's model space, Y up, metres, posed) and draw
/// ranges per part. Several parts using the same texture share one Rgba array instance.
/// </summary>
public sealed class OutfitMesh
{
    public float[] Positions = [];
    public float[] Normals = [];
    /// <summary>uv per vertex; v = 0 is row 0 (the top) of the part's Rgba.</summary>
    public float[] Uvs = [];
    public List<OutfitPart> Parts = [];
    /// <summary>The fixed framing box, the same for every outfit (Link's body plus room for gear).</summary>
    public (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) Bounds;
}

public sealed class OutfitPart
{
    public uint[] Indices = [];
    public int TextureWidth, TextureHeight;
    /// <summary>Albedo, RGBA8, row 0 at the top; null = untextured (use Color).</summary>
    public byte[]? Rgba;
    public bool AlphaTest;
    /// <summary>Fallback colour, 0xAARRGGBB.</summary>
    public uint Color = 0xFFCCCCCC;
    public string Name = "";
}
