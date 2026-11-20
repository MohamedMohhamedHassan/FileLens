using FileLens.Domain.Models;

namespace FileLens.UnitTests.Visual;

public sealed class VisualSearchModelTests
{
    [Fact]
    public void SearchOptions_AllowOnlySupportedResultLimits()
    {
        Assert.Equal(25, new VisualSearchOptions(7).SafeLimit);
        Assert.Equal(100, new VisualSearchOptions(100).SafeLimit);
        Assert.Equal(0.0, new VisualSearchOptions(25, -1).SafeThreshold);
        Assert.Equal(1.0, new VisualSearchOptions(25, 2).SafeThreshold);
    }

    [Fact]
    public void RegionSelection_ClampsToNormalizedCoordinates()
    {
        var region = new RegionSelection(-0.2, 0.4, 1.4, 0.5).Clamp();
        Assert.Equal(0.0, region.X);
        Assert.Equal(0.4, region.Y);
        Assert.Equal(1.0, region.Width);
        Assert.Equal(0.5, region.Height);
    }
}
