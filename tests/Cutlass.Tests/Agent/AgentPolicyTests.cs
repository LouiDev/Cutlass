using Cutlass.Agent.Hosting;
using Cutlass.Media.Settings;

namespace Cutlass.Tests.Agent;

/// <summary>The agent settings and the policy built from them (AGENT_INTEGRATION 3.3, 4.6 and 6.1).</summary>
public sealed class AgentPolicyTests
{
    [Fact]
    public void SettingsWithFolderListsCompareByValue()
    {
        var a = new AppSettings { AgentReadableFolders = ["D:/Footage", "E:/Music"], AgentWritableFolders = ["D:/renders"] };
        var b = new AppSettings { AgentReadableFolders = new List<string> { "D:/Footage", "E:/Music" }, AgentWritableFolders = ["D:/renders"] };
        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { AgentWritableFolders = [] });
        Assert.Empty(new AppSettings().AgentReadableFolders);
        Assert.Equal(new AppSettings(), new AppSettings());
    }

    [Fact]
    public void TheAliasAndTheDefaultsFollowTheSpec()
    {
        var s = new AppSettings { AllowAgentConnections = true };
        Assert.True(s.AgentConnectionsEnabled);
        Assert.Equal(47390, s.AgentPort);
        Assert.Equal(90, s.AgentLeaseSeconds);
        Assert.Equal(300, s.AgentMaxLeaseSeconds);
        Assert.False(s.AgentAskFirst);
    }

    [Fact]
    public void ThePolicyClampsLeasesAndMergesFolders()
    {
        var settings = new AppSettings
        {
            AgentLeaseSeconds = 5,
            AgentMaxLeaseSeconds = 9999,
            AgentAskFirst = true,
            AgentReadableFolders = ["D:/Footage", " "],
            AgentWritableFolders = ["D:/renders"],
            ExportFolder = "D:/exports",
        };
        var policy = AgentPolicy.FromSettings(settings, ["E:/more"], ["E:/out"], acceptsDiscardChanges: true);
        Assert.Equal(15, policy.DefaultLeaseSeconds);
        Assert.Equal(600, policy.MaxLeaseSeconds);
        Assert.True(policy.AskFirst);
        Assert.Equal(["D:/Footage", "E:/more"], policy.ReadFolders.ToArray());
        Assert.Equal(["D:/renders", "E:/out"], policy.WriteFolders.ToArray());
        Assert.Equal("D:/exports", policy.ExportFolder);
        Assert.True(policy.AcceptsDiscardChanges);
    }
}
