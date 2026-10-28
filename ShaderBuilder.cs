using System.Linq;
using OpenTK.Graphics.OpenGL4;

record CompileResult(int Program, bool Ok, string Log);

// Turns Shadertoy code into a complete GLSL program and compiles it.
static class ShaderBuilder
{
    // One oversized triangle that covers the whole screen
    const string VertexCode = """
        #version 330 core
        void main()
        {
            vec2 pos[3] = vec2[](vec2(-1,-1), vec2(3,-1), vec2(-1,3));
            gl_Position = vec4(pos[gl_VertexID], 0, 1);
        }
        """;

    // What Shadertoy adds invisibly before your code.
    // Inputs ShaderWall doesn't fill yet are still declared, so shaders that mention them compile.
    const string Header = """
        #version 430 core
        #define HW_PERFORMANCE 1
        out vec4 outColor;
        uniform vec3 iResolution;
        uniform float iTime;
        uniform float iTimeDelta;
        uniform float iFrameRate;
        uniform int iFrame;
        uniform vec4 iMouse;
        uniform vec4 iDate;
        uniform float iSampleRate;
        uniform float iChannelTime[4];
        uniform vec3 iChannelResolution[4];
        uniform sampler2D iChannel0;
        uniform sampler2D iChannel1;
        uniform sampler2D iChannel2;
        uniform sampler2D iChannel3;
        """;

    // The real entry point: calls the Shadertoy mainImage
    const string Footer = """
        void main()
        {
            mainImage(outColor, gl_FragCoord.xy);
        }
        """;

    // Glue header + Common + Image + footer together.
    // "#line 1 N" restarts line numbering for each part, so error messages point at the right tab.
    public static string Build(string imageCode, string commonCode)
    {
        string code = Header
            + "\n#line 1 1\n" + commonCode
            + "\n#line 1 2\n" + imageCode
            + "\n#line 1 3\n" + Footer;

        // Keep plain ASCII only: symbols like π in comments can truncate the shader (the "premature EOF" bug)
        return new string(code.Where(c => c < 128).ToArray());
    }

    // Compile both shaders and link them. Reports failure instead of continuing with a broken program.
    public static CompileResult Compile(string fragmentCode)
    {
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, VertexCode);
        GL.CompileShader(vs);
        GL.GetShader(vs, ShaderParameter.CompileStatus, out int vsOk);

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fragmentCode);
        GL.CompileShader(fs);
        GL.GetShader(fs, ShaderParameter.CompileStatus, out int fsOk);

        if (vsOk == 0 || fsOk == 0)
            return new CompileResult(0, false, GL.GetShaderInfoLog(vs) + "\n" + GL.GetShaderInfoLog(fs));

        int program = GL.CreateProgram();
        GL.AttachShader(program, vs);
        GL.AttachShader(program, fs);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);

        if (linked == 0)
            return new CompileResult(0, false, GL.GetProgramInfoLog(program));

        return new CompileResult(program, true, "");
    }
}
