using System.Net;
using Acordanza.Worker.Services;
using Shouldly;
using Xunit;

namespace Acordanza.Worker.Tests.Services;

// Plan 31 §3's blocklist, asserted at every range boundary rather than reasoned about.
// This is the file that decides whether "fetch this URL for me" can reach the instance
// metadata endpoint from inside the worker's trust boundary, so it is tested at both ends
// of every range: an off-by-one here is the whole vulnerability.
public class PrivateAddressesTests
{
    [Theory]
    // Both ends of every blocked IPv4 range.
    [InlineData("0.0.0.0")]
    [InlineData("0.255.255.255")]
    [InlineData("10.0.0.0")]
    [InlineData("10.255.255.255")]
    [InlineData("100.64.0.0")]
    [InlineData("100.127.255.255")]
    [InlineData("127.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    [InlineData("169.254.0.0")]
    // The one that matters most: Azure/AWS/GCP instance metadata.
    [InlineData("169.254.169.254")]
    [InlineData("169.254.255.255")]
    [InlineData("172.16.0.0")]
    [InlineData("172.31.255.255")]
    [InlineData("192.0.0.0")]
    [InlineData("192.0.0.255")]
    [InlineData("192.168.0.0")]
    [InlineData("192.168.255.255")]
    [InlineData("198.18.0.0")]
    [InlineData("198.19.255.255")]
    [InlineData("224.0.0.0")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.0")]
    [InlineData("255.255.255.255")]
    public void Blocked_v4_ranges_are_not_public(string address) =>
        PrivateAddresses.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    // One step outside each range, which is where an off-by-one would show.
    [InlineData("1.0.0.0")]
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("126.255.255.255")]
    [InlineData("128.0.0.0")]
    [InlineData("169.253.255.255")]
    [InlineData("169.255.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("192.0.1.0")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.0")]
    [InlineData("198.17.255.255")]
    [InlineData("198.20.0.0")]
    [InlineData("223.255.255.255")]
    // Ordinary public addresses.
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    public void Ordinary_v4_addresses_are_public(string address) =>
        PrivateAddresses.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();

    [Theory]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fc00::")]
    [InlineData("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("fe80::1")]
    [InlineData("febf:ffff::1")]
    [InlineData("ff02::1")]
    public void Blocked_v6_ranges_are_not_public(string address) =>
        PrivateAddresses.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void Ordinary_v6_addresses_are_public(string address) =>
        PrivateAddresses.IsPublic(IPAddress.Parse(address)).ShouldBeTrue();

    // The mapped form is the one that gets missed — every v4 rule would skip
    // ::ffff:169.254.169.254 if it were only checked as v6. Its own test rather than a row
    // above, because this is the specific mistake §3 calls out.
    [Theory]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    public void IPv4_mapped_addresses_are_unmapped_before_checking(string address) =>
        PrivateAddresses.IsPublic(IPAddress.Parse(address)).ShouldBeFalse();

    [Fact]
    public void A_mapped_public_address_stays_public() =>
        PrivateAddresses.IsPublic(IPAddress.Parse("::ffff:8.8.8.8")).ShouldBeTrue();
}
