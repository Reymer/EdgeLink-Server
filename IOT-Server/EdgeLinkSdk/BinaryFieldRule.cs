using System.Runtime.Serialization;

namespace EdgeLink
{
    [DataContract]
    public class BinaryFieldRule
    {
        [DataMember] public string name     { get; set; }
        [DataMember] public int    offset   { get; set; }
        [DataMember] public int    length   { get; set; }
        [DataMember] public string dataType { get; set; } = "hex";
    }
}
