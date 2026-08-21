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
        Assert.IsTrue(MinecraftRuleEvaluator.IsAllowed(null, context));
    }

    [TestMethod]
    [DataRow(PlatformOperatingSystem.Windows, "windows")]
    [DataRow(PlatformOperatingSystem.MacOS, "osx")]
    [DataRow(PlatformOperatingSystem.Linux, "linux")]
    public void MatchingOperatingSystemRule_Allows(
        PlatformOperatingSystem operatingSystem,
        string mojangName)
    {
        var context = CreateContext(operatingSystem);
        MinecraftRule[] rules =
        [
            new(
                MinecraftRuleAction.Allow,
                new MinecraftOsRule(Name: mojangName))
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
    public void UnknownOperatingSystem_DoesNotMatchKnownOperatingSystemRule()
    {
        var context = CreateContext(PlatformOperatingSystem.Unknown);
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

    [TestMethod]
    public void MissingFeature_IsTreatedAsFalse()
    {
        var context = CreateContext(PlatformOperatingSystem.Linux);
        MinecraftRule[] requiresFalse =
        [
            new(
                MinecraftRuleAction.Allow,
                Features: new Dictionary<string, bool>
                {
                    ["has_custom_resolution"] = false
                })
        ];
        MinecraftRule[] requiresTrue =
        [
            new(
                MinecraftRuleAction.Allow,
                Features: new Dictionary<string, bool>
                {
                    ["has_custom_resolution"] = true
                })
        ];

        Assert.IsTrue(MinecraftRuleEvaluator.IsAllowed(requiresFalse, context));
        Assert.IsFalse(MinecraftRuleEvaluator.IsAllowed(requiresTrue, context));
    }

    [TestMethod]
    public void MultipleRequiredFeatures_PartialMismatch_DoesNotMatch()
    {
        var context = new MinecraftRuleContext(
            new PlatformTarget(PlatformOperatingSystem.Linux, PlatformArchitecture.X64),
            "1.0",
            new Dictionary<string, bool>
            {
                ["is_demo_user"] = true,
                ["has_custom_resolution"] = false
            });
        MinecraftRule[] rules =
        [
            new(
                MinecraftRuleAction.Allow,
                Features: new Dictionary<string, bool>
                {
                    ["is_demo_user"] = true,
                    ["has_custom_resolution"] = true
                })
        ];

        Assert.IsFalse(MinecraftRuleEvaluator.IsAllowed(rules, context));
    }

    [TestMethod]
    public void EmptyOrNullFeatureRequirements_HaveNoConstraints()
    {
        var context = CreateContext(PlatformOperatingSystem.Linux);
        MinecraftRule[] emptyFeatures =
        [
            new(
                MinecraftRuleAction.Allow,
                Features: new Dictionary<string, bool>())
        ];
        MinecraftRule[] nullFeatures =
        [
            new(MinecraftRuleAction.Allow, Features: null)
        ];

        Assert.IsTrue(MinecraftRuleEvaluator.IsAllowed(emptyFeatures, context));
        Assert.IsTrue(MinecraftRuleEvaluator.IsAllowed(nullFeatures, context));
    }

    private static MinecraftRuleContext CreateContext(PlatformOperatingSystem operatingSystem) =>
        new(
            new PlatformTarget(operatingSystem, PlatformArchitecture.X64),
            "1.0",
            new Dictionary<string, bool>());
}
