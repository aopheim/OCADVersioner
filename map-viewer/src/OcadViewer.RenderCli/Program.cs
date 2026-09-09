using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;

using OcadViewer.Core;

using SkiaSharp;

namespace OcadViewer.RenderCli
{
    /// <summary>
    /// Renders OCAD files to PNG on the desktop runtime.
    /// </summary>
    /// <remarks>
    /// This exists to prove the vendored parser and renderer work, and to produce
    /// the baseline images that later WebAssembly output is compared against. It
    /// deliberately has no Blazor or browser dependency: if something is broken in
    /// the extraction itself, it should fail here, where the feedback loop is fast.
    /// </remarks>
    internal static class Program
    {
        private const int DefaultWidth = 2000;

        private static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help") {
                Usage();
                return args.Length == 0 ? 2 : 0;
            }

            try {
                var options = CliOptions.Parse(args);

                string fontDirectory = Path.Combine(AppContext.BaseDirectory, "fonts");
                int faces = FontPack.InstallFromDirectory(fontDirectory);
                Console.WriteLine($"Registered {faces} font faces from {fontDirectory}");
                Console.WriteLine();

                int failures = 0;
                foreach (string input in options.Inputs) {
                    if (!RenderOne(input, options))
                        failures++;
                }

                if (options.Inputs.Count > 1) {
                    Console.WriteLine();
                    Console.WriteLine($"{options.Inputs.Count - failures}/{options.Inputs.Count} rendered.");
                }

                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 2;
            }
        }

        private static bool RenderOne(string inputPath, CliOptions options)
        {
            string name = Path.GetFileName(inputPath);

            if (!File.Exists(inputPath)) {
                Console.Error.WriteLine($"{name}: not found");
                return false;
            }

            try {
                var loaded = OcadMapLoader.LoadFromFile(inputPath);
                using (loaded.Map) {
                    RectangleF bounds = loaded.Bounds;

                    if (bounds.Width <= 0 || bounds.Height <= 0) {
                        Console.Error.WriteLine($"{name}: map has empty bounds; nothing to render");
                        return false;
                    }

                    int width = options.Width;
                    int height = MapRenderer.HeightForWidth(bounds, width);

                    var settings = new MapRenderSettings {
                        UsePatternBitmaps = options.UsePatternBitmaps,
                        BlendOverprintedColors = !options.NoOverprint,
                    };

                    var rendered = MapRenderer.Render(loaded.Map, bounds, width, height, settings);

                    string outputPath = options.OutputPathFor(inputPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));

                    using (rendered.Bitmap)
                    using (SKData data = rendered.Bitmap.Encode(SKEncodedImageFormat.Png, 100))
                    using (FileStream stream = File.Create(outputPath))
                        data.SaveTo(stream);

                    long outputBytes = new FileInfo(outputPath).Length;

                    Console.WriteLine($"{name}");
                    Console.WriteLine($"  format     {loaded.Format.kind} v{loaded.Format.version}");
                    Console.WriteLine($"  bounds     {bounds.Width:F1} x {bounds.Height:F1} mm"
                                    + $"  at ({bounds.X:F1}, {bounds.Y:F1})");
                    Console.WriteLine($"  image      {width} x {height} px"
                                    + $"  ({MapRenderer.MinResolutionFor(bounds, width, height):F4} mm/px)");
                    Console.WriteLine($"  parse      {loaded.ParseTime.TotalMilliseconds,8:F0} ms");
                    Console.WriteLine($"  render     {rendered.RenderTime.TotalMilliseconds,8:F0} ms");
                    Console.WriteLine($"  wrote      {outputPath} ({outputBytes / 1024.0:F0} KB)");
                    return true;
                }
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"{name}: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static void Usage()
        {
            Console.WriteLine("""
                Renders OCAD map files to PNG using the vendored Purple Pen renderer.

                usage: OcadViewer.RenderCli <map.ocd> [more maps...] [options]

                options:
                  -o, --out <path>     Output file, or a directory when rendering several
                                       maps. Defaults to <input>.png beside each input.
                  -w, --width <px>     Image width in pixels. Height follows the map's
                                       aspect ratio. Default 2000.
                      --pattern-bitmaps
                                       Render area patterns with pattern brushes instead
                                       of stamped glyphs.
                      --no-overprint   Ignore the overprint flag on colours instead of
                                       blending them with Darken.
                """);
        }

        private sealed class CliOptions
        {
            public List<string> Inputs { get; } = [];
            public int Width { get; private set; } = DefaultWidth;
            public string Output { get; private set; }
            public bool UsePatternBitmaps { get; private set; }
            public bool NoOverprint { get; private set; }

            public static CliOptions Parse(string[] args)
            {
                var options = new CliOptions();

                for (int i = 0; i < args.Length; i++) {
                    string arg = args[i];
                    switch (arg) {
                        case "-o" or "--out":
                            options.Output = Next(args, ref i, arg);
                            break;
                        case "-w" or "--width":
                            string raw = Next(args, ref i, arg);
                            if (!int.TryParse(raw, out int width) || width < 1)
                                throw new ArgumentException($"invalid width: {raw}");
                            options.Width = width;
                            break;
                        case "--pattern-bitmaps":
                            options.UsePatternBitmaps = true;
                            break;
                        case "--no-overprint":
                            options.NoOverprint = true;
                            break;
                        default:
                            if (arg.StartsWith('-'))
                                throw new ArgumentException($"unknown option: {arg}");
                            options.Inputs.Add(arg);
                            break;
                    }
                }

                if (options.Inputs.Count == 0)
                    throw new ArgumentException("no input files given");

                return options;
            }

            /// <summary>
            /// Where to write the PNG for <paramref name="inputPath"/>. A single
            /// input may name the file directly; several inputs treat --out as a
            /// directory, so they cannot overwrite each other.
            /// </summary>
            public string OutputPathFor(string inputPath)
            {
                string pngName = Path.GetFileNameWithoutExtension(inputPath) + ".png";

                if (string.IsNullOrEmpty(Output))
                    return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".", pngName);

                bool outputIsDirectory =
                    Inputs.Count > 1
                    || Directory.Exists(Output)
                    || Output.EndsWith('/') || Output.EndsWith('\\');

                return outputIsDirectory ? Path.Combine(Output, pngName) : Output;
            }

            private static string Next(string[] args, ref int i, string option)
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"{option} needs a value");
                return args[++i];
            }
        }
    }
}
