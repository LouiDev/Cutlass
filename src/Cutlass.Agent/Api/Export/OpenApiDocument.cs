using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Cutlass.Agent.Http;

namespace Cutlass.Agent.Api.Export;

/// <summary>
/// <c>GET /v1/openapi.json</c> (AGENT_INTEGRATION 9.2): an OpenAPI 3.1 document built from <see cref="ToolCatalog"/>,
/// so the description cannot drift from the routes.
/// <list type="bullet">
/// <item>One path item per route; <c>{name}</c> segments are required path parameters typed from the tool schema.</item>
/// <item>GET and DELETE take the other arguments as query parameters (arrays comma separated: <c>style: form,
/// explode: false</c>); POST takes them as a JSON body whose schema is <c>components.schemas.&lt;Name&gt;Args</c>.</item>
/// <item>Responses: the envelope (<c>Envelope</c>, or <c>MutationEnvelope</c> for edits), <c>image/png</c> for image
/// tools, <c>text/plain</c> for describe, the documents of the discovery routes, a <c>423</c> for lock routes and the
/// error envelope as <c>default</c>.</item>
/// <item><c>bearerAuth</c> applies globally; lock routes take the <c>X-Cutlass-Lock</c> header.</item>
/// <item>Vendor extensions: <c>x-cutlass-tool</c>, <c>x-cutlass-mcp-name</c>, <c>x-cutlass-needs-lock</c>,
/// <c>x-cutlass-mutating</c> and <c>x-cutlass-heavy</c>.</item>
/// <item><c>POST /v1/call</c>, which is not a catalog tool, is described by hand with <c>CallRequest</c>.</item>
/// </list>
/// Pure: the same catalog and version give the same bytes.
/// </summary>
public static class OpenApiDocument
{
    /// <summary>The OpenAPI version written.</summary>
    public const string OpenApiVersion = "3.1.0";

    private const string Json = "application/json";

    /// <summary>The document for the whole catalog.</summary>
    /// <param name="appVersion">The app version for <c>info.version</c>.</param>
    /// <returns>The JSON text.</returns>
    public static string Build(string appVersion) => Build(appVersion, ToolCatalog.All);

    /// <summary>The document for some tools.</summary>
    /// <param name="appVersion">The app version for <c>info.version</c>.</param>
    /// <param name="tools">The tools.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="InvalidOperationException">When two tools map to the same operation id or schema name.</exception>
    public static string Build(string appVersion, IEnumerable<ToolInfo> tools)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentNullException.ThrowIfNull(tools);
        var schemas = BaseSchemas();
        var paths = new JsonObject { [Router.CallRoute] = new JsonObject { ["post"] = CallOperation() } };
        var operationIds = new HashSet<string>(StringComparer.Ordinal) { "call" };
        var tags = new List<string> { "call" };
        foreach (var tool in tools)
        {
            var operationId = OperationId(tool);
            if (!operationIds.Add(operationId))
            {
                throw new InvalidOperationException($"Two tools have the operation id '{operationId}'.");
            }

            var tag = TagOf(tool);
            if (!tags.Contains(tag))
            {
                tags.Add(tag);
            }

            if (paths[tool.Route] is not JsonObject item)
            {
                item = [];
                paths[tool.Route] = item;
            }

            item[tool.Method.ToLowerInvariant()] = Operation(tool, operationId, tag, schemas);
        }

        var doc = new JsonObject
        {
            ["openapi"] = OpenApiVersion,
            ["info"] = new JsonObject
            {
                ["title"] = "Cutlass agent API",
                ["version"] = appVersion,
                ["summary"] = "Drive the Cutlass video editor from any agent or script over loopback HTTP.",
                ["description"] = "Every route needs the bearer token from the token file that endpoint.json names. Changes need the edit "
                    + "lock: call POST /v1/lock/acquire, then send its lockId as X-Cutlass-Lock. Replies are the envelope "
                    + "{revision, warnings, result} or {revision, error}. POST /v1/call runs any tool by name and is the simplest "
                    + "entry point for a harness. Timeline positions are integer frames; source times are flicks (705600000 per second).",
                ["x-cutlass-api-version"] = ApiVersion.Current,
                ["x-cutlass-api-revision"] = ApiVersion.Revision,
            },
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = "http://{host}:{port}",
                    ["description"] = "The loopback server; use baseUrl from endpoint.json.",
                    ["variables"] = new JsonObject
                    {
                        ["host"] = new JsonObject { ["default"] = "localhost", ["enum"] = new JsonArray("localhost", "127.0.0.1") },
                        ["port"] = new JsonObject { ["default"] = AgentHttpServer.DefaultPort.ToString(CultureInfo.InvariantCulture) },
                    },
                },
            },
            ["security"] = new JsonArray { new JsonObject { ["bearerAuth"] = new JsonArray() } },
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)new JsonObject { ["name"] = t }).ToArray()),
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["securitySchemes"] = new JsonObject
                {
                    ["bearerAuth"] = new JsonObject
                    {
                        ["type"] = "http",
                        ["scheme"] = "bearer",
                        ["description"] = "The token in <data>/agent/token (endpoint.json names the file as tokenFile).",
                    },
                },
                ["parameters"] = new JsonObject
                {
                    ["LockHeader"] = new JsonObject
                    {
                        ["name"] = ApiVersion.LockHeader,
                        ["in"] = "header",
                        ["required"] = true,
                        ["description"] = "The lockId from POST /v1/lock/acquire.",
                        ["schema"] = new JsonObject { ["type"] = "string" },
                    },
                    ["AgentHeader"] = new JsonObject
                    {
                        ["name"] = ApiVersion.AgentHeader,
                        ["in"] = "header",
                        ["required"] = false,
                        ["description"] = "Names the caller in the audit log before it holds a lock.",
                        ["schema"] = new JsonObject { ["type"] = "string" },
                    },
                },
                ["responses"] = new JsonObject
                {
                    ["Error"] = ErrorResponse("An error: the code, a sentence saying what to do next, details and, for busy, paused and rate_limited, retryAfterMs."),
                    ["LockError"] = ErrorResponse("lock_required (no valid lock) or lock_lost (revoked, expired or released; details.reason)."),
                },
                ["schemas"] = schemas,
            },
        };
        return ExportJson.Write(doc);
    }

    /// <summary>The operation id of a tool: its MCP name, or <c>cutlass_</c> and the catalog name in snake case.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>For example <c>cutlass_split</c> or <c>cutlass_media_peaks</c>.</returns>
    public static string OperationId(ToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool.McpName ?? "cutlass_" + tool.Name.Replace('.', '_').Replace('-', '_');
    }

    /// <summary>The component schema name of a POST tool's body: the catalog name in Pascal case plus <c>Args</c>.</summary>
    /// <param name="tool">The tool.</param>
    /// <returns>For example <c>EditTransitionAddArgs</c>.</returns>
    public static string ArgsSchemaName(ToolInfo tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var sb = new StringBuilder();
        foreach (var part in tool.Name.Split(['.', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ToUpperInvariant(part[0])).Append(part.AsSpan(1));
        }

        return sb.Append("Args").ToString();
    }

    private static string TagOf(ToolInfo tool)
    {
        var head = tool.Name.Split('.')[0];
        return head switch
        {
            "status" or "capabilities" or "lock" or "events" or "shutdown" => "session",
            "discovery" => "discovery",
            "project" => "project",
            "media" => "media",
            "frame" or "contact-sheet" or "audio" or "playhead" => "preview",
            "exports" => "export",
            _ => "edit",
        };
    }

    private static JsonObject Operation(ToolInfo tool, string operationId, string tag, JsonObject schemas)
    {
        var args = (JsonObject)JsonNode.Parse(tool.Schema)!;
        var properties = args["properties"] as JsonObject ?? [];
        var required = (args["required"] as JsonArray)?.Select(r => r!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        var parameters = new JsonArray();

        var pathNames = tool.Route.Split('/').Where(s => s.StartsWith('{') && s.EndsWith('}')).Select(s => s[1..^1]).ToList();
        foreach (var name in pathNames)
        {
            parameters.Add(Parameter(name, "path", true, properties[name] as JsonObject));
            properties.Remove(name);
        }

        if (args["required"] is JsonArray requiredArray)
        {
            foreach (var item in requiredArray.Where(r => pathNames.Contains(r!.GetValue<string>())).ToList())
            {
                requiredArray.Remove(item);
            }

            if (requiredArray.Count == 0)
            {
                args.Remove("required");
            }
        }

        if (tool.NeedsLock)
        {
            parameters.Add(new JsonObject { ["$ref"] = "#/components/parameters/LockHeader" });
        }

        parameters.Add(new JsonObject { ["$ref"] = "#/components/parameters/AgentHeader" });

        var op = new JsonObject
        {
            ["operationId"] = operationId,
            ["summary"] = tool.McpName ?? tool.Name,
            ["description"] = tool.Description,
            ["tags"] = new JsonArray(tag),
        };

        JsonObject? body = null;
        if (string.Equals(tool.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            var schemaName = ArgsSchemaName(tool);
            if (schemas.ContainsKey(schemaName))
            {
                throw new InvalidOperationException($"Two tools have the schema name '{schemaName}'.");
            }

            schemas[schemaName] = args;
            body = new JsonObject
            {
                ["required"] = args["required"] is JsonArray { Count: > 0 },
                ["content"] = new JsonObject { [Json] = new JsonObject { ["schema"] = Ref("schemas", schemaName) } },
            };
        }
        else
        {
            foreach (var (name, schema) in properties.ToList())
            {
                parameters.Add(Parameter(name, "query", required.Contains(name), schema as JsonObject));
            }
        }

        op["parameters"] = parameters;
        if (body is not null)
        {
            op["requestBody"] = body;
        }

        var responses = new JsonObject { ["200"] = Success(tool) };
        if (tool.NeedsLock)
        {
            responses["423"] = Ref("responses", "LockError");
        }

        responses["default"] = Ref("responses", "Error");
        op["responses"] = responses;
        op["x-cutlass-tool"] = tool.Name;
        if (tool.McpName is { } mcp)
        {
            op["x-cutlass-mcp-name"] = mcp;
        }

        op["x-cutlass-needs-lock"] = tool.NeedsLock;
        op["x-cutlass-mutating"] = tool.Mutating;
        op["x-cutlass-heavy"] = tool.Heavy;
        return op;
    }

    private static JsonObject Parameter(string name, string location, bool required, JsonObject? schema)
    {
        var copy = schema is null ? new JsonObject { ["type"] = "string" } : (JsonObject)schema.DeepClone();
        var description = copy["description"]?.GetValue<string>();
        copy.Remove("description");
        var parameter = new JsonObject { ["name"] = name, ["in"] = location, ["required"] = required };
        if (description is not null)
        {
            parameter["description"] = description;
        }

        parameter["schema"] = copy;
        if (copy["type"]?.GetValueKind() == System.Text.Json.JsonValueKind.String && copy["type"]!.GetValue<string>() == "array")
        {
            parameter["style"] = "form";
            parameter["explode"] = false;
        }

        return parameter;
    }

    private static JsonObject Success(ToolInfo tool)
    {
        var content = new JsonObject();
        string description;
        var headers = (JsonObject?)null;
        switch (tool.Result)
        {
            case ToolResultKind.Png:
                description = "The PNG, or with format=base64 (the default through /v1/call) the envelope with pngBase64 in result.";
                content["image/png"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "string", ["contentMediaType"] = "image/png" } };
                content[Json] = new JsonObject { ["schema"] = Ref("schemas", "Envelope") };
                if (tool.Name == "contact-sheet")
                {
                    headers = new JsonObject
                    {
                        [ApiVersion.LayoutHeader] = new JsonObject
                        {
                            ["description"] = "The cell layout as JSON [{frame, timecode, x, y}] next to the PNG.",
                            ["schema"] = new JsonObject { ["type"] = "string" },
                        },
                    };
                }

                break;
            case ToolResultKind.Text:
                description = "The text (through /v1/call: the envelope with result.text).";
                content["text/plain"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "string" } };
                break;
            case ToolResultKind.Document when tool.Name == "discovery.tools":
                description = "The catalog {tools: [...]}, a vendor tool list (format openai, anthropic or gemini), or Markdown.";
                content[Json] = new JsonObject { ["schema"] = new JsonObject { ["type"] = new JsonArray("object", "array") } };
                content["text/markdown"] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "string" } };
                break;
            case ToolResultKind.Document when tool.Name == "discovery.instructions":
                description = "The workflow rules and the call contract.";
                content[Json] = new JsonObject { ["schema"] = Ref("schemas", "Instructions") };
                break;
            case ToolResultKind.Document:
                description = "The document.";
                content[Json] = new JsonObject { ["schema"] = new JsonObject { ["type"] = "object" } };
                break;
            default:
                description = tool.Mutating ? "The mutation envelope." : "The envelope.";
                content[Json] = new JsonObject { ["schema"] = Ref("schemas", tool.Mutating ? "MutationEnvelope" : "Envelope") };
                break;
        }

        var response = new JsonObject { ["description"] = description };
        if (headers is not null)
        {
            response["headers"] = headers;
        }

        response["content"] = content;
        return response;
    }

    private static JsonObject CallOperation() => new()
    {
        ["operationId"] = "call",
        ["summary"] = "Run any tool by name",
        ["description"] = "Runs a tool by its MCP name (cutlass_split) or catalog name (edit.split) with its arguments. The reply is always "
            + "the envelope: image tools answer with pngBase64 in result, describe with result.text. Lock tools need X-Cutlass-Lock.",
        ["tags"] = new JsonArray("call"),
        ["parameters"] = new JsonArray
        {
            new JsonObject
            {
                ["name"] = ApiVersion.LockHeader,
                ["in"] = "header",
                ["required"] = false,
                ["description"] = "The lockId; required by tools that need the lock.",
                ["schema"] = new JsonObject { ["type"] = "string" },
            },
            Ref("parameters", "AgentHeader"),
        },
        ["requestBody"] = new JsonObject
        {
            ["required"] = true,
            ["content"] = new JsonObject { [Json] = new JsonObject { ["schema"] = Ref("schemas", "CallRequest") } },
        },
        ["responses"] = new JsonObject
        {
            ["200"] = new JsonObject
            {
                ["description"] = "The envelope of the tool.",
                ["content"] = new JsonObject { [Json] = new JsonObject { ["schema"] = Ref("schemas", "MutationEnvelope") } },
            },
            ["423"] = Ref("responses", "LockError"),
            ["default"] = Ref("responses", "Error"),
        },
    };

    private static JsonObject ErrorResponse(string description) => new()
    {
        ["description"] = description,
        ["headers"] = new JsonObject
        {
            ["Retry-After"] = new JsonObject
            {
                ["description"] = "Seconds to wait, when the error has retryAfterMs.",
                ["schema"] = new JsonObject { ["type"] = "integer" },
            },
        },
        ["content"] = new JsonObject { [Json] = new JsonObject { ["schema"] = Ref("schemas", "ErrorEnvelope") } },
    };

    private static JsonObject BaseSchemas() => new()
    {
        ["Envelope"] = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "A successful reply.",
            ["required"] = new JsonArray("revision", "warnings"),
            ["properties"] = new JsonObject
            {
                ["revision"] = new JsonObject { ["type"] = "integer", ["description"] = "The project revision after the call." },
                ["warnings"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["description"] = "Clamps and notes; empty when the call did exactly what was asked.",
                },
                ["result"] = new JsonObject { ["description"] = "The tool's result." },
            },
        },
        ["MutationEnvelope"] = new JsonObject
        {
            ["description"] = "The reply of an edit: the envelope plus what changed.",
            ["allOf"] = new JsonArray
            {
                Ref("schemas", "Envelope"),
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["changed"] = new JsonObject { ["type"] = "boolean", ["description"] = "False when the edit clamped to nothing." },
                        ["select"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "Ids selected after the edit." },
                        ["undoLabel"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["description"] = "What Undo would revert now." },
                        ["project"] = new JsonObject { ["type"] = "object", ["description"] = "The project summary, with returnProject." },
                    },
                },
            },
        },
        ["Error"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("code", "message"),
            ["properties"] = new JsonObject
            {
                ["code"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(Enum.GetValues<ErrorCode>().Select(c => (JsonNode?)ApiError.WireName(c)).ToArray()),
                },
                ["message"] = new JsonObject { ["type"] = "string", ["description"] = "What happened and what to do next." },
                ["details"] = new JsonObject { ["type"] = new JsonArray("object", "null"), ["additionalProperties"] = new JsonObject { ["type"] = "string" } },
                ["retryAfterMs"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
            },
        },
        ["ErrorEnvelope"] = new JsonObject
        {
            ["type"] = "object",
            ["description"] = "A failed reply.",
            ["required"] = new JsonArray("revision", "error"),
            ["properties"] = new JsonObject
            {
                ["revision"] = new JsonObject { ["type"] = "integer" },
                ["error"] = Ref("schemas", "Error"),
            },
        },
        ["CallRequest"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("tool"),
            ["properties"] = new JsonObject
            {
                ["tool"] = new JsonObject { ["type"] = "string", ["description"] = "The MCP name (cutlass_split) or the catalog name (edit.split)." },
                ["args"] = new JsonObject { ["type"] = "object", ["description"] = "The tool's arguments, as its schema in GET /v1/tools." },
            },
        },
        ["Instructions"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("text"),
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
        },
    };

    private static JsonObject Ref(string section, string name) => new() { ["$ref"] = "#/components/" + section + "/" + name };
}
