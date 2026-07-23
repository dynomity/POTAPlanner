$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$iconCode = @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class PotaPlannerIcon
{
    public static void Create(string outputPath)
    {
        using (var master = new Bitmap(512, 512, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(master))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using (var background = new SolidBrush(Color.FromArgb(12, 38, 70)))
            using (var border = new Pen(Color.FromArgb(54, 133, 184), 12))
            {
                graphics.FillEllipse(background, 18, 18, 476, 476);
                graphics.DrawEllipse(border, 24, 24, 464, 464);
            }

            var leaf = new Point[]
            {
                new Point(256, 70), new Point(282, 138), new Point(340, 112),
                new Point(324, 172), new Point(394, 184), new Point(332, 221),
                new Point(364, 276), new Point(294, 258), new Point(286, 350),
                new Point(256, 395), new Point(226, 350), new Point(218, 258),
                new Point(148, 276), new Point(180, 221), new Point(118, 184),
                new Point(188, 172), new Point(172, 112), new Point(230, 138)
            };

            using (var mapleRed = new SolidBrush(Color.FromArgb(215, 47, 54)))
                graphics.FillPolygon(mapleRed, leaf);

            using (var mast = new Pen(Color.White, 14))
            using (var signal = new Pen(Color.FromArgb(246, 199, 73), 13))
            using (var signalSmall = new Pen(Color.FromArgb(246, 199, 73), 10))
            using (var dot = new SolidBrush(Color.FromArgb(246, 199, 73)))
            {
                mast.StartCap = LineCap.Round;
                mast.EndCap = LineCap.Round;
                signal.StartCap = LineCap.Round;
                signal.EndCap = LineCap.Round;
                signalSmall.StartCap = LineCap.Round;
                signalSmall.EndCap = LineCap.Round;

                graphics.DrawLine(mast, 256, 168, 256, 358);
                graphics.DrawLine(mast, 226, 378, 256, 358);
                graphics.DrawLine(mast, 286, 378, 256, 358);
                graphics.FillEllipse(dot, 242, 154, 28, 28);

                graphics.DrawArc(signalSmall, 186, 174, 92, 92, 208, 124);
                graphics.DrawArc(signalSmall, 234, 174, 92, 92, 28, 124);
                graphics.DrawArc(signal, 130, 124, 150, 190, 202, 136);
                graphics.DrawArc(signal, 232, 124, 150, 190, 22, 136);
            }

            int[] sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
            var pngImages = new List<byte[]>();

            foreach (int size in sizes)
            {
                using (var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                using (var resizedGraphics = Graphics.FromImage(resized))
                using (var stream = new MemoryStream())
                {
                    resizedGraphics.SmoothingMode = SmoothingMode.HighQuality;
                    resizedGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    resizedGraphics.DrawImage(master, new Rectangle(0, 0, size, size));
                    resized.Save(stream, ImageFormat.Png);
                    pngImages.Add(stream.ToArray());
                }
            }

            using (var writer = new BinaryWriter(File.Create(outputPath)))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int size = sizes[i];
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)(size == 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(pngImages[i].Length);
                    writer.Write(offset);
                    offset += pngImages[i].Length;
                }

                foreach (byte[] image in pngImages)
                    writer.Write(image);
            }
        }
    }
}
'@

Add-Type -TypeDefinition $iconCode -ReferencedAssemblies System.Drawing

$outputPath = Join-Path $PSScriptRoot 'POTAPlanner.ico'
[PotaPlannerIcon]::Create($outputPath)
Write-Host "Created $outputPath"

