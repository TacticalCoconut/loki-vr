using System.Text;

namespace Loki.VrMaps
{
    /// <summary>RO file and object names are EUC-KR bytes kept as Latin-1 by the client's reader (IO/RoReader.cs:10-15); for people the
    /// report shows them decoded (code page 949, in .NET's own code-pages provider), the raw form being what the GRF holds.</summary>
    public static class Names
    {
        private static Encoding _kr;
        public static string Display(string latin1)
        {
            if (string.IsNullOrEmpty(latin1)) return latin1;
            bool high = false; foreach (char c in latin1) if (c >= 0x80) { high = true; break; }
            if (!high) return latin1;
            try
            {
                if (_kr == null) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _kr = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
                return _kr.GetString(Loki.IO.RoReader.Latin1.GetBytes(latin1));
            }
            catch (System.Exception) { return latin1; }
        }
    }
}
