using System;
using System.Text;

namespace Loki.Net
{
    /// <summary>
    /// Text on the wire. rAthena never transcodes: chat, whispers and NPC input are relayed byte for byte
    /// (rA:conf/inter_athena.conf:24-26 leaves default_codepage off; rA:common/sql.cpp:167-172 only runs SET NAMES;
    /// rA:map/clif.cpp:10442-10578), and the Latin American official client writes windows-1252
    /// (rB:Utils/CodepageManager.js:108-218 maps langtype 12, Brazil, to windows-1252 — roBrowserLegacy, a reimplementation).
    /// So Loki writes and reads cp1252:
    ///   - 0xA0-0xFF and 0x00-0x7F are Latin-1 (ç ã é = 0xE7 0xE3 0xE9 in both);
    ///   - 0x80-0x9F use the 32-entry table below (€ curly quotes … Ÿ; the five bytes cp1252 leaves undefined map to
    ///     U+0081 U+008D U+008F U+0090 U+009D, as Windows' own MultiByteToWideChar does — checked on this PC with .NET's
    ///     Encoding.GetEncoding(1252)), so <see cref="Decode"/> is a bijection over all 256 byte values.
    /// <see cref="Encode"/> is its inverse and also maps every U+0000-U+00FF character to the same byte (Latin-1). Names are
    /// read with <see cref="Latin1"/> (PacketReader.Str) and written back through <see cref="Encode"/>, so a character name
    /// goes out exactly as the server sent it — rAthena checks the chat prefix "Name : " byte for byte and kicks on any
    /// difference (rA:map/clif.cpp:10499-10508). Encoding.GetEncoding(1252) is not used: it needs the I18N assemblies in a
    /// Unity player and does not round-trip 0x81 0x8D 0x8F 0x90 0x9D (re-audit §2i).
    /// </summary>
    public static class RoText
    {
        /// <summary>cp1252 0x80-0x9F → Unicode (Unicode CP1252.TXT; the undefined five are the identity).</summary>
        private static readonly char[] High = {
            '€', '\u0081', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\u008D', 'Ž', '\u008F',
            '\u0090', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\u009D', 'ž', 'Ÿ',
        };

        /// <summary>A character the server's code page can't hold (Korean, emoji…) goes out as this, like Encoding's default fallback.</summary>
        public const byte Unmappable = (byte)'?';

        /// <summary>The byte for one character, or -1 when cp1252 has none.</summary>
        public static int ByteOf(char c)
        {
            if (c <= 'ÿ') return c; // Latin-1, including U+0080-U+009F (names read with Latin1 keep their bytes)
            for (int i = 0; i < High.Length; i++) if (High[i] == c) return 0x80 + i;
            return -1;
        }

        /// <summary>True when every character of the text survives the trip to the server.</summary>
        public static bool CanEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            foreach (char c in s) if (ByteOf(c) < 0) return false;
            return true;
        }

        /// <summary>Text → bytes: one byte per character (unmappable ones become '?').</summary>
        public static byte[] Encode(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) { int v = ByteOf(s[i]); b[i] = v < 0 ? Unmappable : (byte)v; }
            return b;
        }

        /// <summary>Bytes → text, cp1252 (chat, whispers, NPC dialog, broadcasts, mail, notices).</summary>
        public static string Decode(byte[] b, int offset, int count)
        {
            if (count <= 0) return "";
            var c = new char[count];
            for (int i = 0; i < count; i++) { byte v = b[offset + i]; c[i] = v >= 0x80 && v <= 0x9F ? High[v - 0x80] : (char)v; }
            return new string(c);
        }
        public static string Decode(byte[] b) => b == null ? "" : Decode(b, 0, b.Length);

        /// <summary>A chat line "Name : text" (ZC_NOTIFY_CHAT 0x008d, ZC_NOTIFY_PLAYERCHAT 0x008e, party 0x0109, guild 0x017f): the
        /// name before the first " : " as Latin-1, like every other name the client holds (so comparisons with it are exact),
        /// the rest as cp1252. No separator: the whole line is text.</summary>
        public static string DecodeChatLine(byte[] b, int offset, int count)
        {
            for (int i = 0; i + 2 < count; i++)
                if (b[offset + i] == 0x20 && b[offset + i + 1] == 0x3A && b[offset + i + 2] == 0x20)
                    return i == 0 ? Decode(b, offset, count) : Latin1(b, offset, i) + Decode(b, offset + i, count - i);
            return Decode(b, offset, count);
        }

        /// <summary>Bytes → text, Latin-1 (names, map and file names: every byte kept as the character of the same value).</summary>
        public static string Latin1(byte[] b, int offset, int count)
        {
            if (count <= 0) return "";
            var c = new char[count];
            for (int i = 0; i < count; i++) c[i] = (char)b[offset + i];
            return new string(c);
        }

        /// <summary>Checks the two promises above over all 256 byte values: Encode(Decode(b)) == b and Encode(Latin1(b)) == b.</summary>
        public static bool SelfTest(out string why)
        {
            var all = new byte[256]; for (int i = 0; i < 256; i++) all[i] = (byte)i;
            var text = Decode(all); var back = Encode(text);
            for (int i = 0; i < 256; i++) if (back[i] != i) { why = $"cp1252 byte 0x{i:x2} → U+{(int)text[i]:X4} → 0x{back[i]:x2}"; return false; }
            var name = Latin1(all, 0, 256); back = Encode(name);
            for (int i = 0; i < 256; i++) if (back[i] != i) { why = $"Latin-1 byte 0x{i:x2} came back as 0x{back[i]:x2}"; return false; }
            for (int i = 0; i < 256; i++) for (int j = i + 1; j < 256; j++) if (text[i] == text[j]) { why = $"bytes 0x{i:x2} and 0x{j:x2} decode alike"; return false; }
            why = null; return true;
        }

        /// <summary>Hex of the bytes a text becomes (logs and smokes).</summary>
        public static string Hex(string s)
        {
            var b = Encode(s); var sb = new StringBuilder(b.Length * 3);
            foreach (var v in b) sb.Append(v.ToString("x2")).Append(' ');
            return sb.ToString().TrimEnd();
        }
    }
}
