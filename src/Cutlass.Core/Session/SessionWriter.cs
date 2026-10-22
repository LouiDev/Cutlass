namespace Cutlass.Core.Session;

/// <summary>Who is writing to a <see cref="ProjectSession"/> (AGENT_INTEGRATION 2.6).</summary>
public enum SessionWriter
{
    /// <summary>The person at the keyboard (the default).</summary>
    User,

    /// <summary>An agent, inside <see cref="ProjectSession.WriteAs"/>.</summary>
    Agent,
}
