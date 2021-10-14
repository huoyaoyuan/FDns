using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace Meowtrix.FDns
{
    public static class PunyCode
    {
        private const int Base = 36;
        private const int TMin = 1;
        private const int TMax = 26;
        private const int Skew = 38;
        private const int Damp = 700;
        private const int InitialBias = 72;
        private const int InitialN = 0x80;

        private static int AdaptBias(int delta, int numPoints, bool firstTime)
        {
            delta /= firstTime ? Damp : 2;
            delta += delta / numPoints;
            int k = 0;
            while (delta > (Base - TMin) * TMax / 2)
            {
                delta /= Base - TMin;
                k += Base;
            }
            return k + (Base - TMin + 1) * delta / (delta + Skew);
        }

        private static bool TryEncodeCore(ReadOnlySpan<char> chars, ref ValueBuffer<byte> asciiBuffer)
        {
            int n = InitialN;
            int delta = 0;
            int bias = InitialBias;

            int h = 0;
            int totalRunes = 0;
            foreach (Rune c in chars.EnumerateRunes())
            {
                totalRunes++;

                if (c.IsAscii)
                {
                    h++;

                    if (!asciiBuffer.TryAdd((byte)c.Value))
                        return false;
                }
            }

            bool firstTime = true;
            while (h < totalRunes)
            {
                int m = int.MaxValue;
                foreach (Rune c in chars.EnumerateRunes())
                {
                    if (c.Value >= n)
                        m = Math.Min(m, c.Value);
                }

                Debug.Assert(m < int.MaxValue);
                delta += (m - n) * (h + 1);
                n = m;

                foreach (Rune c in chars.EnumerateRunes())
                {
                    static byte GetDigitChar(int digit) => (byte)(digit <= 25 ? 'a' + digit : '0' + digit - 26);

                    if (c.Value < n)
                        delta++;

                    if (c.Value == n)
                    {
                        int q = delta;
                        for (int k = Base; true; k += Base)
                        {
                            int t = Math.Clamp(k - bias, TMin, TMax);
                            if (q < t)
                                break;

                            if (!asciiBuffer.TryAdd(GetDigitChar(t + (q - t) % (Base - t))))
                                return false;

                            q = (q - t) / (Base - t);
                        }

                        if (!asciiBuffer.TryAdd(GetDigitChar(q)))
                            return false;

                        bias = AdaptBias(delta, h + 1, firstTime);
                        firstTime = false;
                        delta = 0;
                        h++;
                    }
                }

                delta++;
                n++;
            }

            return true;
        }

        public static OperationStatus TryEncodeToAscii(ReadOnlySpan<char> chars, Span<byte> asciiBuffer, out int bytesWritten)
        {
            var buffer = new ValueBuffer<byte>(asciiBuffer, false);
            bool result = TryEncodeCore(chars, ref buffer);
            bytesWritten = buffer.BytesConsumed;
            return result ? OperationStatus.Done : OperationStatus.DestinationTooSmall;
        }

        public static OperationStatus TryEncodeToUtf16(ReadOnlySpan<char> chars, Span<char> utf16Buffer, out int bytesWritten)
        {
            Span<byte> asciiBuffer = stackalloc byte[utf16Buffer.Length];

            var buffer = new ValueBuffer<byte>(asciiBuffer, false);
            bool result = TryEncodeCore(chars, ref buffer);
            bytesWritten = buffer.BytesConsumed;

            Encoding.ASCII.GetChars(buffer.ConsumedSpan, utf16Buffer);
            return result ? OperationStatus.Done : OperationStatus.DestinationTooSmall;
        }

        public static int EncodeToAscii(ReadOnlySpan<char> chars, Span<byte> asciiBuffer)
        {
            if (TryEncodeToAscii(chars, asciiBuffer, out int bytesWritten) == OperationStatus.Done)
                return bytesWritten;
            throw new ArgumentException("Destination too small", nameof(asciiBuffer));
        }

        public static int EncodeToUtf16(ReadOnlySpan<char> chars, Span<char> utf16Buffer)
        {
            if (TryEncodeToUtf16(chars, utf16Buffer, out int bytesWritten) == OperationStatus.Done)
                return bytesWritten;
            throw new ArgumentException("Destination too small", nameof(utf16Buffer));
        }

        public static string EncodeToString(ReadOnlySpan<char> chars)
        {
            var buffer = new ValueBuffer<byte>(stackalloc byte[63], true);
            try
            {
                bool result = TryEncodeCore(chars, ref buffer);
                Debug.Assert(result);
                return Encoding.ASCII.GetString(buffer.ConsumedSpan);
            }
            finally
            {
                buffer.Dispose();
            }
        }
    }
}
