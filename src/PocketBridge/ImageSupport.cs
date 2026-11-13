using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PocketBridge;

public static class ImageSupport
{
    public static int Orientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata) return 1;
        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try { if (metadata.GetQuery(query) is ushort value) return value; }
            catch (Exception ex) when (ex is NotSupportedException or System.Runtime.InteropServices.COMException) { }
        }
        return 1;
    }

    public static BitmapSource Orient(BitmapSource source, int orientation)
    {
        var matrix = orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, 0, 0),
            3 => new Matrix(-1, 0, 0, -1, 0, 0),
            4 => new Matrix(1, 0, 0, -1, 0, 0),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, 0, 0),
            7 => new Matrix(0, -1, -1, 0, 0, 0),
            8 => new Matrix(0, -1, 1, 0, 0, 0),
            _ => Matrix.Identity
        };
        if (matrix.IsIdentity) return source;
        var result = new TransformedBitmap(source, new MatrixTransform(matrix));
        result.Freeze();
        return result;
    }
}
