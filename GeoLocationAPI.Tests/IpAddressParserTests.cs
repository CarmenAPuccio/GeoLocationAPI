using GeoLocationAPI.V1.Helpers;

namespace GeoLocationAPI.Tests;

public class IpAddressParserTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("::1")]
    [InlineData("::ffff:1.2.3.4")]
    public void Accepts_ValidAddresses(string value)
    {
        Assert.True(IpAddressParser.TryParse(value, out var address));
        Assert.NotNull(address);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("127.1")]
    [InlineData("1.2.3")]
    [InlineData("256.1.1.1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("fe80::1%eth0")]
    [InlineData("8.8.8.8/32")]
    [InlineData("8.8.8.8:80")]
    public void Rejects_InvalidAddresses(string? value)
    {
        Assert.False(IpAddressParser.TryParse(value, out var address));
        Assert.Null(address);
    }
}
