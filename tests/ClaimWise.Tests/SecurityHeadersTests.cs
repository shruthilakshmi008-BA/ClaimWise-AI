using Xunit;

namespace ClaimWise.Tests;

public class SecurityHeadersTests(ClaimWiseFactory factory) : IClassFixture<ClaimWiseFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}
