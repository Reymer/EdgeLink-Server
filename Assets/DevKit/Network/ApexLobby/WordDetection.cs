public static class WordDetection
{
    private static string[] illegalWord = new string[] { "幹" , "/" , "!" , ":" , "晨" , "@" , "." ,
        "ㄅ" , "ㄆ" , "ㄇ" , "ㄈ", "ㄉ" , "ㄊ" , "ㄋ" , "ㄌ" , "ㄍ" , "ㄎ" , "ㄏ" , "ㄐ" , "ㄑ" , "ㄒ" , "ㄓ" , "ㄔ",
    "ㄕ" , "ㄖ" , "ㄗ" , "ㄘ" , "ㄙ"  , "一" , "ㄨ" , "ㄩ" , "ㄚ" , "ㄛ" , "ㄜ" ,"ㄝ" , "ㄞ" ,"ㄟ" , "ㄠ" , "ㄡ" , "ㄢ" ,
    "ㄣ" , "ㄤ" , "ㄥ" , "\\" , "*" , "\"" , "<" , ">" , "|" , "ˇ" ,"ˋ" , "^"};
    private static int maxRoomNameLength = 20;
    public static string[] GetillegalWord()
    {
        return illegalWord;
    }
    public static int GetMaxRoomNameLength()
    {
        return maxRoomNameLength;
    }
}
