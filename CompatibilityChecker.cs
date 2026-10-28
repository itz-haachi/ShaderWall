using System.Text.RegularExpressions;

// A list of check results, printed as [OK] / [!] / [X] lines.
class CheckReport
{
    public List<(string Mark, string Text)> Items = new();
    public bool CanRun = true;

    public void Ok(string text) => Items.Add(("OK", text));
    public void Warn(string text) => Items.Add(("!", text));
    public void Fail(string text) { Items.Add(("X", text)); CanRun = false; }

    public void Print()
    {
        Console.WriteLine("Compatibility check");
        foreach (var (mark, text) in Items)
            Console.WriteLine($"  [{mark}] {text}");
    }
}

// Looks at a shader before it runs, and explains compile errors in Shadertoy terms.
static class CompatibilityChecker
{
    // Checks we can do just by reading the code (no GPU needed)
    public static CheckReport CheckSource(string imageCode, string commonCode, bool? multiPass)
    {
        var report = new CheckReport();
        string all = commonCode + "\n" + imageCode;

        // Must have the Shadertoy entry point
        if (Regex.IsMatch(imageCode, @"void\s+mainImage\s*\("))
            report.Ok("Has mainImage()");
        else
            report.Fail("No mainImage() found. Is this the Image tab?");

        // Multi-pass is only known when the shader came from the API (or a saved reply)
        if (multiPass == true)
            report.Warn("Multi-pass shader (Buffer tabs): only the Image tab runs, so it may look wrong");
        else if (multiPass == false)
            report.Ok("Single pass");

        // Inputs ShaderWall doesn't support yet
        if (Regex.IsMatch(all, @"\biChannel[0-3]\b"))
            report.Warn("Uses iChannel textures: not supported yet, they will read as black");
        else
            report.Ok("No texture inputs");

        if (Regex.IsMatch(all, @"\biMouse\b"))
            report.Warn("Uses iMouse: mouse input isn't supported yet");

        // Symbols like π in comments used to cut the shader short (the "premature EOF" bug)
        int nonAscii = all.Count(c => c >= 128);
        if (nonAscii > 0)
            report.Ok($"Removed {nonAscii} non-ASCII character(s), e.g. symbols in comments");

        return report;
    }

    // ShaderBuilder marks each part with "#line 1 <number>", so errors say which part they're in
    static readonly string[] PartNames = { "ShaderWall header", "Common tab", "Image tab", "ShaderWall footer" };

    public static void PrintCompileErrors(string log)
    {
        Console.WriteLine("  [X] Doesn't compile:");
        foreach (string raw in log.Split('\n'))
        {
            string line = raw.Trim();
            if (line == "") continue;

            // Drivers write the position as "2(5)" (NVIDIA) or "2:5" (AMD, Intel): part 2, line 5
            Match m = Regex.Match(line, @"(\d+)[:(](\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int part) && part < PartNames.Length)
                Console.WriteLine($"      {PartNames[part]}, line {m.Groups[2].Value}:  {line}");
            else
                Console.WriteLine("      " + line);
        }
    }
}
