using System.Collections.Generic;

namespace VARLive.ApexNetwork.Proxy {
    internal static class ProxyTable<T> {
        private static Dictionary<T, T> table = new Dictionary<T, T>();

        internal static void Add( T key, T value ) {
            table.Add( key, value );
        }

        internal static bool TryGetValue( T key, out T value ) {
            return table.TryGetValue( key, out value );
        }

        internal static void CleanUp() {
            table.Clear();
        }
    }
}
