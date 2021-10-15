using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Meowtrix.FDns
{
    public static class PunyCode
    {
        // https://datatracker.ietf.org/doc/html/rfc3492

        private const int Base = 36;
        private const int TMin = 1;
        private const int TMax = 26;
        private const int Skew = 38;
        private const int Damp = 700;
        private const int InitialBias = 72;
        private const int InitialN = 0x80;
        private const byte Delimiter = (byte)'-';

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

        private static OperationStatus TryEncodeCore(ReadOnlySpan<char> chars, ref ValueBuffer<byte> asciiBuffer)
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
                        return OperationStatus.DestinationTooSmall;
                }
            }

            if (asciiBuffer.BytesConsumed > 0)
            {
                if (!asciiBuffer.TryAdd(Delimiter))
                    return OperationStatus.DestinationTooSmall;
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
                try
                {
                    checked
                    {
                        delta += (m - n) * (h + 1);
                    }
                }
                catch (OverflowException)
                {
                    return OperationStatus.InvalidData;
                }
                n = m;

                foreach (Rune c in chars.EnumerateRunes())
                {
                    static byte GetDigitChar(int digit, bool upperCase) => (byte)(digit <= 25 ? (upperCase ? 'A' : 'a') + digit : '0' + digit - 26);

                    if (c.Value < n)
                    {
                        if (delta == int.MaxValue)
                            return OperationStatus.InvalidData;
                        delta++;
                    }

                    if (c.Value == n)
                    {
                        int q = delta;
                        for (int k = Base; true; k += Base)
                        {
                            int t = Math.Clamp(k - bias, TMin, TMax);
                            if (q < t)
                                break;

                            if (!asciiBuffer.TryAdd(GetDigitChar(t + (q - t) % (Base - t), Rune.IsUpper(c))))
                                return OperationStatus.DestinationTooSmall;

                            q = (q - t) / (Base - t);
                        }

                        if (!asciiBuffer.TryAdd(GetDigitChar(q, Rune.IsUpper(c))))
                            return OperationStatus.DestinationTooSmall;

                        bias = AdaptBias(delta, h + 1, firstTime);
                        firstTime = false;
                        delta = 0;
                        h++;
                    }
                }

                delta++;
                n++;
            }

            return OperationStatus.Done;
        }

        public static OperationStatus TryEncodeToAscii(ReadOnlySpan<char> chars, Span<byte> asciiBuffer, out int bytesWritten)
        {
            var buffer = new ValueBuffer<byte>(asciiBuffer, false);
            var result = TryEncodeCore(chars, ref buffer);
            bytesWritten = buffer.BytesConsumed;
            return result;
        }

        public static OperationStatus TryEncodeToUtf16(ReadOnlySpan<char> chars, Span<char> utf16Buffer, out int bytesWritten)
        {
            Span<byte> asciiBuffer = stackalloc byte[utf16Buffer.Length];

            var buffer = new ValueBuffer<byte>(asciiBuffer, false);
            var result = TryEncodeCore(chars, ref buffer);
            bytesWritten = buffer.BytesConsumed;

            Encoding.ASCII.GetChars(buffer.ConsumedSpan, utf16Buffer);
            return result;
        }

        public static int EncodeToAscii(ReadOnlySpan<char> chars, Span<byte> asciiBuffer)
        {
            return TryEncodeToAscii(chars, asciiBuffer, out int bytesWritten) switch
            {
                OperationStatus.Done => bytesWritten,
                OperationStatus.InvalidData => throw new OverflowException("The input can't be represented by PunyCode"),
                OperationStatus.DestinationTooSmall => throw new ArgumentException("Destination too small", nameof(asciiBuffer)),
                _ => throw new InvalidOperationException("unreachable")
            };
        }

        public static int EncodeToUtf16(ReadOnlySpan<char> chars, Span<char> utf16Buffer)
        {
            return TryEncodeToUtf16(chars, utf16Buffer, out int bytesWritten) switch
            {
                OperationStatus.Done => bytesWritten,
                OperationStatus.InvalidData => throw new OverflowException("The input can't be represented by PunyCode"),
                OperationStatus.DestinationTooSmall => throw new ArgumentException("Destination too small", nameof(utf16Buffer)),
                _ => throw new InvalidOperationException("unreachable")
            };
        }

        public static string EncodeToString(ReadOnlySpan<char> chars)
        {
            var buffer = new ValueBuffer<byte>(stackalloc byte[63], true);
            try
            {
                var result = TryEncodeCore(chars, ref buffer);
                if (result != OperationStatus.Done)
                {
                    Debug.Assert(result == OperationStatus.InvalidData);
                    throw new OverflowException("The input can't be represented by PunyCode");
                }
                return Encoding.ASCII.GetString(buffer.ConsumedSpan);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private static OperationStatus TryDecodeCore(ReadOnlySpan<byte> ascii, ref ValueBuffer<Rune> runeBuffer)
        {
            int n = InitialN;
            int i = 0;
            int bias = InitialBias;

            for (int j = ascii.Length - 1; j >= 0; j--)
            {
                if (ascii[j] == Delimiter)
                {
                    foreach (byte b in ascii[..j])
                        if (!runeBuffer.TryAdd(new Rune(b)))
                            return OperationStatus.DestinationTooSmall;

                    ascii = ascii[(j + 1)..];
                    break;
                }
            }

            while (!ascii.IsEmpty)
            {
                int oldi = i;
                int w = 1;
                for (int k = Base; true; k += Base)
                {
                    if (ascii.IsEmpty)
                        return OperationStatus.NeedMoreData;

                    char c = (char)ascii[0];
                    ascii = ascii[1..];
                    int digit = c switch
                    {
                        >= 'a' and <= 'z' => c - 'a',
                        >= 'A' and <= 'Z' => c - 'A',
                        >= '0' and <= '9' => c - '0' + 26,
                        _ => -1
                    };
                    if (digit == -1)
                        return OperationStatus.InvalidData;

                    try
                    {
                        checked
                        {
                            i += digit * w;
                        }
                    }
                    catch (OverflowException)
                    {
                        return OperationStatus.InvalidData;
                    }

                    int t = Math.Clamp(k - bias, TMin, TMax);
                    if (digit < t)
                        break;

                    try
                    {
                        checked
                        {
                            w *= Base - t;
                        }
                    }
                    catch (OverflowException)
                    {
                        return OperationStatus.InvalidData;
                    }
                }

                bias = AdaptBias(i - oldi, runeBuffer.BytesConsumed + 1, oldi == 0);

                try
                {
                    checked
                    {
                        n += i / (runeBuffer.BytesConsumed + 1);
                    }
                }
                catch (OverflowException)
                {
                    return OperationStatus.InvalidData;
                }

                i %= runeBuffer.BytesConsumed + 1;

                var rune = new Rune(n);
                if (rune.IsAscii)
                    return OperationStatus.InvalidData;
                if (!runeBuffer.TryInsert(rune, i))
                    return OperationStatus.DestinationTooSmall;

                i++;
            }

            return OperationStatus.Done;
        }

        public static OperationStatus TryDecodeFromAscii(ReadOnlySpan<byte> ascii, Span<char> utf16Buffer, out int charsWritten)
        {
            var buffer = new ValueBuffer<Rune>(stackalloc Rune[utf16Buffer.Length], false);
            var result = TryDecodeCore(ascii, ref buffer);
            var utf32 = MemoryMarshal.AsBytes(buffer.ConsumedSpan);

            if (Encoding.UTF32.GetCharCount(utf32) > utf16Buffer.Length)
            {
                charsWritten = 0;
                return OperationStatus.DestinationTooSmall;
            }

            charsWritten = Encoding.UTF32.GetChars(utf32, utf16Buffer);
            return result;
        }

        public static OperationStatus TryDecodeFromUtf16(ReadOnlySpan<char> utf16, Span<char> utf16Buffer, out int charsWritten)
        {
            Span<byte> ascii = stackalloc byte[utf16.Length];
            int asciiByttes = Encoding.ASCII.GetBytes(utf16, ascii);
            Debug.Assert(asciiByttes == ascii.Length);
            return TryDecodeFromAscii(ascii, utf16Buffer, out charsWritten);
        }

        public static int DecodeFromAscii(ReadOnlySpan<byte> ascii, Span<char> utf16Buffer)
        {
            return TryDecodeFromAscii(ascii, utf16Buffer, out int charsWritten) switch
            {
                OperationStatus.Done => charsWritten,
                OperationStatus.InvalidData => throw new ArgumentException("The input is not valid PunyCode", nameof(ascii)),
                OperationStatus.NeedMoreData => throw new ArgumentException("The input is not complete PunyCode", nameof(ascii)),
                OperationStatus.DestinationTooSmall => throw new ArgumentException("Destination too small", nameof(utf16Buffer)),
                _ => throw new InvalidOperationException("unreachable")
            };
        }

        public static int DecodeFromUtf16(ReadOnlySpan<char> utf16, Span<char> utf16Buffer)
        {
            return TryDecodeFromUtf16(utf16, utf16Buffer, out int charsWritten) switch
            {
                OperationStatus.Done => charsWritten,
                OperationStatus.InvalidData => throw new ArgumentException("The input is not valid PunyCode", nameof(utf16)),
                OperationStatus.NeedMoreData => throw new ArgumentException("The input is not complete PunyCode", nameof(utf16)),
                OperationStatus.DestinationTooSmall => throw new ArgumentException("Destination too small", nameof(utf16Buffer)),
                _ => throw new InvalidOperationException("unreachable")
            };
        }

        public static string DecodeToString(ReadOnlySpan<char> utf16)
        {
            Span<byte> ascii = stackalloc byte[utf16.Length];
            int asciiByttes = Encoding.ASCII.GetBytes(utf16, ascii);
            Debug.Assert(asciiByttes == ascii.Length);

            var buffer = new ValueBuffer<Rune>(stackalloc Rune[utf16.Length], true);
            try
            {

                var result = TryDecodeCore(ascii, ref buffer);
                if (result != OperationStatus.Done)
                {
                    Debug.Assert(result == OperationStatus.InvalidData);
                    throw new OverflowException("The input can't be represented by PunyCode");
                }
                return Encoding.UTF32.GetString(MemoryMarshal.AsBytes(buffer.ConsumedSpan));
            }
            finally
            {
                buffer.Dispose();
            }
        }
    }
}
