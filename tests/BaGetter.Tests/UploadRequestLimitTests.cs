using System;
using BaGetter.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace BaGetter.Tests;

public class UploadRequestLimitTests
{
    [Fact]
    public void LegacySizeHasFiniteKestrelLimit()
    {
        Assert.Equal(8L * 1024 * 1024 * 1024,
            ConfigureBaGetterServer.GetMaxRequestBodyBytes(8, null));
    }

    [Fact]
    public void MiBLimitCanReduceButNotIncreaseMaximum()
    {
        Assert.Equal(256L * 1024 * 1024,
            ConfigureBaGetterServer.GetMaxRequestBodyBytes(8, 256));
        Assert.Equal(1024L * 1024 * 1024,
            ConfigureBaGetterServer.GetMaxRequestBodyBytes(1, 2048));
    }

    [Fact]
    public void RejectsZeroLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConfigureBaGetterServer.GetMaxRequestBodyBytes(0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConfigureBaGetterServer.GetMaxRequestBodyBytes(1, 0));
    }

    [Fact]
    public void MultipartAndIisUseSameLimit()
    {
        var configurator = new ConfigureBaGetterServer(
            Options.Create(new BaGetterOptions
            {
                MaxPackageSizeGiB = 8,
                MaxRequestBodySizeMiB = 256
            }),
            new ConfigurationBuilder().Build());

        var form = new FormOptions();
        configurator.Configure(form);
        var iis = new IISServerOptions();
        configurator.Configure(iis);

        Assert.Equal(256L * 1024 * 1024, form.MultipartBodyLengthLimit);
        Assert.Equal(256L * 1024 * 1024, iis.MaxRequestBodySize);
    }
}
