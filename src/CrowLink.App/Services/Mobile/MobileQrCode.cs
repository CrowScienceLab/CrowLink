using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CrowLink.Services.Mobile;

public static class MobileQrCode
{
    private const int Version = 5;
    private const int Size = 37;
    private const int DataCodewords = 108;
    private const int ErrorCorrectionCodewords = 26;

    public static BitmapSource CreateBitmap(string text, int scale = 4)
    {
        var modules = Encode(text);
        var safeScale = Math.Clamp(scale, 2, 10);
        const int border = 4;
        var imageSize = (Size + (border * 2)) * safeScale;
        var pixels = Enumerable.Repeat((byte)255, imageSize * imageSize).ToArray();
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!modules[x, y])
                {
                    continue;
                }

                var startX = (x + border) * safeScale;
                var startY = (y + border) * safeScale;
                for (var offsetY = 0; offsetY < safeScale; offsetY++)
                {
                    Array.Fill(pixels, (byte)0, ((startY + offsetY) * imageSize) + startX, safeScale);
                }
            }
        }

        var bitmap = BitmapSource.Create(
            imageSize,
            imageSize,
            96,
            96,
            PixelFormats.Gray8,
            null,
            pixels,
            imageSize);
        bitmap.Freeze();
        return bitmap;
    }

    public static bool[,] Encode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 106)
        {
            throw new ArgumentOutOfRangeException(nameof(text), "Mobile URL is too long for the built-in QR code.");
        }

        var data = CreateDataCodewords(bytes);
        var divisor = CreateReedSolomonDivisor(ErrorCorrectionCodewords);
        var correction = CreateReedSolomonRemainder(data, divisor);
        var allCodewords = new byte[data.Length + correction.Length];
        data.CopyTo(allCodewords, 0);
        correction.CopyTo(allCodewords, data.Length);

        var modules = new bool[Size, Size];
        var functions = new bool[Size, Size];
        DrawFunctionPatterns(modules, functions);
        DrawCodewords(modules, functions, allCodewords);
        DrawFormatBits(modules, functions, 0);
        return modules;
    }

    private static byte[] CreateDataCodewords(byte[] text)
    {
        var bits = new List<bool>(DataCodewords * 8);
        AppendBits(bits, 0b0100, 4);
        AppendBits(bits, text.Length, 8);
        foreach (var value in text)
        {
            AppendBits(bits, value, 8);
        }

        var capacity = DataCodewords * 8;
        AppendBits(bits, 0, Math.Min(4, capacity - bits.Count));
        while ((bits.Count & 7) != 0)
        {
            bits.Add(false);
        }

        var pad = 0xEC;
        while (bits.Count < capacity)
        {
            AppendBits(bits, pad, 8);
            pad ^= 0xEC ^ 0x11;
        }

        var result = new byte[DataCodewords];
        for (var index = 0; index < bits.Count; index++)
        {
            result[index >> 3] |= (byte)((bits[index] ? 1 : 0) << (7 - (index & 7)));
        }

        return result;
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] functions)
    {
        for (var index = 0; index < Size; index++)
        {
            SetFunction(modules, functions, 6, index, (index & 1) == 0);
            SetFunction(modules, functions, index, 6, (index & 1) == 0);
        }

        DrawFinder(modules, functions, 3, 3);
        DrawFinder(modules, functions, Size - 4, 3);
        DrawFinder(modules, functions, 3, Size - 4);
        DrawAlignment(modules, functions, 30, 30);

        for (var index = 0; index <= 5; index++)
        {
            SetFunction(modules, functions, 8, index, false);
        }

        SetFunction(modules, functions, 8, 7, false);
        SetFunction(modules, functions, 8, 8, false);
        SetFunction(modules, functions, 7, 8, false);
        for (var index = 9; index < 15; index++)
        {
            SetFunction(modules, functions, 14 - index, 8, false);
        }

        for (var index = 0; index < 8; index++)
        {
            SetFunction(modules, functions, Size - 1 - index, 8, false);
        }

        for (var index = 8; index < 15; index++)
        {
            SetFunction(modules, functions, 8, Size - 15 + index, false);
        }

        SetFunction(modules, functions, 8, Size - 8, true);
    }

    private static void DrawFinder(bool[,] modules, bool[,] functions, int centerX, int centerY)
    {
        for (var deltaY = -4; deltaY <= 4; deltaY++)
        {
            for (var deltaX = -4; deltaX <= 4; deltaX++)
            {
                var x = centerX + deltaX;
                var y = centerY + deltaY;
                if (x < 0 || y < 0 || x >= Size || y >= Size)
                {
                    continue;
                }

                var distance = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
                SetFunction(modules, functions, x, y, distance is not 2 and not 4);
            }
        }
    }

    private static void DrawAlignment(bool[,] modules, bool[,] functions, int centerX, int centerY)
    {
        for (var deltaY = -2; deltaY <= 2; deltaY++)
        {
            for (var deltaX = -2; deltaX <= 2; deltaX++)
            {
                SetFunction(
                    modules,
                    functions,
                    centerX + deltaX,
                    centerY + deltaY,
                    Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)) != 1);
            }
        }
    }

    private static void DrawCodewords(bool[,] modules, bool[,] functions, byte[] codewords)
    {
        var bitIndex = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            for (var vertical = 0; vertical < Size; vertical++)
            {
                var upward = ((right + 1) & 2) == 0;
                var y = upward ? Size - 1 - vertical : vertical;
                for (var column = 0; column < 2; column++)
                {
                    var x = right - column;
                    if (functions[x, y])
                    {
                        continue;
                    }

                    var value = bitIndex < codewords.Length * 8 &&
                                ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                    bitIndex++;
                    modules[x, y] = value ^ (((x + y) & 1) == 0);
                }
            }
        }
    }

    private static void DrawFormatBits(bool[,] modules, bool[,] functions, int mask)
    {
        var data = (1 << 3) | mask;
        var remainder = data;
        for (var index = 0; index < 10; index++)
        {
            remainder = (remainder << 1) ^ (((remainder >> 9) & 1) * 0x537);
        }

        var bits = ((data << 10) | remainder) ^ 0x5412;
        for (var index = 0; index <= 5; index++)
        {
            SetFunction(modules, functions, 8, index, GetBit(bits, index));
        }

        SetFunction(modules, functions, 8, 7, GetBit(bits, 6));
        SetFunction(modules, functions, 8, 8, GetBit(bits, 7));
        SetFunction(modules, functions, 7, 8, GetBit(bits, 8));
        for (var index = 9; index < 15; index++)
        {
            SetFunction(modules, functions, 14 - index, 8, GetBit(bits, index));
        }

        for (var index = 0; index < 8; index++)
        {
            SetFunction(modules, functions, Size - 1 - index, 8, GetBit(bits, index));
        }

        for (var index = 8; index < 15; index++)
        {
            SetFunction(modules, functions, 8, Size - 15 + index, GetBit(bits, index));
        }

        SetFunction(modules, functions, 8, Size - 8, true);
    }

    private static byte[] CreateReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        byte root = 1;
        for (var index = 0; index < degree; index++)
        {
            for (var coefficient = 0; coefficient < result.Length; coefficient++)
            {
                result[coefficient] = Multiply(result[coefficient], root);
                if (coefficient + 1 < result.Length)
                {
                    result[coefficient] ^= result[coefficient + 1];
                }
            }

            root = Multiply(root, 0x02);
        }

        return result;
    }

    private static byte[] CreateReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var value in data)
        {
            var factor = (byte)(value ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var index = 0; index < result.Length; index++)
            {
                result[index] ^= Multiply(divisor[index], factor);
            }
        }

        return result;
    }

    private static byte Multiply(byte left, int right)
    {
        var product = 0;
        var multiplicand = (int)left;
        var multiplier = right;
        for (var index = 0; index < 8; index++)
        {
            product ^= -(multiplier & 1) & multiplicand;
            multiplier >>= 1;
            multiplicand = (multiplicand << 1) ^ ((multiplicand >> 7) * 0x11D);
        }

        return (byte)product;
    }

    private static void AppendBits(List<bool> bits, int value, int length)
    {
        for (var index = length - 1; index >= 0; index--)
        {
            bits.Add(((value >> index) & 1) != 0);
        }
    }

    private static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;

    private static void SetFunction(bool[,] modules, bool[,] functions, int x, int y, bool value)
    {
        modules[x, y] = value;
        functions[x, y] = true;
    }
}
