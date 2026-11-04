using PinchZoom.Core;

namespace PinchZoom.Core.Tests;

public class PinchZoomConfigTests
{
    [Fact]
    public void Parse_ReadsKeyAndZoom()
    {
        var config = PinchZoomConfig.Parse("""{ "key": "Scroll", "zoom": 3 }""", out var problems);

        Assert.Empty(problems);
        Assert.Equal(new PinchZoomConfig("Scroll", 3.0), config);
    }

    [Fact]
    public void Parse_AllowsCommentsTrailingCommasAndAnyCase()
    {
        var config = PinchZoomConfig.Parse("""
            {
              // hold this key
              "Key": "F13",
              "ZOOM": 2,
            }
            """, out var problems);

        Assert.Empty(problems);
        Assert.Equal(new PinchZoomConfig("F13", 2.0), config);
    }

    [Fact]
    public void Parse_MissingSettingsUseDefaults()
    {
        var config = PinchZoomConfig.Parse("{}", out var problems);

        Assert.Empty(problems);
        Assert.Equal(PinchZoomConfig.Default, config);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("0.5")]
    [InlineData("5.01")]
    [InlineData("\"2\"")]
    public void Parse_OutOfRangeZoom_FallsBackAndReports(string zoom)
    {
        var config = PinchZoomConfig.Parse($$"""{ "key": "Scroll", "zoom": {{zoom}} }""", out var problems);

        Assert.Single(problems);
        Assert.Equal(new PinchZoomConfig("Scroll", ZoomStateMachine.DefaultTargetZoom), config);
    }

    [Fact]
    public void Parse_MaxZoomIsAllowed()
    {
        var config = PinchZoomConfig.Parse("""{ "zoom": 5 }""", out var problems);

        Assert.Empty(problems);
        Assert.Equal(ZoomStateMachine.MaxZoom, config.Zoom);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("42")]
    public void Parse_BadKey_FallsBackAndReports(string key)
    {
        var config = PinchZoomConfig.Parse($$"""{ "key": {{key}}, "zoom": 3 }""", out var problems);

        Assert.Single(problems);
        Assert.Equal(new PinchZoomConfig(PinchZoomConfig.DefaultKey, 3.0), config);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void Parse_InvalidDocument_ReturnsDefaults(string json)
    {
        var config = PinchZoomConfig.Parse(json, out var problems);

        Assert.Single(problems);
        Assert.Equal(PinchZoomConfig.Default, config);
    }

    [Fact]
    public void Parse_UnknownSetting_IsReported()
    {
        PinchZoomConfig.Parse("""{ "zom": 3 }""", out var problems);

        Assert.Contains("zom", Assert.Single(problems));
    }

    [Fact]
    public void ToJson_RoundTrips()
    {
        var original = new PinchZoomConfig("RControlKey", 3.5);

        var parsed = PinchZoomConfig.Parse(original.ToJson(), out var problems);

        Assert.Empty(problems);
        Assert.Equal(original, parsed);
    }
}
