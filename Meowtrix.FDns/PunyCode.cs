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

        public static OperationStatus TryEncodeToAscii(ReadOnlySpan<char> chars, Span<byte> asciiBuffer, out int bytesWritten)
        {
            bytesWritten = 0;

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

                    if (bytesWritten >= asciiBuffer.Length)
                        return OperationStatus.DestinationTooSmall;
                    asciiBuffer[bytesWritten++] = (byte)c.Value;
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

                            if (bytesWritten >= asciiBuffer.Length)
                                return OperationStatus.DestinationTooSmall;
                            asciiBuffer[bytesWritten++] = GetDigitChar(t + (q - t) % (Base - t));

                            q = (q - t) / (Base - t);
                        }

                        if (bytesWritten >= asciiBuffer.Length)
                            return OperationStatus.DestinationTooSmall;
                        asciiBuffer[bytesWritten++] = GetDigitChar(q);
                    }

                    bias = AdaptBias(delta, h + 1, firstTime);
                    firstTime = false;
                    delta = 0;
                    h++;
                }

                delta++;
                n++;
            }

            return OperationStatus.Done;
        }

        public static OperationStatus TryEncodeToUtf16(ReadOnlySpan<char> chars, Span<char> utf16Buffer, out int bytesWritten)
        {
            Span<byte> asciiBuffer = stackalloc byte[utf16Buffer.Length];
            var result = TryEncodeToAscii(chars, asciiBuffer, out bytesWritten);

            for (int i = 0; i < asciiBuffer.Length; i++)
                utf16Buffer[i] = (char)asciiBuffer[i];

            return result;
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
    }
}
