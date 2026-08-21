using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL3.Minecraft.Accounts;
using PCL3.Minecraft.Artifacts;
using PCL3.Minecraft.Java;
using PCL3.Minecraft.Launch;
using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Runtime;
using PCL3.Platform;

namespace PCL3.Minecraft.Tests;

[TestClass]
public sealed class LaunchExecutionTests
{
    [TestMethod]
    public void LaunchVariables_ComposeSessionRuntimeAndCompatibilityAliases()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var (chain, ruleContext, runtime) = CreateRuntime(root, includeAssetIndex: true);
            var session = new MinecraftSession(
                "Player",
                "00112233-4455-6677-8899-aabbccddeeff",
                "secret-token",
                xuid: "123456",
                clientId: "client-123");
            var context = new MinecraftLaunchContext(
                chain,
                ruleContext,
                runtime,
                session,
                Path.Combine(root, "game"),
                LauncherVersion: "4.0.0",
                ResolutionWidth: 1920,
                ResolutionHeight: 1080);

            var variables = MinecraftLaunchVariableComposer.Create(context);

            Assert.AreEqual("Player", variables["auth_player_name"]);
            Assert.AreEqual("00112233445566778899aabbccddeeff", variables["auth_uuid"]);
            Assert.AreEqual("secret-token", variables["auth_access_token"]);
            Assert.AreEqual("secret-token", variables["access_token"]);
            Assert.AreEqual("secret-token", variables["auth_session"]);
            Assert.AreEqual("123456", variables["auth_xuid"]);
            Assert.AreEqual("client-123", variables["clientid"]);
            Assert.AreEqual("19", variables["assets_index_name"]);
            Assert.AreEqual("1920", variables["resolution_width"]);
            Assert.AreEqual("1080", variables["resolution_height"]);
            StringAssert.EndsWith(
                variables["primary_jar"],
                Path.Combine("versions", "test", "test.jar"));
            Assert.AreEqual(runtime.LibrariesDirectory, variables["libraries_directory"]);
            Assert.IsFalse(session.ToString().Contains("secret-token", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void LaunchPlanBuilder_UsesTypedContextAndRedactsDiagnosticArguments()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var (chain, ruleContext, runtime) = CreateRuntime(root, includeAssetIndex: false);
            var context = new MinecraftLaunchContext(
                chain,
                ruleContext,
                runtime,
                new MinecraftSession(
                    "Player",
                    "00112233445566778899aabbccddeeff",
                    "secret-token"),
                Path.Combine(root, "game"),
                JavaExecutableOverride: "java-test");

            var plan = MinecraftLaunchPlanBuilder.Build(context);

            Assert.AreEqual("java-test", plan.Executable);
            CollectionAssert.Contains(plan.Arguments.ToList(), "Player");
            CollectionAssert.Contains(plan.Arguments.ToList(), "secret-token");
            Assert.IsFalse(plan.ToString().Contains("secret-token", StringComparison.Ordinal));
            Assert.IsFalse(context.ToString().Contains("secret-token", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ProcessStartInfoBuilder_PreservesArgumentBoundariesWithoutShell()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var plan = new LaunchPlan(
                "java",
                root,
                new[] { "-Dmessage=hello world", "quoted\"value", "plain" },
                new Dictionary<string, string> { ["PCL3_TEST"] = "value" });

            ProcessStartInfo startInfo = MinecraftProcessStartInfoBuilder.Build(
                plan,
                new MinecraftProcessStartOptions(
                    RedirectStandardOutput: true,
                    RedirectStandardError: true,
                    CreateNoWindow: true));

            Assert.IsFalse(startInfo.UseShellExecute);
            Assert.AreEqual(3, startInfo.ArgumentList.Count);
            Assert.AreEqual("-Dmessage=hello world", startInfo.ArgumentList[0]);
            Assert.AreEqual("quoted\"value", startInfo.ArgumentList[1]);
            Assert.AreEqual("plain", startInfo.ArgumentList[2]);
            Assert.AreEqual("value", startInfo.Environment["PCL3_TEST"]);
            Assert.IsTrue(startInfo.RedirectStandardOutput);
            Assert.IsTrue(startInfo.RedirectStandardError);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Pipeline_DoesNotStartProcessWhenPreparationFails()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var (chain, ruleContext, runtime) = CreateRuntime(
                root,
                includeAssetIndex: false,
                includeClientDownload: true);
            var failingAcquirer = new FailingArtifactAcquirer();
            var executor = new CountingProcessExecutor();
            var pipeline = new MinecraftLaunchPipeline(
                new MinecraftPreparationService(failingAcquirer),
                executor);
            var context = CreateOfflineContext(root, chain, ruleContext, runtime);

            await using var result = await pipeline.PrepareAndStartAsync(context);

            Assert.IsFalse(result.Started);
            Assert.IsFalse(result.Preparation.IsSuccess);
            Assert.AreEqual(0, executor.StartCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Pipeline_StartsOnlyAfterSuccessfulPreparation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var (chain, ruleContext, runtime) = CreateRuntime(root, includeAssetIndex: false);
            var executor = new CountingProcessExecutor();
            var pipeline = new MinecraftLaunchPipeline(
                new MinecraftPreparationService(new SuccessfulArtifactAcquirer()),
                executor);
            var context = CreateOfflineContext(root, chain, ruleContext, runtime);

            await using var result = await pipeline.PrepareAndStartAsync(context);

            Assert.IsTrue(result.Started);
            Assert.IsTrue(result.Preparation.IsSuccess);
            Assert.AreEqual(1, executor.StartCalls);
            Assert.IsNotNull(result.LaunchPlan);
            Assert.AreEqual(42, result.Process?.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Pipeline_RunToExitReturnsProcessExitCode()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var (chain, ruleContext, runtime) = CreateRuntime(root, includeAssetIndex: false);
            var process = new FakeRunningProcess(exitCode: 17);
            var executor = new CountingProcessExecutor(process);
            var pipeline = new MinecraftLaunchPipeline(
                new MinecraftPreparationService(new SuccessfulArtifactAcquirer()),
                executor);
            var context = CreateOfflineContext(root, chain, ruleContext, runtime);

            var result = await pipeline.RunToExitAsync(context);

            Assert.IsTrue(result.Started);
            Assert.AreEqual(17, result.ExitCode);
            Assert.AreEqual(1, executor.StartCalls);
            Assert.AreEqual(1, process.WaitCalls);
            Assert.AreEqual(0, process.TerminateCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task Pipeline_CancellationTerminatesProcessExactlyOnceWhenEnabled()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var (chain, ruleContext, runtime) = CreateRuntime(root, includeAssetIndex: false);
            var process = new FakeRunningProcess(waitUntilCancellation: true);
            var executor = new CountingProcessExecutor(process);
            var pipeline = new MinecraftLaunchPipeline(
                new MinecraftPreparationService(new SuccessfulArtifactAcquirer()),
                executor);
            var context = CreateOfflineContext(root, chain, ruleContext, runtime);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                pipeline.RunToExitAsync(
                    context,
                    terminateOnCancellation: true,
                    cancellationToken: cancellation.Token));

            Assert.AreEqual(1, executor.StartCalls);
            Assert.AreEqual(1, process.WaitCalls);
            Assert.AreEqual(1, process.TerminateCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static MinecraftLaunchContext CreateOfflineContext(
        string root,
        MinecraftVersionChain chain,
        MinecraftRuleContext ruleContext,
        MinecraftRuntimePlan runtime) =>
        new(
            chain,
            ruleContext,
            runtime,
            MinecraftSession.CreateOffline(
                "Player",
                Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")),
            Path.Combine(root, "game"),
            JavaExecutableOverride: "java-test");

    private static (MinecraftVersionChain Chain, MinecraftRuleContext RuleContext, MinecraftRuntimePlan Runtime) CreateRuntime(
        string root,
        bool includeAssetIndex,
        bool includeClientDownload = false)
    {
        var assetIndex = includeAssetIndex
            ? "\"assets\":\"19\",\"assetIndex\":{\"id\":\"19\",\"url\":\"https://example.invalid/19.json\"},"
            : string.Empty;
        var clientDownload = includeClientDownload
            ? "\"downloads\":{\"client\":{\"url\":\"https://example.invalid/client.jar\"}},"
            : string.Empty;
        var metadata = MinecraftVersionJson.Parse($$"""
        {
          "id": "test",
          "type": "release",
          "mainClass": "example.Main",
          "javaVersion": { "component": "java-runtime", "majorVersion": 21 },
          {{assetIndex}}
          {{clientDownload}}
          "arguments": {
            "jvm": ["-cp", "${classpath}"],
            "game": [
              "--username", "${auth_player_name}",
              "--uuid", "${auth_uuid}",
              "--accessToken", "${auth_access_token}",
              "--xuid", "${auth_xuid}",
              "--clientId", "${clientid}"
            ]
          }
        }
        """);
        var chain = new MinecraftVersionChain(new[] { metadata });
        var ruleContext = new MinecraftRuleContext(
            PlatformTarget.Current,
            "test",
            new Dictionary<string, bool>());
        var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "java.exe" : "java");
        var java = new JavaRuntimeDescriptor(
            Path.Combine(root, "java-home"),
            21,
            PlatformTarget.Current.Architecture,
            ExecutablePath: executable);
        var runtime = MinecraftRuntimePlanner.Build(
            chain,
            ruleContext,
            root,
            Path.Combine(root, "natives"),
            new[] { java },
            clientJarPath: Path.Combine(root, "versions", "test", "test.jar"));

        return (chain, ruleContext, runtime);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcl3-launch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SuccessfulArtifactAcquirer : IMinecraftArtifactAcquirer
    {
        public Task<MinecraftArtifactAcquisitionResult> AcquireAsync(
            MinecraftArtifactAcquisitionPlan plan,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MinecraftArtifactAcquisitionResult(
                Array.Empty<MinecraftArtifactAcquisitionItemResult>()));
    }

    private sealed class FailingArtifactAcquirer : IMinecraftArtifactAcquirer
    {
        public Task<MinecraftArtifactAcquisitionResult> AcquireAsync(
            MinecraftArtifactAcquisitionPlan plan,
            CancellationToken cancellationToken = default)
        {
            var artifact = plan.Artifacts.First();
            return Task.FromResult(new MinecraftArtifactAcquisitionResult(new[]
            {
                new MinecraftArtifactAcquisitionItemResult(
                    artifact,
                    MinecraftArtifactAcquisitionStatus.Failed,
                    Error: "expected test failure")
            }));
        }
    }

    private sealed class CountingProcessExecutor : IMinecraftProcessExecutor
    {
        private readonly IMinecraftRunningProcess _process;

        public CountingProcessExecutor(IMinecraftRunningProcess? process = null)
        {
            _process = process ?? new FakeRunningProcess();
        }

        public int StartCalls { get; private set; }

        public IMinecraftRunningProcess Start(
            LaunchPlan plan,
            MinecraftProcessStartOptions? options = null)
        {
            StartCalls++;
            return _process;
        }
    }

    private sealed class FakeRunningProcess(
        int exitCode = 0,
        bool waitUntilCancellation = false) : IMinecraftRunningProcess
    {
        public int Id => 42;

        public bool HasExited => !waitUntilCancellation;

        public TextReader? StandardOutput => null;

        public TextReader? StandardError => null;

        public int WaitCalls { get; private set; }

        public int TerminateCalls { get; private set; }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            WaitCalls++;
            if (waitUntilCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return exitCode;
        }

        public Task TerminateAsync(CancellationToken cancellationToken = default)
        {
            TerminateCalls++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
