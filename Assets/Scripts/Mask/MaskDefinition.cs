using System;
using System.Collections.Generic;

[Serializable]
public class MaskDefinitions
{
    public List<MaskDefinition> definitions = new();
}

[Serializable]
public class MaskDefinition
{
    public string maskId;
    public string localizationKey;
    public string description;
    public string fieldDelimiter;
    public string kvSeparator;
    public string outputTemplate;
    public string sampleData;
}
