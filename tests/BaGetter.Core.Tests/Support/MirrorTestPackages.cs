using System.IO;
using NuGet.Packaging;
using NuGet.Versioning;

namespace BaGetter.Core.Tests.Support;

internal static class MirrorTestPackages
{
    public static MemoryStream Create(string id, string version)
    {
        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = "Mirror identity regression fixture",
        };
        builder.Authors.Add("BaGetter Tests");
        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = typeof(MirrorTestPackages).Assembly.Location,
            TargetPath = "lib/net10.0/MirrorTest.dll",
        });

        var stream = new MemoryStream();
        builder.Save(stream);
        stream.Position = 0;
        return stream;
    }
}
