using System;
using System.Text;
using Antigen.Tree;
using System.Runtime.Intrinsics.Arm;
using System.Diagnostics;

namespace Antigen
{
    public static partial class Helpers
    {
        public static char[] Alphabets = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };

        public static byte GetRandomByte()
        {
            return (byte)PRNG.Next(byte.MinValue, byte.MaxValue);
        }

        public static short GetRandomShort()
        {
            return (short)PRNG.Next(short.MinValue, short.MaxValue);
        }

        public static int GetRandomInt()
        {
            return PRNG.Next(int.MinValue, int.MaxValue);
        }

        public static long GetRandomLong()
        {
            return PRNG.NextLong(long.MaxValue);
        }

        public static ushort GetRandomUShort()
        {
            return (ushort)PRNG.Next(ushort.MaxValue);
        }

        public static uint GetRandomUInt()
        {
            return (uint)PRNG.NextLong(uint.MaxValue);
        }

        public static ulong GetRandomULong()
        {
            return (ulong)PRNG.NextLong(long.MaxValue);
        }

        public static char GetRandomChar()
        {
            return Alphabets[PRNG.Next(Alphabets.Length)];
        }

        public static string GetRandomString(int length = -1)
        {
            length = (length == -1) ? PRNG.Next(10) : length;
            StringBuilder strBuilder = new StringBuilder();

            for (int c = 0; c < length; c++)
            {
                strBuilder.Append(Alphabets[PRNG.Next(Alphabets.Length)]);
            }
            return strBuilder.ToString();
        }

        public static string GetRandomEnumValue(Primitive primitive)
        {
            Array enumValues;
            switch (primitive)
            {
                case Primitive.SveMaskPattern:
                    enumValues = Enum.GetValues(typeof(SveMaskPattern));
                    return "SveMaskPattern." + enumValues.GetValue(PRNG.Next(enumValues.Length)).ToString();
                default:
                    Debug.Assert(false, string.Format("Hit unknown value type {0}", Enum.GetName(typeof(Tree.Primitive), primitive)));
                    return "0";

            }
        }

        public static bool GetRandomBoolean()
        {
            return PRNG.Decide(0.5) ? true : false;
        }

        public static sbyte GetRandomSByte()
        {
            return (sbyte)PRNG.Next(sbyte.MinValue, sbyte.MaxValue);
        }

        public static float GetRandomFloat()
        {
            return (float)PRNG.Next(10) + (1 / PRNG.Next(1, 5));
        }

        public static double GetRandomDouble()
        {
            return (double)PRNG.Next(10) + (1 / PRNG.Next(1, 5));
        }

        public static decimal GetRandomDecimal()
        {
            return (decimal)PRNG.Next(10) + (1 / PRNG.Next(1, 5));
        }
    }
}
