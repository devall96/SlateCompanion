using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;

namespace SlateCompanion.UI;

/// <summary>
/// Link in his current gear, live in 3D on the graphics card: the whole outfit (<see cref="ModelRender.BuildOutfit"/>:
/// posed, from the player's own game files) in one depth-tested pass, simple albedo × light shading, drag to turn
/// him. It draws only when something changes (a drag, new gear, a resize): never in a loop, so it's idle like the
/// rest of the app, and nothing at all while the window is minimized or in the background. If OpenGL can't be used,
/// <see cref="Failed"/> fires and the Inventory screen shows the software renderer's still picture instead.
/// </summary>
public sealed class LinkView3D : OpenGlControlBase
{
    OutfitMesh? mesh, uploaded;
    float yaw = 0.35f, pitch = 0.06f;
    Point? dragFrom;
    bool ready, broken;
    int program, vao, vbo, uLoc, uMvp, uModel, uTex, uHasTex, uAlpha, uColR, uColG, uColB;
    readonly List<(int ibo, int count, int tex, bool alpha, uint color)> parts = [];
    readonly Dictionary<byte[], int> textures = new(ReferenceEqualityComparer.Instance);
    static readonly Color4 Background = new(0x0F / 255f, 0x21 / 255f, 0x29 / 255f);  // (the panel it sits in, over the app background)

    public event Action<string>? Failed;

    readonly record struct Color4(float R, float G, float B);

    public OutfitMesh? Mesh
    {
        get => mesh;
        set { mesh = value; RequestNextFrameRendering(); }
    }

    // ------------------------------------------------------------------ GL

    const int GL_ARRAY_BUFFER = 0x8892, GL_ELEMENT_ARRAY_BUFFER = 0x8893, GL_STATIC_DRAW = 0x88E4, GL_FLOAT = 0x1406,
        GL_UNSIGNED_INT = 0x1405, GL_TRIANGLES = 0x0004, GL_TEXTURE_2D = 0x0DE1, GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401,
        GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800, GL_LINEAR = 0x2601, GL_LINEAR_MIPMAP_LINEAR = 0x2703,
        GL_TEXTURE_WRAP_S = 0x2802, GL_TEXTURE_WRAP_T = 0x2803, GL_REPEAT = 0x2901, GL_DEPTH_TEST = 0x0B71, GL_LEQUAL = 0x0203,
        GL_COLOR_BUFFER_BIT = 0x4000, GL_DEPTH_BUFFER_BIT = 0x0100, GL_VERTEX_SHADER = 0x8B31, GL_FRAGMENT_SHADER = 0x8B30,
        GL_TEXTURE0 = 0x84C0, GL_FRAMEBUFFER = 0x8D40, GL_CULL_FACE = 0x0B44;

    delegate void GenerateMipmapProc(int target);
    GenerateMipmapProc? generateMipmap;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            bool es = GlVersion.Type == GlProfileType.OpenGLES;
            string head = es ? "#version 300 es\nprecision mediump float;\n" : "#version 330 core\n";
            const string vs = """
                in vec3 aPos; in vec3 aNormal; in vec2 aUv;
                uniform mat4 uMvp; uniform mat4 uModel;
                out vec3 vNormal; out vec2 vUv;
                void main() { gl_Position = uMvp * vec4(aPos, 1.0); vNormal = mat3(uModel) * aNormal; vUv = aUv; }
                """;
            const string fs = """
                in vec3 vNormal; in vec2 vUv;
                uniform sampler2D uTex; uniform float uHasTex; uniform float uAlpha; uniform float uColR; uniform float uColG; uniform float uColB;
                out vec4 frag;
                void main() {
                    vec4 c = uHasTex > 0.5 ? texture(uTex, vUv) : vec4(uColR, uColG, uColB, 1.0);
                    if (uAlpha > 0.5 && c.a < 0.5) discard;
                    vec3 n = normalize(vNormal);
                    vec3 l = normalize(vec3(-0.45, 0.6, 0.65));
                    float d = max(dot(n, l), 0.0) + 0.25 * max(dot(n, -l), 0.0);   // a little fill from behind
                    frag = vec4(c.rgb * (0.38 + 0.62 * d), 1.0);
                }
                """;
            int v = gl.CreateShader(GL_VERTEX_SHADER), f = gl.CreateShader(GL_FRAGMENT_SHADER);
            if (gl.CompileShaderAndGetError(v, head + vs) is { Length: > 0 } ev) throw new InvalidOperationException("vertex shader: " + ev);
            if (gl.CompileShaderAndGetError(f, head + fs) is { Length: > 0 } ef) throw new InvalidOperationException("fragment shader: " + ef);
            program = gl.CreateProgram();
            gl.AttachShader(program, v); gl.AttachShader(program, f);
            gl.BindAttribLocationString(program, 0, "aPos");
            gl.BindAttribLocationString(program, 1, "aNormal");
            gl.BindAttribLocationString(program, 2, "aUv");
            if (gl.LinkProgramAndGetError(program) is { Length: > 0 } el) throw new InvalidOperationException("link: " + el);
            gl.DeleteShader(v); gl.DeleteShader(f);
            uMvp = gl.GetUniformLocationString(program, "uMvp");
            uModel = gl.GetUniformLocationString(program, "uModel");
            uTex = gl.GetUniformLocationString(program, "uTex");
            uHasTex = gl.GetUniformLocationString(program, "uHasTex");
            uAlpha = gl.GetUniformLocationString(program, "uAlpha");
            uColR = gl.GetUniformLocationString(program, "uColR");
            uColG = gl.GetUniformLocationString(program, "uColG");
            uColB = gl.GetUniformLocationString(program, "uColB");
            var mip = gl.GetProcAddress("glGenerateMipmap");
            if (mip != IntPtr.Zero) generateMipmap = Marshal.GetDelegateForFunctionPointer<GenerateMipmapProc>(mip);
            ready = true;
            Log.Info($"3D Link: OpenGL ready ({GlVersion.Type} {GlVersion.Major}.{GlVersion.Minor})");
        }
        catch (Exception e) { Fail(e.Message); }
    }

    void Fail(string why)
    {
        if (broken) return;
        broken = true; ready = false;
        Log.Warn("3D Link: OpenGL not used: " + why);
        Dispatcher.UIThread.Post(() => Failed?.Invoke(why));
    }

    protected override void OnOpenGlLost() { ready = false; uploaded = null; parts.Clear(); textures.Clear(); }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (!ready) return;
        Release(gl);
        gl.DeleteProgram(program);
        ready = false;
    }

    void Release(GlInterface gl)
    {
        foreach (var p in parts) gl.DeleteBuffer(p.ibo);
        foreach (var t in textures.Values) gl.DeleteTexture(t);
        if (vbo != 0) gl.DeleteBuffer(vbo);
        if (vao != 0) gl.DeleteVertexArray(vao);
        parts.Clear(); textures.Clear(); vbo = vao = 0; uploaded = null;
    }

    unsafe void Upload(GlInterface gl, OutfitMesh m)
    {
        Release(gl);
        int n = m.Positions.Length / 3;
        var inter = new float[n * 8];  // position, normal, uv
        for (int i = 0; i < n; i++)
        {
            inter[i * 8 + 0] = m.Positions[i * 3]; inter[i * 8 + 1] = m.Positions[i * 3 + 1]; inter[i * 8 + 2] = m.Positions[i * 3 + 2];
            inter[i * 8 + 3] = m.Normals[i * 3]; inter[i * 8 + 4] = m.Normals[i * 3 + 1]; inter[i * 8 + 5] = m.Normals[i * 3 + 2];
            inter[i * 8 + 6] = m.Uvs[i * 2]; inter[i * 8 + 7] = m.Uvs[i * 2 + 1];
        }
        vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);
        vbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, vbo);
        fixed (float* p = inter) gl.BufferData(GL_ARRAY_BUFFER, inter.Length * 4, (IntPtr)p, GL_STATIC_DRAW);
        gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, 32, IntPtr.Zero);
        gl.VertexAttribPointer(1, 3, GL_FLOAT, 0, 32, (IntPtr)12);
        gl.VertexAttribPointer(2, 2, GL_FLOAT, 0, 32, (IntPtr)24);
        gl.EnableVertexAttribArray(0); gl.EnableVertexAttribArray(1); gl.EnableVertexAttribArray(2);
        foreach (var part in m.Parts)
        {
            int ibo = gl.GenBuffer();
            gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, ibo);
            fixed (uint* ip = part.Indices) gl.BufferData(GL_ELEMENT_ARRAY_BUFFER, part.Indices.Length * 4, (IntPtr)ip, GL_STATIC_DRAW);
            int tex = 0;
            if (part.Rgba != null && part.TextureWidth > 0 && !textures.TryGetValue(part.Rgba, out tex))
            {
                tex = gl.GenTexture();
                gl.BindTexture(GL_TEXTURE_2D, tex);
                fixed (byte* bp = part.Rgba) gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, part.TextureWidth, part.TextureHeight, 0, GL_RGBA, GL_UNSIGNED_BYTE, (IntPtr)bp);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
                gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
                if (generateMipmap != null) { generateMipmap(GL_TEXTURE_2D); gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR); }
                else gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                textures[part.Rgba] = tex;
            }
            parts.Add((ibo, part.Indices.Length, tex, part.AlphaTest, part.Color));
        }
        gl.BindVertexArray(0);
        uploaded = m;
        Log.Info($"3D Link: {n:N0} vertices, {m.Parts.Sum(p => p.Indices.Length) / 3:N0} triangles, {textures.Count} textures uploaded");
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!ready) return;
        try
        {
            double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int w = Math.Max(1, (int)(Bounds.Width * scale)), h = Math.Max(1, (int)(Bounds.Height * scale));
            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
            gl.Viewport(0, 0, w, h);
            gl.ClearColor(Background.R, Background.G, Background.B, 1);
            gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
            var m = mesh;
            if (m == null) return;
            if (!ReferenceEquals(m, uploaded)) Upload(gl, m);
            gl.Enable(GL_DEPTH_TEST);
            gl.DepthFunc(GL_LEQUAL);
            gl.Disable(GL_CULL_FACE);  // (thin cloth and capes have one side)

            // frame the fixed box the same way for every outfit: turn around its centre, fit its height
            var b = m.Bounds;
            var centre = new Vector3((b.minX + b.maxX) / 2, (b.minY + b.maxY) / 2, (b.minZ + b.maxZ) / 2);
            float height = b.maxY - b.minY, radius = MathF.Max(height, MathF.Max(b.maxX - b.minX, b.maxZ - b.minZ)) * 0.55f;
            var model = Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateRotationX(pitch);
            float fov = 0.5f, dist = radius / MathF.Sin(fov / 2) * 1.02f;
            var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, dist), Vector3.Zero, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, (float)w / h, dist * 0.1f, dist * 4);
            var mvp = model * view * proj;
            gl.UseProgram(program);
            gl.UniformMatrix4fv(uMvp, 1, false, &mvp);
            gl.UniformMatrix4fv(uModel, 1, false, &model);
            gl.Uniform1i(uTex, 0);
            gl.ActiveTexture(GL_TEXTURE0);
            gl.BindVertexArray(vao);
            foreach (var p in parts)
            {
                gl.Uniform1f(uHasTex, p.tex != 0 ? 1 : 0);
                gl.Uniform1f(uAlpha, p.alpha ? 1 : 0);
                gl.Uniform1f(uColR, ((p.color >> 16) & 255) / 255f); gl.Uniform1f(uColG, ((p.color >> 8) & 255) / 255f); gl.Uniform1f(uColB, (p.color & 255) / 255f);
                gl.BindTexture(GL_TEXTURE_2D, p.tex);
                gl.BindBuffer(GL_ELEMENT_ARRAY_BUFFER, p.ibo);
                gl.DrawElements(GL_TRIANGLES, p.count, GL_UNSIGNED_INT, IntPtr.Zero);
            }
            gl.BindVertexArray(0);
            gl.Disable(GL_DEPTH_TEST);
        }
        catch (Exception e) { Fail(e.Message); }
    }

    // ------------------------------------------------------------------ turning him (drag); double-click: face front again

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2) { yaw = 0.35f; pitch = 0.06f; RequestNextFrameRendering(); return; }
        dragFrom = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (dragFrom is not { } from) return;
        var p = e.GetPosition(this);
        yaw += (float)(p.X - from.X) * 0.012f;
        pitch = Math.Clamp(pitch + (float)(p.Y - from.Y) * 0.006f, -0.5f, 0.6f);
        dragFrom = p;
        RequestNextFrameRendering();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) { dragFrom = null; e.Pointer.Capture(null); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) RequestNextFrameRendering();
    }
}
