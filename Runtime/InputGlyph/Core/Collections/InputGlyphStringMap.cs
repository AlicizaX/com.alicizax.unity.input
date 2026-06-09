#if INPUTSYSTEM_SUPPORT
using System;

internal sealed class InputGlyphStringMap<TValue>
{
    private const int InitialCapacity = 16;

    private string[] _keys;
    private int[] _hashes;
    private bool[] _occupied;
    private TValue[] _values;
    private int _mask;
    private int _count;

    public InputGlyphStringMap(int capacity = InitialCapacity)
    {
        int resolvedCapacity = NextPowerOfTwo(capacity < InitialCapacity ? InitialCapacity : capacity);
        _keys = new string[resolvedCapacity];
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

    public bool ContainsKey(string key)
    {
        return TryGetValue(key, out TValue _);
    }

    public bool TryGetValue(string key, out TValue value)
    {
        value = default;
        int hash = InputGlyphStringUtility.StableHash(key);
        if (hash == 0)
        {
            return false;
        }

        int slot = hash & _mask;
        int startSlot = slot;
        do
        {
            if (!_occupied[slot])
            {
                return false;
            }

            if (_hashes[slot] == hash && InputGlyphStringUtility.EqualsOrdinal(_keys[slot], key))
            {
                value = _values[slot];
                return true;
            }

            slot = (slot + 1) & _mask;
        }
        while (slot != startSlot);

        return false;
    }

    public void Set(string key, TValue value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if ((_count + 1) * 2 >= _keys.Length)
        {
            Resize(_keys.Length << 1);
        }

        SetInternal(key, InputGlyphStringUtility.StableHash(key), value);
    }

    public bool Remove(string key)
    {
        int hash = InputGlyphStringUtility.StableHash(key);
        if (hash == 0)
        {
            return false;
        }

        int slot = hash & _mask;
        int startSlot = slot;
        do
        {
            if (!_occupied[slot])
            {
                return false;
            }

            if (_hashes[slot] == hash && InputGlyphStringUtility.EqualsOrdinal(_keys[slot], key))
            {
                RemoveAt(slot);
                return true;
            }

            slot = (slot + 1) & _mask;
        }
        while (slot != startSlot);

        return false;
    }

    public string GetKeyAtSlot(int slot)
    {
        return slot >= 0 && slot < _keys.Length && _occupied[slot] ? _keys[slot] : null;
    }

    public TValue GetValueAtSlot(int slot)
    {
        return slot >= 0 && slot < _values.Length && _occupied[slot] ? _values[slot] : default;
    }

    public int SlotCapacity
    {
        get
        {
            return _keys.Length;
        }
    }

    private void RemoveAt(int slot)
    {
        _keys[slot] = null;
        _hashes[slot] = 0;
        _occupied[slot] = false;
        _values[slot] = default;
        _count--;

        int next = (slot + 1) & _mask;
        while (_occupied[next])
        {
            string keyToRehash = _keys[next];
            int hashToRehash = _hashes[next];
            TValue valueToRehash = _values[next];
            _keys[next] = null;
            _hashes[next] = 0;
            _occupied[next] = false;
            _values[next] = default;
            _count--;
            SetInternal(keyToRehash, hashToRehash, valueToRehash);
            next = (next + 1) & _mask;
        }
    }

    private void SetInternal(string key, int hash, TValue value)
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

            if (_hashes[slot] == hash && InputGlyphStringUtility.EqualsOrdinal(_keys[slot], key))
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
        string[] oldKeys = _keys;
        int[] oldHashes = _hashes;
        bool[] oldOccupied = _occupied;
        TValue[] oldValues = _values;

        _keys = new string[newCapacity];
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
