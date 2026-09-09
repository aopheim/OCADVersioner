using System;
using System.Collections.Generic;
using System.IO;

using PurplePen.Graphics2D;
using PurplePen.MapModel;

using Map_SkiaStd;

namespace OcadViewer.Core
{
    /// <summary>
    /// Registers the bundled font set with Skia.
    /// </summary>
    /// <remarks>
    /// This exists because a browser gives the rendering engine no system fonts at
    /// all, so every face a map asks for has to be supplied by us. Fonts must be
    /// registered <em>before</em> parsing, not merely before drawing: OcadImport
    /// calls ITextMetrics.TextFaceIsInstalled during import to build its
    /// missing-font list, and text symbol layout measures glyphs as it reads.
    ///
    /// Registration is by file path. SkiaFontManager.AddFontFile calls
    /// Path.GetFullPath / File.Exists and then SKTypeface.FromFile, with no stream
    /// overload. On WebAssembly that still works, because the .NET runtime there
    /// runs on Emscripten's in-memory filesystem and Skia is linked into the same
    /// module - so writing the bytes to a virtual path first is enough.
    /// </remarks>
    public static class FontPack
    {
        /// <summary>Family used when a map asks for a face we do not have.</summary>
        public const string FallbackFamily = "Roboto";

        /// <summary>
        /// The bundled faces, as (family, effects, file name).
        /// </summary>
        public static readonly IReadOnlyList<(string Family, TextEffects Effects, string FileName)> Faces =
        [
            ("Roboto",           TextEffects.Regular,                    "Roboto-Regular.ttf"),
            ("Roboto",           TextEffects.Bold,                       "Roboto-Bold.ttf"),
            ("Roboto",           TextEffects.Italic,                     "Roboto-Italic.ttf"),
            ("Roboto",           TextEffects.Bold | TextEffects.Italic,  "Roboto-BoldItalic.ttf"),
            ("Roboto Condensed", TextEffects.Regular,                    "RobotoCondensed-Regular.ttf"),
            ("Roboto Condensed", TextEffects.Bold,                       "RobotoCondensed-Bold.ttf"),
            ("Roboto Condensed", TextEffects.Italic,                     "RobotoCondensed-Italic.ttf"),
            ("Roboto Condensed", TextEffects.Bold | TextEffects.Italic,  "RobotoCondensed-BoldItalic.ttf"),
        ];

        /// <summary>
        /// Families that real OCAD maps commonly name, mapped onto a bundled face.
        /// </summary>
        /// <remarks>
        /// Without these, SkiaFontManager would fall back to <see cref="FallbackFamily"/>
        /// for the whole family and lose the bold/italic variant that was asked for.
        /// The metrics still differ from the real Arial or Helvetica, so text
        /// advances will not match OCAD exactly; shipping metric-compatible faces
        /// (Liberation Sans, Arimo) is the fix if that becomes visible.
        /// </remarks>
        public static readonly IReadOnlyList<string> AliasFamilies =
        [
            "Arial",
            "Helvetica",
            "Arial Narrow",
        ];

        /// <summary>
        /// Registers every bundled face from <paramref name="fontDirectory"/>, plus
        /// the alias families, and sets the fallback family.
        /// </summary>
        /// <returns>The number of faces registered.</returns>
        public static int InstallFromDirectory(string fontDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(fontDirectory);

            if (!Directory.Exists(fontDirectory))
                throw new DirectoryNotFoundException($"Font directory not found: {fontDirectory}");

            int count = 0;

            foreach ((string family, TextEffects effects, string fileName) in Faces) {
                string path = Path.Combine(fontDirectory, fileName);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Bundled font missing: {fileName}", path);

                SkiaFontLoader.Instance.AddFontFile(family, effects, path);
                count++;

                // Map the common OCAD family names onto the same files, so that at
                // least the requested weight and slant survive the substitution.
                if (family == "Roboto") {
                    foreach (string alias in AliasFamilies) {
                        SkiaFontLoader.Instance.AddFontFile(alias, effects, path);
                        count++;
                    }
                }
            }

            SkiaFontManager.SetDefaultFontFamilyName(FallbackFamily);
            return count;
        }
    }
}
