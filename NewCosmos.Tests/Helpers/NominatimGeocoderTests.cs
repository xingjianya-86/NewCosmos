using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>村屯在线地理编码（2026-11-15 水印二改）：URL 组装与 Nominatim 响应解析的纯逻辑测试。</summary>
public class NominatimGeocoderTests
{
    [Fact]
    public void BuildSearchUrl_县镇村空格拼接_且转义()
    {
        var url = NominatimGeocoder.BuildSearchUrl("林口县", "柳树镇", "复兴村");

        Assert.StartsWith("https://nominatim.openstreetmap.org/search?", url);
        Assert.Contains("format=jsonv2", url);
        Assert.Contains("limit=1", url);
        Assert.Contains("countrycodes=cn", url);

        var decoded = Uri.UnescapeDataString(url);
        Assert.Contains("q=林口县 柳树镇 复兴村", decoded);
    }

    [Fact]
    public void BuildSearchUrl_空级跳过_缺镇也能查()
    {
        var decoded = Uri.UnescapeDataString(NominatimGeocoder.BuildSearchUrl("牡丹江市林口县", "", "柞木村"));
        Assert.Contains("q=牡丹江市林口县 柞木村", decoded);
        Assert.DoesNotContain("q=牡丹江市林口县  柞木村", decoded); // 空级不产生连续空格
    }

    [Fact]
    public void ParseSearchResponse_标准响应_返回经纬()
    {
        const string json = """
            [{"place_id":1,"lat":"45.2135467","lon":"129.6543210","display_name":"黑龙江省牡丹江市林口县"}]
            """;

        var geo = NominatimGeocoder.ParseSearchResponse(json);

        Assert.NotNull(geo);
        Assert.Equal(129.6543210, geo!.Value.Longitude, 6);
        Assert.Equal(45.2135467, geo.Value.Latitude, 6);
    }

    [Fact]
    public void ParseSearchResponse_空结果与坏输入_返回空不抛()
    {
        Assert.Null(NominatimGeocoder.ParseSearchResponse(null));
        Assert.Null(NominatimGeocoder.ParseSearchResponse(""));
        Assert.Null(NominatimGeocoder.ParseSearchResponse("[]"));
        Assert.Null(NominatimGeocoder.ParseSearchResponse("{}"));
        Assert.Null(NominatimGeocoder.ParseSearchResponse("not-json"));
        Assert.Null(NominatimGeocoder.ParseSearchResponse("""[{"lat":"abc","lon":"1.2"}]"""));
    }
}
