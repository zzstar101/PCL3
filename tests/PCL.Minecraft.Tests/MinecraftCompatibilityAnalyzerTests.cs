using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Core.Compatibility;
using PCL3.Minecraft.Compatibility;
using PCL3.Minecraft.Java;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MinecraftCompatibilityAnalyzerTests
{
    [TestMethod]
    public void TooOldJava_IsAnError()
    {
        var runtime = new JavaRuntimeDescriptor(
            "/java",
            17,
            PlatformArchitecture.X64,
            "Test");
        var requirement = new JavaRequirement(21);
        var target = new PlatformTarget(
            PlatformOperatingSystem.Windows,
            PlatformArchitecture.X64);

        var report = MinecraftCompatibilityAnalyzer.AnalyzeJava(
            runtime,
            requirement,
            target);

        Assert.IsFalse(report.IsCompatible);
        Assert.IsTrue(report.Issues.Any(issue =>
            issue.Code == "java.version.too-old" &&
            issue.Severity == CompatibilitySeverity.Error));
    }

    [TestMethod]
    public void DifferentPlatformArchitecture_IsAWarningWhenNotExplicitlyRequired()
    {
        var runtime = new JavaRuntimeDescriptor(
            "/java",
            21,
            PlatformArchitecture.X64,
            "Test");
        var requirement = new JavaRequirement(21);
        var target = new PlatformTarget(
            PlatformOperatingSystem.MacOS,
            PlatformArchitecture.Arm64);

        var report = MinecraftCompatibilityAnalyzer.AnalyzeJava(
            runtime,
            requirement,
            target);

        Assert.IsTrue(report.IsCompatible);
        Assert.IsTrue(report.Issues.Any(issue =>
            issue.Code == "java.architecture.emulation" &&
            issue.Severity == CompatibilitySeverity.Warning));
    }

    [TestMethod]
    public void UnknownArchitecture_DoesNotProduceEmulationWarning()
    {
        var runtime = new JavaRuntimeDescriptor(
            "/java",
            21,
            PlatformArchitecture.Unknown,
            "Test");
        var requirement = new JavaRequirement(21);
        var target = new PlatformTarget(
            PlatformOperatingSystem.Linux,
            PlatformArchitecture.X64);

        var report = MinecraftCompatibilityAnalyzer.AnalyzeJava(
            runtime,
            requirement,
            target);

        Assert.IsTrue(report.IsCompatible);
        Assert.IsFalse(report.Issues.Any(issue =>
            issue.Code == "java.architecture.emulation"));
    }
}
