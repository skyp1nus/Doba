namespace Doba.Bridge;

/// <summary>One MCP tool as Doba.Mcp lists it and Doba executes it.</summary>
/// <param name="Name">The tool name (<c>^[a-z_]+$</c>).</param>
/// <param name="Title">Human-readable name; also <c>annotations.title</c>.</param>
/// <param name="Description">What the model reads: what the tool does, formats and limits.</param>
/// <param name="InputSchemaJson">JSON Schema of the arguments: an object with <c>additionalProperties: false</c>.</param>
/// <param name="ReadOnly"><c>readOnlyHint</c>: the tool never changes anything.</param>
/// <param name="Destructive"><c>destructiveHint</c>: the tool may overwrite or remove data.</param>
/// <param name="Idempotent"><c>idempotentHint</c>: repeating the call with the same arguments changes nothing more.</param>
public sealed record ToolDefinition(
    string Name,
    string Title,
    string Description,
    string InputSchemaJson,
    bool ReadOnly,
    bool Destructive,
    bool Idempotent)
{
    /// <summary>True when Claude needs the "Read &amp; edit" access level for this tool.</summary>
    public bool RequiresWrite => !ReadOnly;
}
