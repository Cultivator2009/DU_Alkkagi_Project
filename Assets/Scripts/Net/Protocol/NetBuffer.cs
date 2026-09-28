using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// The bytes of one message. Integers are variable length (small numbers,
// one byte), and every record in a list goes in with its length. A reader
// that runs out of a message or a record reads zeros, and one that stops
// early skips the rest: a field added at the end of a message or a record
// reads as its default on a build that sends none, and a build that
// doesn't know it passes over it. So a message grows without the other
// build misreading it; change or remove a field and ProtocolVersion must
// go up.
public sealed class NetWriter
{
    private byte[] buffer = new byte[128];

    public int Length { get; private set; }

    public void Byte(byte value)
    {
        Ensure(1);
        buffer[Length++] = value;
    }

    public void Bool(bool value) => Byte(value ? (byte)1 : (byte)0);

    public void UInt(uint value)
    {
        while (value >= 0x80)
        {
            Byte((byte)(value | 0x80));
            value >>= 7;
        }
        Byte((byte)value);
    }

    // Zigzag, so -1 is one byte too.
    public void Int(int value) => UInt((uint)((value << 1) ^ (value >> 31)));

    public void ULong(ulong value)
    {
        Ensure(8);
        for (var i = 0; i < 8; i++) buffer[Length++] = (byte)(value >> (8 * i));
    }

    public void Float(float value)
    {
        var bits = BitConverter.SingleToInt32Bits(value);
        Ensure(4);
        for (var i = 0; i < 4; i++) buffer[Length++] = (byte)(bits >> (8 * i));
    }

    public void Vector3(Vector3 value)
    {
        Float(value.x);
        Float(value.y);
        Float(value.z);
    }

    public void Quaternion(Quaternion value)
    {
        Float(value.x);
        Float(value.y);
        Float(value.z);
        Float(value.w);
    }

    // A piece id: one of BoardSetup.PieceId's letters.
    public void Piece(char id) => Byte((byte)id);

    public void String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        UInt((uint)bytes.Length);
        Ensure(bytes.Length);
        Array.Copy(bytes, 0, buffer, Length, bytes.Length);
        Length += bytes.Length;
    }

    // One record, its length first. Usually under 128 bytes, so one byte is
    // kept for the length and the rest made room for when it's longer.
    public void Record(Action<NetWriter> write)
    {
        var at = Length;
        Byte(0);
        write(this);
        var length = Length - at - 1;
        if (length < 0x80)
        {
            buffer[at] = (byte)length;
            return;
        }
        var prefix = 0;
        for (var v = (uint)length; v > 0; v >>= 7) prefix++;
        Ensure(prefix - 1);
        Array.Copy(buffer, at + 1, buffer, at + prefix, length);
        Length = at;
        UInt((uint)length);
        Length = at + prefix + length;
    }

    public void List<T>(IReadOnlyList<T> items, Action<NetWriter, T> write)
    {
        UInt((uint)items.Count);
        foreach (var item in items) Record(w => write(w, item));
    }

    public byte[] ToArray()
    {
        var bytes = new byte[Length];
        Array.Copy(buffer, bytes, Length);
        return bytes;
    }

    private void Ensure(int more)
    {
        if (Length + more <= buffer.Length) return;
        Array.Resize(ref buffer, Mathf.Max(buffer.Length * 2, Length + more));
    }
}

public sealed class NetReader
{
    private const int MaxListCount = 4096; // a corrupt count can't make a reader allocate without end

    private readonly byte[] data;
    private int position;
    private int end;

    public NetReader(byte[] data, int start = 0)
    {
        this.data = data;
        position = start;
        end = data.Length;
    }

    public bool AtEnd => position >= end;

    public byte Byte() => position < end ? data[position++] : (byte)0;

    public bool Bool() => Byte() != 0;

    public uint UInt()
    {
        uint value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = Byte();
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
        }
        return value;
    }

    public int Int()
    {
        var z = UInt();
        return (int)(z >> 1) ^ -(int)(z & 1);
    }

    public ulong ULong()
    {
        ulong value = 0;
        for (var i = 0; i < 8; i++) value |= (ulong)Byte() << (8 * i);
        return value;
    }

    public float Float()
    {
        var bits = 0;
        for (var i = 0; i < 4; i++) bits |= Byte() << (8 * i);
        return BitConverter.Int32BitsToSingle(bits);
    }

    public Vector3 Vector3() => new Vector3(Float(), Float(), Float());

    // An absent rotation reads as none, not as the zero quaternion.
    public Quaternion Quaternion()
    {
        if (AtEnd) return UnityEngine.Quaternion.identity;
        return new Quaternion(Float(), Float(), Float(), Float());
    }

    public char Piece() => (char)Byte();

    public string String()
    {
        var length = (int)Math.Max(0, Math.Min(UInt(), (long)(end - position)));
        var value = Encoding.UTF8.GetString(data, position, length);
        position += length;
        return value;
    }

    // Reads one record with read, inside its length: whatever read leaves is
    // skipped, and what it asks for past the end reads as zero.
    public T Record<T>(Func<NetReader, T> read)
    {
        var length = (int)UInt();
        var outer = end;
        var recordEnd = Math.Min(outer, position + length);
        end = recordEnd;
        var value = read(this);
        position = recordEnd;
        end = outer;
        return value;
    }

    public List<T> List<T>(Func<NetReader, T> read)
    {
        var count = (int)Math.Min(UInt(), MaxListCount);
        var items = new List<T>(count);
        for (var i = 0; i < count && !AtEnd; i++) items.Add(Record(read));
        return items;
    }
}
