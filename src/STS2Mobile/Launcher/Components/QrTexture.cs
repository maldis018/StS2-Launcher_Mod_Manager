using System;
using Godot;
using QRCoder;

namespace STS2Mobile.Launcher.Components;

// Renders a string (the Steam sign-in challenge URL) as a QR code texture.
// Builds the image straight from QRCoder's module matrix, one pixel per
// module, so no System.Drawing or PNG round trip is involved; the TextureRect
// showing it must use nearest-neighbour filtering to stay sharp when scaled.
public static class QrTexture
{
    public static Texture2D Create(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        try
        {
            using var generator = new QRCodeGenerator();
            // ECC level L keeps the code as small (and as easy to scan) as
            // possible; SteamKit2's own QR sample uses the same level.
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
            var matrix = data.ModuleMatrix; // includes the 4-module quiet zone
            var size = matrix.Count;

            var pixels = new byte[size * size];
            for (var y = 0; y < size; y++)
            {
                var row = matrix[y];
                for (var x = 0; x < size; x++)
                    pixels[y * size + x] = row[x] ? (byte)0 : (byte)255;
            }

            var image = Image.CreateFromData(size, size, false, Image.Format.L8, pixels);
            return ImageTexture.CreateFromImage(image);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Auth] QR render failed: {ex.Message}");
            return null;
        }
    }
}
