using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace VARLive.ApexNetwork.Proxy {
#if NET_2_0 || NET_2_0_SUBSET
    public delegate void Action<T1, T2, T3, T4, T5>( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5 );
    public delegate void Action<T1, T2, T3, T4, T5, T6>( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6 );
    public delegate void Action<T1, T2, T3, T4, T5, T6, T7>( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7 );
    public delegate void Action<T1, T2, T3, T4, T5, T6, T7, T8>( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8 );
#endif
    public class ApexProxy : MonoBehaviour {
        private ReaderWriterLockSlim lockSlim = new ReaderWriterLockSlim();

        private static ApexProxy instance;
        public static ApexProxy Instance {
            get {
                if ( instance == null ) {
                    instance = new GameObject( "ApexProxy" ).AddComponent<ApexProxy>();
                }

                return instance;
            }
        }

        private Queue<Action> actionQueue = new Queue<Action>();

        internal Action SyncCallback( Action listener ) {
            return () => EnqueueAction( () => listener.Invoke() );
        }

        internal Action<object> SyncCallback( Action<object> listener ) {
            return ( object obj ) => EnqueueAction( () => listener.Invoke( obj ) );
        }

        internal Action<User> SyncCallback( Action<User> listener ) {
            return ( User user ) => EnqueueAction( () => listener.Invoke( user ) );
        }

        internal Action<string, object> SyncCallback( Action<string, object> listener ) {
            return ( string eventName, object obj ) => EnqueueAction( () => listener.Invoke( eventName, obj ) );
        }

        internal Action<string, object[]> SyncCallback( Action<string, object[]> listener ) {
            return ( string eventName, object[] args ) => EnqueueAction( () => listener.Invoke( eventName, args ) );
        }

        internal Action<T1> SyncCallback<T1>( Action<T1> listener ) {
            return ( T1 arg1 ) => EnqueueAction( () => listener.Invoke( arg1 ) );
        }

        internal Action<T1, T2> SyncCallback<T1, T2>( Action<T1, T2> listener ) {
            return ( T1 arg1, T2 arg2 ) => EnqueueAction( () => listener.Invoke( arg1, arg2 ) );
        }

        internal Action<T1, T2, T3> SyncCallback<T1, T2, T3>( Action<T1, T2, T3> listener ) {
            return ( T1 arg1, T2 arg2, T3 arg3 ) => EnqueueAction( () => listener.Invoke( arg1, arg2, arg3 ) );
        }

        internal Action<T1, T2, T3, T4> SyncCallback<T1, T2, T3, T4>( Action<T1, T2, T3, T4> listener ) {
            return ( T1 arg1, T2 arg2, T3 arg3, T4 arg4 ) => EnqueueAction( () => listener.Invoke( arg1, arg2, arg3, arg4 ) );
        }

        internal Action<T1, T2, T3, T4, T5> SyncCallback<T1, T2, T3, T4, T5>( Action<T1, T2, T3, T4, T5> listener ) {
            return ( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5 ) => EnqueueAction( () => listener.Invoke( arg1, arg2, arg3, arg4, arg5 ) );
        }

        internal Action<T1, T2, T3, T4, T5, T6> SyncCallback<T1, T2, T3, T4, T5, T6>( Action<T1, T2, T3, T4, T5, T6> listener ) {
            return ( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6 ) => EnqueueAction( () => listener.Invoke( arg1, arg2, arg3, arg4, arg5, arg6 ) );
        }

        internal Action<T1, T2, T3, T4, T5, T6, T7> SyncCallback<T1, T2, T3, T4, T5, T6, T7>( Action<T1, T2, T3, T4, T5, T6, T7> listener ) {
            return ( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7 ) => EnqueueAction( () => listener.Invoke( arg1, arg2, arg3, arg4, arg5, arg6, arg7 ) );
        }

        internal Action<T1, T2, T3, T4, T5, T6, T7, T8> SyncCallback<T1, T2, T3, T4, T5, T6, T7, T8>( Action<T1, T2, T3, T4, T5, T6, T7, T8> listener ) {
            return ( T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8 ) => EnqueueAction( () => listener.Invoke( arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8 ) );
        }

        private void EnqueueAction( Action action ) {
            lockSlim.EnterWriteLock();
            try {
                actionQueue.Enqueue( action );
            }
            finally {
                lockSlim.ExitWriteLock();
            }
        }

        private void Update() {
            while ( true ) {
                Action action = null;

                lockSlim.EnterReadLock();
                try {
                    if ( actionQueue.Count > 0 ) {
                        action = actionQueue.Dequeue();
                    }
                }
                finally {
                    lockSlim.ExitReadLock();
                }

                if ( action != null ) {
                    action.Invoke();
                }
                else {
                    break;
                }
            }
        }

        private void OnDestroy() {
            lockSlim.Dispose();
        }
    }
}
