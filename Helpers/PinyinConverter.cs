using TinyPinyin;

namespace NewCosmos.Helpers;

public interface IPinyinConverter
{
    string ToPinyin(string chineseText);
}

public class PinyinConverter : IPinyinConverter
{
    public string ToPinyin(string chineseText)
    {
        if (string.IsNullOrWhiteSpace(chineseText))
        {
            return string.Empty;
        }

        try
        {
            var pinyinList = new List<string>();
            foreach (var c in chineseText)
            {
                if (PinyinHelper.IsChinese(c))
                {
                    var pinyin = PinyinHelper.GetPinyin(c);
                    if (!string.IsNullOrEmpty(pinyin))
                    {
                        pinyinList.Add(pinyin.ToUpper());
                    }
                }
                else
                {
                    pinyinList.Add(c.ToString());
                }
            }
            return string.Join(" ", pinyinList);
        }
        catch
        {
            return chineseText;
        }
    }
}
