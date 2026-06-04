#if INPUTSYSTEM_SUPPORT
using System;

public sealed class InputGlyphGuidMap<TValue>
{
    private const int InitialCapacity = 16;

    private Guid[] _keys;
    private int[] _hashes;
    private bool[] _occupied;
    private TValue[] _values;
    private int _mask;
    private int _count;

    public InputGlyphGuidMap(int capacity = InitialCapacity)
    {
        int resolvedCapacity = NextPowerOfTwo(capacity < InitialCapacity ? InitialCapacity : capacity);
        _keys = new Guid[resolvedCapacity];
        _hashes = new int[resolvedCapacity];
        _occupied = new bool[resolvedCapacity];
        _values = new TValue[resolvedCapacity];
        _mask = resolvedCapacity - 1;
    }

    public int Count
    {
        get
        {
            return _count;
        }
    }

    public void Clear()
    {
        Array.Clear(_keys, 0, _keys.Length);
        Array.Clear(_hashes, 0, _hashes.Length);
        Array.Clear(_occupied, 0, _occupied.Length);
        Array.Clear(_values, 0, _values.Length);
        _count = 0;
    }

    public bool TryGetValue(Guid key, out TValue value)
    {
        value = default;
        int hash = key.GetHashCode();
        int slot = hash & _mask;
        int startSlot = slot;
        do
        {
            if (!_occupied[slot])
            {
                return false;
            }

            if (_hashes[slot] == hash && _keys[slot].Equals(key))
            {
                value = _values[slot];
                return true;
            }

            slot = (slot + 1) & _mask;
        }
        while (slot != startSlot);

        return false;
    }

    public void Set(Guid key, TValue value)
    {
        if ((_count + 1) * 2 >= _keys.Length)
        {
            Resize(_keys.Length << 1);
        }

        SetInternal(key, key.GetHashCode(), value);
    }

    private void SetInternal(Guid key, int hash, TValue value)
    {
        int slot = hash & _mask;
        int startSlot = slot;
        do
        {
            if (!_occupied[slot])
            {
                _keys[slot] = key;
                _hashes[slot] = hash;
                _occupied[slot] = true;
                _values[slot] = value;
                _count++;
                return;
            }

            if (_hashes[slot] == hash && _keys[slot].Equals(key))
            {
                _values[slot] = value;
                return;
            }

            slot = (slot + 1) & _mask;
        }
        while (slot != startSlot);
    }

    private void Resize(int newCapacity)
    {
        Guid[] oldKeys = _keys;
        int[] oldHashes = _hashes;
        bool[] oldOccupied = _occupied;
        TValue[] oldValues = _values;

        _keys = new Guid[newCapacity];
        _hashes = new int[newCapacity];
        _occupied = new bool[newCapacity];
        _values = new TValue[newCapacity];
        _mask = newCapacity - 1;
        _count = 0;

        for (int i = 0; i < oldKeys.Length; i++)
        {
            if (oldOccupied[i])
            {
                SetInternal(oldKeys[i], oldHashes[i], oldValues[i]);
            }
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
#endif
