namespace iotserver {
    public static class LanguageKeys {
        public static readonly string Name = "Name";
        public static readonly string NetProtocol = "NetProtocol";
        public static readonly string RemotePort = "RemotePort";
        public static readonly string LocalPort = "LocalPort";
        public static readonly string ForwardTarget = "ForwardTarget";
        public static readonly string NetState = "NetState";
        public static readonly string Mask = "Mask";
        public static readonly string Exit = "Exit";
        public static readonly string RemoteIP = "RemoteIP";
        public static readonly string Save = "Save";
        public static readonly string Cancel = "Cancel";
        public static readonly string EnterName = "EnterName";
        public static readonly string EnterIp = "EnterIp";
        public static readonly string EnterRemotePort = "EnterRemotePort";
        public static readonly string EnterLocalPort = "EnterLocalPort";
        public static readonly string Connecting = "Connecting";
        public static readonly string NotConnecting = "NotConnecting";
        public static readonly string OriginalData = "OriginalData";

        public static readonly string Log_Connected        = "Log_Connected";
        public static readonly string Log_Disconnected     = "Log_Disconnected";
        public static readonly string Log_Removed          = "Log_Removed";
        public static readonly string Log_NotFound         = "Log_NotFound";
        public static readonly string Log_HeartbeatLost    = "Log_HeartbeatLost";
        public static readonly string Log_HeartbeatFailed  = "Log_HeartbeatFailed";
        public static readonly string Log_MaxRetry         = "Log_MaxRetry";
        public static readonly string Log_Restarting       = "Log_Restarting";
        public static readonly string Log_InvalidPort      = "Log_InvalidPort";
        public static readonly string Log_PortOccupied     = "Log_PortOccupied";
        public static readonly string Log_StartFailed      = "Log_StartFailed";
        public static readonly string Log_RestartFailed    = "Log_RestartFailed";
        public static readonly string Log_MaxConnections   = "Log_MaxConnections";
        public static readonly string Log_AlreadyConnected = "Log_AlreadyConnected";
        public static readonly string Log_ReconnectSuccess = "Log_ReconnectSuccess";
        public static readonly string Log_ReconnectFailed  = "Log_ReconnectFailed";

        public static readonly string Log_DataLoaded              = "Log_DataLoaded";
        public static readonly string Log_UnknownProtocol         = "Log_UnknownProtocol";
        public static readonly string Log_MaskNotFound            = "Log_MaskNotFound";
        public static readonly string Log_ForwardFailed           = "Log_ForwardFailed";
        public static readonly string Log_PortEmpty               = "Log_PortEmpty";
        public static readonly string Log_InvalidPortFormat       = "Log_InvalidPortFormat";
        public static readonly string Log_PortOutOfRange          = "Log_PortOutOfRange";
        public static readonly string Log_InvalidIP               = "Log_InvalidIP";
        public static readonly string Log_InvalidUTF8             = "Log_InvalidUTF8";
        public static readonly string Log_PacketDropped           = "Log_PacketDropped";
        public static readonly string Log_BufferOverflow          = "Log_BufferOverflow";
        public static readonly string Log_UnexpectedError         = "Log_UnexpectedError";
        public static readonly string Log_ReceiveMessagesUnexpectedError = "Log_ReceiveMessagesUnexpectedError";
        public static readonly string Log_DisconnectFailed        = "Log_DisconnectFailed";

        public static readonly string Log_AllFieldsEmpty           = "Log_AllFieldsEmpty";
        public static readonly string Log_NameRequired             = "Log_NameRequired";
        public static readonly string Log_UdpNeedsRemotePort       = "Log_UdpNeedsRemotePort";
        public static readonly string Log_TcpClientNeedsRemotePort = "Log_TcpClientNeedsRemotePort";
        public static readonly string Log_TcpClientNeedsIP         = "Log_TcpClientNeedsIP";
        public static readonly string Log_TcpServerNeedsLocalPort  = "Log_TcpServerNeedsLocalPort";
        public static readonly string Log_UnknownProtocolType      = "Log_UnknownProtocolType";

        public static readonly string Log_PacketReceived = "Log_PacketReceived";
        public static readonly string Log_PacketSent     = "Log_PacketSent";

        public static readonly string Log_DuplicatePort         = "Log_DuplicatePort";
        public static readonly string Log_NotifyTarget          = "Log_NotifyTarget";
        public static readonly string Log_NotifyFailed          = "Log_NotifyFailed";
        public static readonly string Log_AcceptClientsError    = "Log_AcceptClientsError";
        public static readonly string Log_ProcessPacketsError   = "Log_ProcessPacketsError";
        public static readonly string Log_ReceiveClientError    = "Log_ReceiveClientError";

        public static readonly string UI_SourceNone = "UI_SourceNone";
        public static readonly string UI_Source = "UI_Source";
    }
}
