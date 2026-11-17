using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

using PurplePen.MapModel;

using Map_SkiaStd;

namespace OcadViewer.Core
{
    /// <summary>Result of loading a map file.</summary>
    /// <param name="Map">The parsed map. The caller owns it and must dispose it.</param>
    /// <param name="Format">Which on-disk format was detected.</param>
    /// <param name="Bounds">Bounding rectangle of all symbols, in paper millimetres.</param>
    /// <param name="ParseTime">Wall-clock time spent parsing.</param>
    public sealed record OcadMapLoadResult(
        Map Map,
        MapFileFormat Format,
        RectangleF Bounds,
        TimeSpan ParseTime);

    /// <summary>
    /// Loads an OCAD file into Purple Pen's <see cref="Map"/> model.
    /// </summary>
    /// <remarks>
    /// Thin by design. All the work is in the vendored parser; this only supplies
    /// the two services <see cref="Map"/> requires - text metrics and a file loader
    /// for templates and embedded bitmaps - and measures how long parsing took.
    /// </remarks>
    public static class OcadMapLoader
    {
        /// <summary>
        /// Parses <paramref name="fileBytes"/> as an OCAD (or OpenOrienteering) map.
        /// </summary>
        /// <param name="fileBytes">Raw file contents.</param>
        /// <param name="fileName">
        /// Path or name the bytes came from. Used to resolve template references
        /// relative to the map, and reported in parse errors.
        /// </param>
        /// <param name="templateSearchDirectory">
        /// Directory searched for templates and bitmaps the map references.
        /// Defaults to the directory of <paramref name="fileName"/>.
        /// </param>
        /// <exception cref="OcadFileFormatException">
        /// The file is not a supported map, or is a version outside OCAD 6-12/2018.
        /// </exception>
        public static OcadMapLoadResult Load(
            byte[] fileBytes,
            string fileName,
            string templateSearchDirectory = null)
        {
            ArgumentNullException.ThrowIfNull(fileBytes);
            ArgumentException.ThrowIfNullOrEmpty(fileName);

            string searchDirectory = templateSearchDirectory
                ?? Path.GetDirectoryName(Path.GetFullPath(fileName))
                ?? ".";

            var stopwatch = Stopwatch.StartNew();

            var map = new Map(new Skia_TextMetrics(), new Skia_FileLoader(searchDirectory));
            MapFileFormat format;
            try {
                format = InputOutput.ReadFile(fileBytes, fileName, map);
            }
            catch {
                map.Dispose();
                throw;
            }

            stopwatch.Stop();

            RectangleF bounds;
            using (map.Read())
                bounds = map.Bounds;

            return new OcadMapLoadResult(map, format, bounds, stopwatch.Elapsed);
        }

        /// <summary>Parses the map file at <paramref name="path"/>.</summary>
        public static OcadMapLoadResult LoadFromFile(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            return Load(File.ReadAllBytes(path), path);
        }
    }
}
