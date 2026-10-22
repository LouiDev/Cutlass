using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Cutlass.Agent.Mcp.Prompts;

/// <summary>
/// The MCP prompt (docs/AGENT_INTEGRATION.md 5.3): <c>cutlass_workflow</c> returns the server instructions as a user
/// message, for clients that surface prompts instead of server instructions.
/// </summary>
[McpServerPromptType]
public sealed class CutlassPrompts
{
    /// <summary>The workflow prompt.</summary>
    /// <returns>One user message holding <see cref="ServerInstructions.Text"/>.</returns>
    [McpServerPrompt(Name = "cutlass_workflow")]
    [Description("How to edit a video in Cutlass safely: status first, the edit lock, batches, frames and seconds, and releasing the lock.")]
    public static GetPromptResult Workflow() => new()
    {
        Description = "The rules for editing in Cutlass while its user may be watching.",
        Messages =
        [
            new PromptMessage
            {
                Role = Role.User,
                Content = new TextContentBlock { Text = ServerInstructions.Text },
            },
        ],
    };
}
