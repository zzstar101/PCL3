using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Libraries;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MavenCoordinateTests
{
    [TestMethod]
    public void Parse_StandardCoordinate_BuildsRepositoryPath()
    {
        var coordinate = MavenCoordinate.Parse("org.lwjgl:lwjgl:3.3.3");

        Assert.AreEqual("org.lwjgl", coordinate.Group);
        Assert.AreEqual("lwjgl", coordinate.Artifact);
        Assert.AreEqual("3.3.3", coordinate.Version);
        Assert.IsNull(coordinate.Classifier);
        Assert.AreEqual("jar", coordinate.Extension);
        Assert.AreEqual(
            "org/lwjgl/lwjgl/3.3.3/lwjgl-3.3.3.jar",
            coordinate.RepositoryPath);
    }

    [TestMethod]
    public void Parse_ClassifierAndExtension_BuildsArtifactName()
    {
        var coordinate = MavenCoordinate.Parse(
            "com.example:native-lib:1.2.0:natives-linux@zip");

        Assert.AreEqual("natives-linux", coordinate.Classifier);
        Assert.AreEqual("zip", coordinate.Extension);
        Assert.AreEqual(
            "native-lib-1.2.0-natives-linux.zip",
            coordinate.FileName);
    }

    [TestMethod]
    public void Parse_InvalidCoordinate_Throws()
    {
        Assert.ThrowsExactly<FormatException>(() =>
            MavenCoordinate.Parse("not-a-coordinate"));
    }
}
