using System;
using System.IO;
using Personify.Cards;

// Writes <out>/<name>.rgba (900x1260 RGBA) for a few sample NPCs. The body and head are placeholder silhouettes;
// in the game they come from the preview camera.
internal static class Program
{
    private static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "out";
        Directory.CreateDirectory(dir);
        Write(dir, "customer", new CardArtInput { Name = "Tick Ruiz", Role = "customer", Code = "k7m2qp", Creator = "DooDesch" }, 1.78f);
        Write(dir, "dealer-long", new CardArtInput { Name = "Mama Lou Fitzgerald-Okonkwo", Role = "dealer", Code = "a2b3c4", Creator = "SomeVeryLongCreatorName", BasedOn = "k7m2qp" }, 1.62f);
        Write(dir, "customer-credit", new CardArtInput { Name = "Finn Murphy", Role = "customer", Code = "m3n4p5", Creator = "NPC Vault", OriginalBy = "FannsoNetti, More NPCs" }, 1.76f);
        Write(dir, "remix-credit", new CardArtInput { Name = "Finn Murphy", Role = "customer", Code = "q7r8s9", Creator = "DooDesch", BasedOn = "m3n4p5", OriginalBy = "FannsoNetti, More NPCs" }, 1.76f);
        Write(dir, "supplier-umlaut", new CardArtInput { Name = "Jürgen Weiß", Role = "supplier", Code = "zz9yx8" }, 1.95f);
        return 0;
    }

    private static void Write(string dir, string name, CardArtInput input, float meters)
    {
        input.Body = Silhouette(meters);
        input.HeadSize = 128;
        input.Head = Head(128);
        File.WriteAllBytes(Path.Combine(dir, name + ".rgba"), CardArt.Compose(input));
        Console.WriteLine("wrote " + name);
    }

    private static byte[] Silhouette(float meters)
    {
        int w = CardArt.ChartWidth, h = CardArt.ChartHeight;
        var px = new byte[w * h * 4];
        float ppm = h / CardArt.ChartMeters;
        int top = h - (int)(meters * ppm), cx = w / 2;
        for (int y = top; y < h; y++)
        {
            float t = (y - top) / (float)(h - top);
            int half = t < 0.13f ? 46 : t < 0.5f ? 110 : 70;
            for (int x = cx - half; x < cx + half; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = 40; px[i + 1] = 46; px[i + 2] = 52; px[i + 3] = 235;
            }
        }
        return px;
    }

    private static byte[] Head(int s)
    {
        var px = new byte[s * s * 4];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            int i = (y * s + x) * 4;
            bool face = (x - s / 2) * (x - s / 2) + (y - s / 2) * (y - s / 2) < (s / 3) * (s / 3);
            px[i] = face ? (byte)190 : (byte)28; px[i + 1] = face ? (byte)150 : (byte)31; px[i + 2] = face ? (byte)120 : (byte)40; px[i + 3] = 255;
        }
        return px;
    }
}
