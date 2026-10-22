using System.Text.Json;
using System.Text.Json.Nodes;
using Cutlass.App.Services.Agent.Clients;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 9.3: the JSON (with comments) and TOML editors change only the edited entry. Upsert then remove
/// gives the original text back where the parent object existed; new members take the file's indentation, line ends
/// and trailing-comma style; inline objects, tabs and CRLF files stay valid; the TOML editor keeps other tables, keys
/// and comments.
/// </summary>
public sealed class ConfigEditorTests
{
    private static readonly string[] Path = ["mcpServers", "cutlass"];

    private static JsonNode Entry => new JsonObject { ["command"] = "/opt/c/mcp", ["args"] = new JsonArray() };

    [Fact]
    public void AnEmptyFileBecomesANewDocument()
    {
        Assert.Equal(
            "{\n  \"mcpServers\": {\n    \"cutlass\": {\n      \"command\": \"/opt/c/mcp\",\n      \"args\": []\n    }\n  }\n}\n",
            JsoncEditor.Upsert(string.Empty, Path, Entry));
    }

    [Theory]
    [InlineData("{\n  \"mcpServers\": {\n    \"other\": { \"command\": \"x\" }\n  }\n}\n")]
    [InlineData("{\n  // comment\n  \"mcpServers\": {\n    \"other\": { \"command\": \"x\" }, /* trailing */\n  },\n}\n")]
    [InlineData("{\r\n\t\"mcpServers\": {\r\n\t\t\"other\": {}\r\n\t}\r\n}\r\n")]
    [InlineData("{\"mcpServers\": {\"other\": {\"command\": \"x\"}}}")]
    [InlineData("{\n  \"mcpServers\": {}\n}\n")]
    public void UpsertThenRemoveGivesTheOriginalBack(string original)
    {
        var added = JsoncEditor.Upsert(original, Path, Entry);
        Assert.Equal("/opt/c/mcp", JsoncEditor.Read(added, Path)!["command"]!.GetValue<string>());
        Assert.NotNull(JsoncEditor.Read(added, ["mcpServers", "other"]) ?? JsoncEditor.Read(original, ["mcpServers"]));
        using (JsonDocument.Parse(added, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
        {
        }

        var removed = JsoncEditor.Remove(added, Path, out var wasThere);
        Assert.True(wasThere);
        if (original.Contains("\"other\"", StringComparison.Ordinal))
        {
            Assert.Equal(original, removed);
        }
        else
        {
            Assert.Equal("{\n  \"mcpServers\": {}\n}\n", removed);
        }
    }

    [Fact]
    public void NewMembersFollowTheFileStyle()
    {
        var tabs = JsoncEditor.Upsert("{\r\n\t\"mcpServers\": {\r\n\t\t\"other\": {}\r\n\t}\r\n}\r\n", Path, Entry);
        Assert.Contains("\t\t\"other\": {},\r\n\t\t\"cutlass\": {\r\n\t\t\t\"command\": \"/opt/c/mcp\",\r\n\t\t\t\"args\": []\r\n\t\t}\r\n\t}", tabs, StringComparison.Ordinal);

        var missingParent = JsoncEditor.Upsert("{\n  \"theme\": \"dark\"\n}\n", Path, Entry);
        Assert.Equal("{\n  \"theme\": \"dark\",\n  \"mcpServers\": {\n    \"cutlass\": {\n      \"command\": \"/opt/c/mcp\",\n      \"args\": []\n    }\n  }\n}\n", missingParent);

        var replaced = JsoncEditor.Upsert("{ \"mcpServers\": { \"cutlass\": 1, \"b\": 2 } }", Path, Entry);
        Assert.StartsWith("{ \"mcpServers\": { \"cutlass\": {", replaced, StringComparison.Ordinal);
        Assert.EndsWith("}, \"b\": 2 } }", replaced, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingAMissingMemberChangesNothingAndABrokenFileThrows()
    {
        const string text = "{ \"a\": 1 }";
        Assert.Same(text, JsoncEditor.Remove(text, Path, out var removed));
        Assert.False(removed);
        Assert.ThrowsAny<JsonException>(() => JsoncEditor.Upsert("{ \"a\": ", Path, Entry));
        Assert.Throws<FormatException>(() => JsoncEditor.Upsert("[1, 2]", Path, Entry));
        Assert.Null(JsoncEditor.Read("{ \"mcpServers\": 3 }", Path));
    }

    [Fact]
    public void QuotedStringsKeepPathsReadable()
    {
        Assert.Equal("\"C:\\\\Program Files\\\\Ü.exe\"", JsoncEditor.Quote("C:\\Program Files\\Ü.exe"));
    }

    [Fact]
    public void TomlReadsUpsertsAndRemovesOneTable()
    {
        string[] table = ["mcp_servers", "cutlass"];
        var values = new JsonObject { ["command"] = "C:\\x\\mcp.exe", ["args"] = new JsonArray("a b", "c\"d") };
        var added = TomlTableEditor.Upsert("a = 1\r\n", table, values);
        Assert.Equal("a = 1\r\n\r\n[mcp_servers.cutlass]\r\ncommand = \"C:\\\\x\\\\mcp.exe\"\r\nargs = [\"a b\", \"c\\\"d\"]\r\n", added);
        var read = TomlTableEditor.Read(added, table)!;
        Assert.Equal("C:\\x\\mcp.exe", read["command"]!.GetValue<string>());
        Assert.Equal(["a b", "c\"d"], read["args"]!.AsArray().Select(e => e!.GetValue<string>()));
        Assert.Equal("a = 1\r\n", TomlTableEditor.Remove(added, table, out var removed));
        Assert.True(removed);

        // Quoted header parts, sub-tables, comments after the header and values of every kind.
        const string text = "[ mcp_servers . \"cutlass\" ]  # ours\nenabled = true\ntimeout = 30\n\n[mcp_servers.cutlass.env]\nA = \"1\"\n\n[x]\ny = 'z'\n";
        var parsed = TomlTableEditor.Read(text, table)!;
        Assert.True(parsed["enabled"]!.GetValue<bool>());
        Assert.Equal(30L, parsed["timeout"]!.GetValue<long>());
        Assert.Equal("[x]\ny = 'z'\n", TomlTableEditor.Remove(text, table, out _));
        Assert.Null(TomlTableEditor.Read("[x]\ny = 1\n", table));
        Assert.Equal("[mcp_servers.cutlass]", TomlTableEditor.HeaderText(table));
        Assert.Equal("{ k = \"v\" }", TomlTableEditor.Format(new JsonObject { ["k"] = "v" }));
    }
}
