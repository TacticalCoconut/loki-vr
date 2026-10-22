using System.Text;

namespace Loki.IO
{
    /// <summary>
    /// The one member the client's GRF reader takes from its RoReader (client/Assets/Loki/Runtime/IO/RoReader.cs, which also has
    /// Unity vector types and so cannot be compiled here): GRF entry names are kept as Latin-1, byte for byte.
    /// </summary>
    internal static class RoReader
    {
        public static readonly Encoding Latin1 = Encoding.Latin1;
    }
}
