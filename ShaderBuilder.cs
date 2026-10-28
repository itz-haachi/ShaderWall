using OpenTK.Graphics.OpenGL4;

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

    // What Shadertoy adds invisibly before your code
    const string Header = """
        #version 430 core
        out vec4 outColor;
        uniform vec3 iResolution;
        uniform float iTime;
        uniform float iTimeDelta;
        uniform int iFrame;
        uniform vec4 iMouse;
        """;

    // The real entry point: calls the Shadertoy mainImage
    const string Footer = """
        void main()
        {
            mainImage(outColor, gl_FragCoord.xy);
        }
        """;

    // Read the Image tab (and the Common tab, if the file exists) from disk
    public static string LoadFromFiles(string imagePath, string commonPath)
    {
        string image = File.ReadAllText(imagePath);
        string common = File.Exists(commonPath) ? File.ReadAllText(commonPath) : "";
        return Build(image, common);
    }

    // Glue header + Common + Image + footer together
    public static string Build(string imageCode, string commonCode)
    {
        string code = Header + "\n" + commonCode + "\n" + imageCode + "\n" + Footer;

        // Keep plain ASCII only: symbols like π in comments can truncate the shader (the "premature EOF" bug)
        return new string(code.Where(c => c < 128).ToArray());
    }

    // Compile both shaders, link them, and return the program ID
    public static int Compile(string fragmentCode)
    {
        int vs = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vs, VertexCode);
        GL.CompileShader(vs);
        PrintLog("VERTEX", GL.GetShaderInfoLog(vs));

        int fs = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fs, fragmentCode);
        GL.CompileShader(fs);
        PrintLog("FRAGMENT", GL.GetShaderInfoLog(fs));

        int program = GL.CreateProgram();
        GL.AttachShader(program, vs);
        GL.AttachShader(program, fs);
        GL.LinkProgram(program);
        return program;
    }

    static void PrintLog(string label, string log)
    {
        if (!string.IsNullOrWhiteSpace(log))
            Console.WriteLine(label + " ERROR: " + log);
    }
}
