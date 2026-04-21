namespace EdgeLink
{
    public class IotMessage
    {
        /// <summary>IoT Server 輸出的原始字串（遮罩處理後）。</summary>
        public string Raw { get; }

        /// <summary>解析結果。建立接收器時若未傳入 MaskDefinition，此屬性為 null。</summary>
        public ParseResult Parsed { get; }

        internal IotMessage(string raw, ParseResult parsed)
        {
            Raw    = raw;
            Parsed = parsed;
        }
    }
}
