using System;
using VARLive.ApexNetwork.Proxy;

namespace VARLive.ApexNetwork {
    public static class ApexExtension {
        // Connect Event
        public static void RegisterOnConnectEvent( this Apex apex, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action>.Add( listener, proxyMethod );
            }

            apex.onConnect += proxyMethod;
        }

        public static void UnregisterOnConnectEvent( this Apex apex, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) ) {
                apex.onConnect -= proxyMethod;
            }
        }

        // Disconnect Event
        public static void RegisterOnDisconnectEvent( this Apex apex, Action<Exception> listener ) {
            Action<Exception> proxyMethod;
            if ( ProxyTable<Action<Exception>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<Exception>>.Add( listener, proxyMethod );
            }

            apex.onDisconnect += proxyMethod;
        }

        public static void UnregisterOnDisconnectEvent( this Apex apex, Action<Exception> listener ) {
            Action<Exception> proxyMethod;
            if ( ProxyTable<Action<Exception>>.TryGetValue( listener, out proxyMethod ) ) {
                apex.onDisconnect -= proxyMethod;
            }
        }

        public static void RegisterOnAppDataChangeEvent( this Apex apex, Action<int, int> listener ) {
            Action<int, int> proxyMethod;
            if ( ProxyTable<Action<int, int>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<int, int>>.Add( listener, proxyMethod );
            }

            apex.onAppDataChange += proxyMethod;
        }

        public static void UnregisterOnAppDataChangeEvent( this Apex apex, Action<int, int> listener ) {
            Action<int, int> proxyMethod;
            if ( ProxyTable<Action<int, int>>.TryGetValue( listener, out proxyMethod ) ) {
                apex.onAppDataChange -= proxyMethod;
            }
        }
    }
}
