using System;
using System.Buffers;
using System.Diagnostics;

namespace Meowtrix.FDns
{
    internal ref struct ValueBuffer<T>
    {
        private Span<T> _buffer;
        private bool _canRent;
        public T[]? _arrayToReturn;

        public ValueBuffer(Span<T> initialBuffer, bool canRent)
        {
            if (initialBuffer == default && !canRent)
            {
                throw new ArgumentException($"The buffer must be rentable if no initial buffer provided.");
            }

            _buffer = initialBuffer;
            _canRent = canRent;
            _arrayToReturn = null;
            BytesConsumed = 0;
        }

        public int BytesConsumed { get; private set; }

        public Span<T> ConsumedSpan => _buffer[..BytesConsumed];

        public bool HasMoreSpace => _canRent || BytesConsumed < _buffer.Length;

        private void Enlarge()
        {
            Debug.Assert(_canRent);

            T[] newArray = ArrayPool<T>.Shared.Rent(_buffer.Length * 2);
            _buffer.CopyTo(newArray);

            if (_arrayToReturn != null)
                ArrayPool<T>.Shared.Return(_arrayToReturn);

            _arrayToReturn = newArray;
            _buffer = newArray;
        }

        public bool TryAdd(T value) => TryInsert(value, BytesConsumed);

        public bool TryInsert(T value, int index)
        {
            if ((uint)index > (uint)BytesConsumed)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (_buffer == default && !_canRent)
                throw new ObjectDisposedException(nameof(ValueBuffer<T>));

            if (BytesConsumed >= _buffer.Length)
            {
                if (!_canRent)
                    return false;

                Enlarge();
            }

            for (int i = BytesConsumed - 1; i >= index; i--)
                _buffer[i + 1] = _buffer[i];

            _buffer[index] = value;
            BytesConsumed++;
            return true;
        }

        public void Dispose()
        {
            if (_arrayToReturn != null)
                ArrayPool<T>.Shared.Return(_arrayToReturn);

            _buffer = default;
            _canRent = false;
            _arrayToReturn = null;
        }
    }
}
