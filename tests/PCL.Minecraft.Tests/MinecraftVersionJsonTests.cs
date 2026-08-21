using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Metadata;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class MinecraftVersionJsonTests
{
    [TestMethod]
    public void Parse_ModernMetadata_NormalizesConditionalArguments()
    {
        const string json = """
        {
          "id": "test-modern",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "javaVersion": {
            "component": "java-runtime-delta",
            "majorVersion": 21
          },
          "arguments": {
            "jvm": [
              "-Djava.library.path=${natives_directory}",
              {
                "rules": [
                  {
                    "action": "allow",
                    "os": { "name": "osx" }
                  }
                ],
                "value": [
                  "-XstartOnFirstThread",
                  "-Dpcl.test=true"
                ]
              }
            ],
            "game": [
              "--username",
              "${auth_player_name}"
            ]
          },
          "libraries": [
            {
              "name": "org.lwjgl:lwjgl:3.3.3",
              "rules": [
                {
                  "action": "allow",
                  "os": { "name": "osx", "arch": "arm64" }
                }
              ],
              "natives": {
                "osx": "natives-macos-${arch}"
              }
            }
          ]
        }
        """;

        var metadata = MinecraftVersionJson.Parse(json);

        Assert.AreEqual("test-modern", metadata.Id);
        Assert.AreEqual("net.minecraft.client.main.Main", metadata.MainClass);
        Assert.AreEqual(21, metadata.JavaVersion?.MajorVersion);
        Assert.AreEqual(2, metadata.JvmArguments.Count);
        Assert.AreEqual(2, metadata.JvmArguments[1].Values.Count);
        Assert.AreEqual(1, metadata.Libraries.Count);
        Assert.AreEqual(
            "natives-macos-${arch}",
            metadata.Libraries[0].Natives["osx"]);
    }

    [TestMethod]
    public void ResolveArguments_AppliesOperatingSystemRulesAndFlattensValues()
    {
        const string json = """
        {
          "id": "test-rules",
          "mainClass": "example.Main",
          "arguments": {
            "jvm": [
              "-Dcommon=true",
              {
                "rules": [
                  {
                    "action": "allow",
                    "os": { "name": "osx" }
                  }
                ],
                "value": [
                  "-XstartOnFirstThread",
                  "-Dmac=true"
                ]
              },
              {
                "rules": [
                  {
                    "action": "allow",
                    "os": { "name": "windows" }
                  }
                ],
                "value": "-Dwindows=true"
              }
            ]
          }
        }
        """;

        var metadata = MinecraftVersionJson.Parse(json);
        var context = new MinecraftRuleContext(
            new PlatformTarget(
                PlatformOperatingSystem.MacOS,
                PlatformArchitecture.Arm64),
            "15.0",
            new Dictionary<string, bool>());

        var resolved = MinecraftArgumentResolver.Resolve(
            metadata.JvmArguments,
            context);

        CollectionAssert.AreEqual(
            new[]
            {
                "-Dcommon=true",
                "-XstartOnFirstThread",
                "-Dmac=true"
            },
            resolved.ToArray());
    }

    [TestMethod]
    public void Parse_LegacyMetadata_PreservesMinecraftArguments()
    {
        const string json = """
        {
          "id": "1.8.9",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "minecraftArguments": "--username ${auth_player_name} --version ${version_name}",
          "libraries": []
        }
        """;

        var metadata = MinecraftVersionJson.Parse(json);

        Assert.AreEqual(
            "--username ${auth_player_name} --version ${version_name}",
            metadata.LegacyMinecraftArguments);
        Assert.AreEqual(0, metadata.JvmArguments.Count);
        Assert.AreEqual(0, metadata.GameArguments.Count);
    }
}
