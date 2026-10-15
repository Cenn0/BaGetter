using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BaGetter.Core.Tests.Authentication;

public class ApiKeyAuthenticationServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything")]
    public async Task RejectsPublishingIfNoKeyConfigured(string suppliedKey)
    {
        var service = Create(new BaGetterOptions());
        Assert.False(await service.AuthenticateAsync(suppliedKey, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task RejectsMissingOrIncorrectKeyWhenLegacyKeyConfigured(string suppliedKey)
    {
        var service = Create(new BaGetterOptions { ApiKey = "private-key" });
        Assert.False(await service.AuthenticateAsync(suppliedKey, CancellationToken.None));
        Assert.True(await service.AuthenticateAsync("private-key", CancellationToken.None));
    }

    [Fact]
    public async Task MissingLegacyKeyDoesNotMatchNullInputWithKeyList()
    {
        var service = Create(new BaGetterOptions
        {
            Authentication = new NugetAuthenticationOptions
            {
                ApiKeys =
                [
                    new ApiKey { Key = "team-key" },
                    new ApiKey { Key = "" },
                    new ApiKey { Key = null }
                ]
            }
        });

        Assert.False(await service.AuthenticateAsync(null, CancellationToken.None));
        Assert.False(await service.AuthenticateAsync("different", CancellationToken.None));
        Assert.True(await service.AuthenticateAsync("team-key", CancellationToken.None));
    }

    private static ApiKeyAuthenticationService Create(BaGetterOptions options)
    {
        var snapshot = new Mock<IOptionsSnapshot<BaGetterOptions>>();
        snapshot.Setup(s => s.Value).Returns(options);
        return new ApiKeyAuthenticationService(snapshot.Object);
    }
}
