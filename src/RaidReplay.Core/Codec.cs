using System;
using System.IO;

namespace RaidReplay.Core
{
    internal static class Codec
    {
        public static void WriteVar(this BinaryWriter w, uint v)
        {
            while (v >= 0x80) { w.Write((byte)(v | 0x80)); v >>= 7; }
            w.Write((byte)v);
        }
        public static uint ReadVar(this BinaryReader r)
        {
            uint result = 0; int shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift > 35) throw new InvalidDataException("varint too long");
            }
        }
        public static void WriteZig(this BinaryWriter w, int v) => w.WriteVar((uint)((v << 1) ^ (v >> 31)));
        public static int ReadZig(this BinaryReader r) { uint u = r.ReadVar(); return (int)(u >> 1) ^ -(int)(u & 1); }

        public static int PosToQ(float metres) => (int)Math.Round(metres * 100.0);
        public static float QToPos(int q) => q / 100f;

        public static ushort AngleToQ(float deg)
        {
            double d = deg % 360.0; if (d < 0) d += 360.0;
            return (ushort)((int)Math.Round(d / 360.0 * 65536.0) & 0xFFFF);
        }
        public static float QToAngle(ushort q) => q * 360f / 65536f;
        public static int AngleDelta(ushort now, ushort prev) => (short)(now - prev);

        public static byte SpeedToQ(float ms) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(ms * 20f)));
    }
}
