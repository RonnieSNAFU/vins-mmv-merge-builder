using NRMerge;
using Xunit;

public class ModelCloneTests
{
    [Theory]
    [InlineData(@"N:\GR\data\Model\parts\Weapon\WP_A_1572\WP_A_1572_a.tif", @"N:\GR\data\Model\parts\Weapon\WP_A_1581\WP_A_1581_a.tif")]
    [InlineData("wp_a_1572.flver", "wp_a_1581.flver")]
    [InlineData("wp_a_1572_1.flver", "wp_a_1581_1.flver")]
    [InlineData("wp_a_15720.flver", "wp_a_15720.flver")]
    [InlineData("WP_A_1570_a", "WP_A_1570_a")]
    public void RenamesOnlyTheExactModelId(string input, string expected)
    {
        Assert.Equal(expected, ModelClone.RenameModelRefs(input, 1572, 1581));
    }
}
