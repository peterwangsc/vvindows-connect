using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace VindOS;

sealed class Input
{
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int Dx, Dy; public uint Data, Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyInput { public ushort Vk, Scan; public uint Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] struct RawInput { public uint Type; public Union U; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, RawInput[] inputs, int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

    const uint MouseMove = 0x0001, LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010, MiddleDown = 0x0020, MiddleUp = 0x0040,
        Wheel = 0x0800, HWheel = 0x1000, Absolute = 0x8000, VirtualDesk = 0x4000, KeyExtended = 0x0001, KeyUp = 0x0002, ScanCode = 0x0008, Unicode = 0x0004;

    static readonly ushort[] Scan = BuildScanTable();

    readonly int _left, _top, _width, _height;
    readonly HashSet<ushort> _keys = [];
    readonly HashSet<int> _buttons = [];
    readonly int[] _counts = new int[6];
    int _keyFailures;
    uint _lastKeyError;

    public Input(int left, int top, int width, int height) => (_left, _top, _width, _height) = (left, top, width, height);

    public string Summary => $"records move={_counts[1]} button={_counts[2]} wheel={_counts[3]} key={_counts[4]} text={_counts[5]} keyInjectFailures={_keyFailures} lastKeyError={_lastKeyError}";

    public void Apply(ReadOnlySpan<byte> record)
    {
        if (record.Length != 16 || BinaryPrimitives.ReadUInt16LittleEndian(record[2..]) != 0) throw new InvalidDataException("Malformed input record.");
        int kind = record[0], flags = record[1], a = BinaryPrimitives.ReadInt32LittleEndian(record[4..]), b = BinaryPrimitives.ReadInt32LittleEndian(record[8..]), c = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
        if (kind is >= 1 and <= 5) _counts[kind]++;
        switch (kind)
        {
            case 1: Coordinates(a, b); Send(Mouse(a, b, 0)); break;
            case 2:
                Coordinates(a, b);
                if (c is < 1 or > 3) throw new InvalidDataException("Bad button.");
                var down = (flags & 1) != 0;
                if (down) _buttons.Add(c); else _buttons.Remove(c);
                Send(Mouse(a, b, c switch { 1 => down ? LeftDown : LeftUp, 2 => down ? RightDown : RightUp, _ => down ? MiddleDown : MiddleUp }));
                break;
            case 3:
                if (a != 0) Send(Plain(Wheel, (uint)a));
                if (b != 0) Send(Plain(HWheel, (uint)b));
                break;
            case 4:
                if (a is < 0 or > 0xFF || Scan[a] == 0) throw new InvalidDataException("Unmapped key.");
                if ((flags & 1) != 0) _keys.Add(Scan[a]); else _keys.Remove(Scan[a]);
                if (Send(Key(Scan[a], (flags & 1) != 0)) == 0) { _keyFailures++; _lastKeyError = (uint)Marshal.GetLastWin32Error(); }
                break;
            case 5:
                if (a is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF)) throw new InvalidDataException("Bad scalar.");
                foreach (var unit in char.ConvertFromUtf32(a)) { Send(Text(unit, true)); Send(Text(unit, false)); }
                break;
            default: throw new InvalidDataException("Unknown input kind.");
        }
    }

    public void ReleaseAll()
    {
        foreach (var b in _buttons) Send(Plain(b switch { 1 => LeftUp, 2 => RightUp, _ => MiddleUp }));
        foreach (var k in _keys) Send(Key(k, false));
        _buttons.Clear();
        _keys.Clear();
    }

    static void Coordinates(int x, int y)
    {
        if (x is < 0 or > 65535 || y is < 0 or > 65535) throw new InvalidDataException("Bad coordinates.");
    }

    RawInput Mouse(int x, int y, uint flags, uint data = 0)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        int px = _left + (int)((long)x * (_width - 1) / 65535), py = _top + (int)((long)y * (_height - 1) / 65535);
        return new RawInput { Type = 0, U = { Mouse = new MouseInput { Dx = (int)(((long)(px - vx) * 65535 + vw / 2) / Math.Max(1, vw - 1)), Dy = (int)(((long)(py - vy) * 65535 + vh / 2) / Math.Max(1, vh - 1)), Data = data, Flags = flags | MouseMove | Absolute | VirtualDesk } } };
    }

    static RawInput Plain(uint flags, uint data = 0) => new() { Type = 0, U = { Mouse = new MouseInput { Data = data, Flags = flags } } };

    static RawInput Key(ushort scan, bool down) =>
        new() { Type = 1, U = { Key = new KeyInput { Scan = (ushort)(scan & 0xFF), Flags = ScanCode | (down ? 0 : KeyUp) | ((scan & 0xE000) != 0 ? KeyExtended : 0) } } };

    static RawInput Text(char unit, bool down) => new() { Type = 1, U = { Key = new KeyInput { Scan = unit, Flags = Unicode | (down ? 0 : KeyUp) } } };

    static uint Send(RawInput input) => SendInput(1, [input], Marshal.SizeOf<RawInput>());

    static ushort[] BuildScanTable()
    {
        var t = new ushort[256];
        ReadOnlySpan<ushort> letters = [0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32, 0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C];
        for (var i = 0; i < 26; i++) t[0x04 + i] = letters[i];
        for (var i = 0; i < 9; i++) t[0x1E + i] = (ushort)(0x02 + i);
        t[0x27] = 0x0B;
        ReadOnlySpan<ushort> row = [0x1C, 0x01, 0x0E, 0x0F, 0x39, 0x0C, 0x0D, 0x1A, 0x1B, 0x2B, 0x2B, 0x27, 0x28, 0x29, 0x33, 0x34, 0x35, 0x3A];
        for (var i = 0; i < row.Length; i++) t[0x28 + i] = row[i];
        for (var i = 0; i < 10; i++) t[0x3A + i] = (ushort)(0x3B + i);
        t[0x44] = 0x57; t[0x45] = 0x58; t[0x46] = 0xE037; t[0x47] = 0x46; t[0x48] = 0x45;
        ReadOnlySpan<ushort> nav = [0xE052, 0xE047, 0xE049, 0xE053, 0xE04F, 0xE051, 0xE04D, 0xE04B, 0xE050, 0xE048, 0x45, 0xE035, 0x37, 0x4A, 0x4E, 0xE01C, 0x4F, 0x50, 0x51, 0x4B, 0x4C, 0x4D, 0x47, 0x48, 0x49, 0x52, 0x53, 0x56, 0xE05D, 0xE05E, 0x59];
        for (var i = 0; i < nav.Length; i++) t[0x49 + i] = nav[i];
        for (var i = 0; i < 12; i++) t[0x68 + i] = (ushort)(0x64 + i);
        ReadOnlySpan<ushort> mods = [0x1D, 0x2A, 0x38, 0xE05B, 0xE01D, 0x36, 0xE038, 0xE05C];
        for (var i = 0; i < 8; i++) t[0xE0 + i] = mods[i];
        return t;
    }

    public static ushort ScanFor(int hidUsage) => Scan[hidUsage & 0xFF];
}
