using System;
using VARLive.ApexNetwork.Proxy;

namespace VARLive.ApexNetwork {
    public static class ChannelExtension {
        public static void RegisterOnSubscribeEvent( this Channel channel, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action>.Add( listener, proxyMethod );
            }

            channel.onSubscribed += proxyMethod;
        }

        public static void UnregisterOnSubscribeEvent( this Channel channel, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) ) {
                channel.onSubscribed -= proxyMethod;
            }
        }

        public static void RegisterOnUnsubscribeEvent( this Channel channel, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action>.Add( listener, proxyMethod );
            }

            channel.onUnsubscribed += proxyMethod;
        }

        public static void UnregisterOnUnsubscribeEvent( this Channel channel, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) ) {
                channel.onUnsubscribed -= proxyMethod;
            }
        }

        public static void RegisterOnAddMemeberEvent( this PresenceChannel presenceChannel, Action<User> listener ) {
            Action<User> proxyMethod;
            if ( ProxyTable<Action<User>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<User>>.Add( listener, proxyMethod );
            }

            presenceChannel.onAddMember += proxyMethod;
        }

        public static void RegisterOnRemoveMemeberEvent( this Channel channel, Action<User> listener ) {
            Action<User> proxyMethod;
            if ( ProxyTable<Action<User>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<User>>.Add( listener, proxyMethod );
            }

            channel.onRemoveMember += proxyMethod;
        }

        public static void BindInMainThread( this Channel channel, string eventName, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread( this Channel channel, string eventName, Action listener ) {
            Action proxyMethod;
            if ( ProxyTable<Action>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        [Obsolete( "Use BindInMainThread<T1, T2, ...>( string, listener )" )]
        public static void BindInMainThread( this Channel channel, string eventName, Action<object> listener ) {
            Action<object> proxyMethod;
            if ( ProxyTable<Action<object>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<object>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        [Obsolete( "Use UnbindInMainThread<T1, T2, ...>( string, listener )" )]
        public static void UnbindInMainThread( this Channel channel, string eventName, Action<object> listener ) {
            Action<object> proxyMethod;
            if ( ProxyTable<Action<object>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1>( this Channel channel, string eventName, Action<T1> listener ) {
            Action<T1> proxyMethod;
            if ( ProxyTable<Action<T1>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1>( this Channel channel, string eventName, Action<T1> listener ) {
            Action<T1> proxyMethod;
            if ( ProxyTable<Action<T1>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1, T2>( this Channel channel, string eventName, Action<T1, T2> listener ) {
            Action<T1, T2> proxyMethod;
            if ( ProxyTable<Action<T1, T2>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1, T2>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1, T2>( this Channel channel, string eventName, Action<T1, T2> listener ) {
            Action<T1, T2> proxyMethod;
            if ( ProxyTable<Action<T1, T2>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1, T2, T3>( this Channel channel, string eventName, Action<T1, T2, T3> listener ) {
            Action<T1, T2, T3> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1, T2, T3>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1, T2, T3>( this Channel channel, string eventName, Action<T1, T2, T3> listener ) {
            Action<T1, T2, T3> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1, T2, T3, T4>( this Channel channel, string eventName, Action<T1, T2, T3, T4> listener ) {
            Action<T1, T2, T3, T4> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1, T2, T3, T4>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1, T2, T3, T4>( this Channel channel, string eventName, Action<T1, T2, T3, T4> listener ) {
            Action<T1, T2, T3, T4> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1, T2, T3, T4, T5>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5> listener ) {
            Action<T1, T2, T3, T4, T5> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1, T2, T3, T4, T5>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1, T2, T3, T4, T5>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5> listener ) {
            Action<T1, T2, T3, T4, T5> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1, T2, T3, T4, T5, T6>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5, T6> listener ) {
            Action<T1, T2, T3, T4, T5, T6> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5, T6>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1, T2, T3, T4, T5, T6>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1, T2, T3, T4, T5, T6>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5, T6> listener ) {
            Action<T1, T2, T3, T4, T5, T6> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5, T6>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1, T2, T3, T4, T5, T6, T7>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5, T6, T7> listener ) {
            Action<T1, T2, T3, T4, T5, T6, T7> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5, T6, T7>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1, T2, T3, T4, T5, T6, T7>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1, T2, T3, T4, T5, T6, T7>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5, T6, T7> listener ) {
            Action<T1, T2, T3, T4, T5, T6, T7> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5, T6, T7>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindInMainThread<T1, T2, T3, T4, T5, T6, T7, T8>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8> listener ) {
            Action<T1, T2, T3, T4, T5, T6, T7, T8> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5, T6, T7, T8>>.TryGetValue( listener, out proxyMethod ) == false ) {
                proxyMethod = ApexProxy.Instance.SyncCallback( listener );
                ProxyTable<Action<T1, T2, T3, T4, T5, T6, T7, T8>>.Add( listener, proxyMethod );
            }

            channel.Bind( eventName, proxyMethod );
        }

        public static void UnbindInMainThread<T1, T2, T3, T4, T5, T6, T7, T8>( this Channel channel, string eventName, Action<T1, T2, T3, T4, T5, T6, T7, T8> listener ) {
            Action<T1, T2, T3, T4, T5, T6, T7, T8> proxyMethod;
            if ( ProxyTable<Action<T1, T2, T3, T4, T5, T6, T7, T8>>.TryGetValue( listener, out proxyMethod ) ) {
                channel.Unbind( eventName, proxyMethod );
            }
        }

        public static void BindAllInMainThread( this Channel channel, Action<string, object[]> listener ) {
            channel.BindAll( ApexProxy.Instance.SyncCallback( listener ) );
        }
    }
}
