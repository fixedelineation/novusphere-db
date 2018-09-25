using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Globalization;

namespace Novusphere.EOS
{
    public static class EOSBinaryReader
    {
        public static BinaryReader FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber);
            return new BinaryReader(new MemoryStream(bytes));
        }

        //https://github.com/EOSIO/eos/blob/master/contracts/eosiolib/types.hpp#L125
        private static string NameToString(ulong value)
        {
            string charmap = ".12345abcdefghijklmnopqrstuvwxyz";
            char[] str = new char[13];

            long tmp = (long)value;
            for (uint i = 0; i <= 12; ++i)
            {
                char c = charmap[(int)(tmp & (i == 0 ? 0x0f : 0x1f))];
                str[12 - i] = c;
                tmp >>= (i == 0 ? 4 : 5);
            }

            return new string(str).TrimEnd('.');
        }

        public static string ReadEOSAccountName(this BinaryReader br)
        {
            return NameToString(br.ReadUInt64());
        }

        public static string ReadEOSString(this BinaryReader br)
        {
            var len = (int)br.ReadVarInt();
            var bytes = br.ReadBytes(len);
            if (bytes.Length != len)
                throw new InvalidOperationException("failed to read EOS string");

            var str = Encoding.UTF8.GetString(bytes);
            return str;
        }

        public static long ReadVarInt(this BinaryReader br)
        {
            const byte MSB = 0x80;
            const byte REST = 0x7F;

            long res = 0;
            int shift = 0;
            
            for (int i = 0; i < 8; i++)
            {
                byte b = br.ReadByte();
                res += (shift < 28) ? (b & REST) << shift : (b & REST) * (int)Math.Pow(2, shift);
                shift += 7;
                if (b < MSB)
                    break;
            }

            return res;
        }
    }
}
