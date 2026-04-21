using System.Collections.Generic;

namespace EdgeLink
{
    public class ParseResult
    {
        public Dictionary<string, string> Fields { get; }
        public string Output { get; }
        public bool Success { get; }
        public string Error { get; }

        internal ParseResult(Dictionary<string, string> fields, string output)
        {
            Fields  = fields;
            Output  = output;
            Success = true;
            Error   = string.Empty;
        }

        internal ParseResult(string error)
        {
            Fields  = new Dictionary<string, string>();
            Output  = string.Empty;
            Success = false;
            Error   = error;
        }
    }
}
