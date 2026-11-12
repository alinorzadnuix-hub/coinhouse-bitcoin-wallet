using UnityEngine;
using ZXing;

public class Color32LuminanceSource : LuminanceSource
{
    private readonly byte[] luminances;

    public Color32LuminanceSource(Color32[] data, int width, int height) : base(width, height)
    {
        luminances = new byte[width * height];
        for (int i = 0; i < data.Length; i++)
        {
            var c = data[i];
            luminances[i] = (byte)(0.299 * c.r + 0.587 * c.g + 0.114 * c.b);
        }
    }

    public override byte[] Matrix => luminances;

    public override byte[] getRow(int y, byte[] row)
    {
        if (row == null || row.Length < Width)
            row = new byte[Width];
        
        System.Array.Copy(luminances, y * Width, row, 0, Width);
        return row;
    }
}
