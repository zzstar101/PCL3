using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Metadata;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MinecraftRuleEvaluatorTests
{
    [TestMethod]
    public void NoRules_AllowsByDefault()
    {
        var context = CreateContext(PlatformOperatingSystem.Linux);

        Assert.IsTrue(MinecraftRuleEvaluator.IsAllowed([], context));
    }

    [TestMethod]
    public void MatchingOperatingSystemRule_Allows()
    {
        var context = CreateContext(PlatformOperatingSystem.Windows);
        MinecraftRule[] rules =
        [
            new(
                MinecraftRuleAction.Allow,
                new MinecraftOsRule(Name: "windows"))
        ];

        Assert.IsTrue(MinecraftRuleEvaluator.IsAllowed(rules, context));
    }

    [TestMethod]
    public void NonMatchingOperatingSystemRule_DoesNotAllow()
    {
        var context = CreateContext(PlatformOperatingSystem.Linux);
        MinecraftRule[] rules =
        [
            new(
                MinecraftRuleAction.Allow,
                new MinecraftOsRule(Name: "windows"))
        ];

        Assert.IsFalse(MinecraftRuleEvaluator.IsAllowed(rules, context));
    }

    [TestMethod]
    public void LaterMatchingRule_OverridesEarlierRule()
    {
        var context = CreateContext(PlatformOperatingSystem.Windows);
        MinecraftRule[] rules =
        [
            new(MinecraftRuleAction.Allow),
            new(
                MinecraftRuleAction.Disallow,
                new MinecraftOsRule(Name: "windows"))
        ];

        Assert.IsFalse(MinecraftRuleEvaluator.IsAllowed(rules, context));
    }

    [TestMethod]
    public void RequiredFeature_MustMatch()
    {
        var context = new MinecraftRuleContext(
            new PlatformTarget(PlatformOperatingSystem.MacOS, PlatformArchitecture.Arm64),
            "15.0",
            new Dictionary<string, bool>
            {
                ["is_demo_user"] = false
            });

        MinecraftRule[] rules =
        [
            new(
                MinecraftRuleAction.Allow,
                Features: new Dictionary<string, bool>
                {
                    ["is_demo_user"] = true
                })
        ];

        Assert.IsFalse(MinecraftRuleEvaluator.IsAllowed(rules, context));
    }

    private static MinecraftRuleContext CreateContext(PlatformOperatingSystem operatingSystem) =>
        new(
            new PlatformTarget(operatingSystem, PlatformArchitecture.X64),
            "1.0",
            new Dictionary<string, bool>());
}
