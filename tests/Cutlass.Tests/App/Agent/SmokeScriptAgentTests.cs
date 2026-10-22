using Cutlass.App.Services;

namespace Cutlass.Tests.App.Agent;

/// <summary>AGENT_INTEGRATION 6.4: the agent smoke commands parse with their arguments and options.</summary>
public sealed class SmokeScriptAgentTests
{
    [Fact]
    public void TheAgentCommandsAreKnown()
    {
        string[] commands =
        [
            "agent-connect", "agent-call", "expect-locked", "expect-unlocked", "agent-takeover", "agent-pause", "agent-resume",
            "expect-readonly", "expect-agent-log", "expect-no-refused",
        ];
        foreach (var command in commands)
        {
            Assert.True(SmokeScript.Commands.ContainsKey(command), command);
        }
    }

    [Fact]
    public void AgentCallsKeepTheirJsonAndOptions()
    {
        var steps = SmokeScript.Parse(
            "agent-connect name=Smoke\n" +
            "agent-call lock.acquire {'agentName':'Smoke','reason':'m9'} save=lock=result.lockId\n" +
            "agent-call edit.title.add \"{'trackId':'$v2','title':{'text':'Meet Ana'}}\" expect=ok\n" +
            "agent-call edit.split {'frame':30} expect=lock_required\n" +
            "agent-call frame.render {'frame':30,'format':'base64'} png=shots/frame30.png\n" +
            "expect-locked holder=Smoke paused\n" +
            "expect-readonly keys=Ctrl+Z,Delete\n" +
            "expect-agent-log edit.split code=lock_required\n" +
            "agent-takeover\nexpect-unlocked\nexpect-no-refused\npanel agent\n");

        Assert.Equal(12, steps.Count);
        Assert.Equal("Smoke", steps[0].Option("name"));
        Assert.Equal(["lock.acquire", "{'agentName':'Smoke','reason':'m9'}"], steps[1].Positional());
        Assert.Equal("lock=result.lockId", steps[1].Option("save"));
        Assert.Equal("{'trackId':'$v2','title':{'text':'Meet Ana'}}", steps[2].Positional()[1]);
        Assert.Equal("lock_required", steps[3].Option("expect"));
        Assert.Equal("shots/frame30.png", steps[4].Option("png"));
        Assert.Equal("Smoke", steps[5].Option("holder"));
        Assert.True(steps[5].Flag("paused"));
        Assert.Equal("Ctrl+Z,Delete", steps[6].Option("keys"));
        Assert.Equal(["edit.split"], steps[7].Positional());
        Assert.Equal("lock_required", steps[7].Option("code"));
        Assert.Equal(["agent"], steps[11].Positional());
    }

    [Fact]
    public void JsonArgumentsUseSingleQuotesForStrings()
    {
        Assert.Equal("{\"a\":\"b c\",\"n\":1}", SmokeRunner.ToJson("{'a':'b c','n':1}", new Dictionary<string, string>()));
        Assert.Equal(
            "{\"mediaId\":\"m_42\",\"at\":0}",
            SmokeRunner.ToJson("{'mediaId':'$m1','at':0}", new Dictionary<string, string> { ["m1"] = "m_42" }));
        Assert.Throws<FormatException>(() => SmokeRunner.ToJson("{'a':$missing}", new Dictionary<string, string>()));
    }
}
