namespace FortressSouls.Llm;

using System.Text.Json;
using FortressSouls.Application;
using Microsoft.Extensions.AI;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

public sealed class FakeToolLoopChatClient : IChatClient
{
    public const string ProviderTypeName = "Fake";
    public const string ModelName = "fake-dwarf";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messageList = messages.ToArray();
        var availableToolNames = GetAvailableToolNames(options);
        var latestUserMessage = messageList.LastOrDefault(message => message.Role == AiChatRole.User)?.Text ?? string.Empty;

        var latestToolResult = messageList
            .Reverse()
            .FirstOrDefault(message => message.Role == AiChatRole.Tool);

        if (latestToolResult is null)
        {
            if (availableToolNames.Contains(FakePerceptionToolService.ListDwarvesToolName)
                && availableToolNames.Contains(FakePerceptionToolService.InspectDwarfToolName)
                && LooksLikeOtherDwarfRequest(latestUserMessage))
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(
                    AiChatRole.Assistant,
                    [new FunctionCallContent(
                        "call-1",
                        FakePerceptionToolService.ListDwarvesToolName,
                        new Dictionary<string, object?>())])));
            }

            if (availableToolNames.Contains(FakePerceptionToolService.LookAroundToolName)
                && LooksLikeLookAroundRequest(latestUserMessage))
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(
                    AiChatRole.Assistant,
                    [new FunctionCallContent(
                        "call-1",
                        FakePerceptionToolService.LookAroundToolName,
                        new Dictionary<string, object?>
                        {
                            ["radius"] = 1
                        })])));
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                "I cannot inspect further just now.")));
        }

        var resultContent = latestToolResult.Contents.OfType<FunctionResultContent>().LastOrDefault();
        if (resultContent is null || !TryGetObservation(resultContent, out var observation))
        {
            throw new LlmProviderException(LlmProviderErrorCode.InvalidResponse, "The chat provider response was malformed.");
        }

        observation = UnwrapObservation(observation);

        if (observation.ValueKind == JsonValueKind.Object
            && observation.TryGetProperty("outcome", out var outcomeProperty)
            && outcomeProperty.ValueKind == JsonValueKind.String)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I could not get a clear answer just now.")));
        }

        if (observation.ValueKind == JsonValueKind.Object
            && observation.TryGetProperty("dwarves", out _))
        {
            var listResult = Deserialize<ListDwarvesToolResult>(observation);
            var dwarfId = SelectInspectableDwarfId(listResult, latestUserMessage);
            if (string.IsNullOrWhiteSpace(dwarfId))
            {
                return Task.FromResult(new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I cannot place another dwarf just now.")));
            }

            return Task.FromResult(new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-2",
                    FakePerceptionToolService.InspectDwarfToolName,
                    new Dictionary<string, object?>
                    {
                        ["dwarfId"] = dwarfId
                    })])));
        }

        if (observation.ValueKind == JsonValueKind.Object
            && observation.TryGetProperty("identity", out _))
        {
            var inspectedDwarf = Deserialize<InspectDwarfToolResult>(observation);
            return Task.FromResult(new ChatResponse(new ChatMessage(AiChatRole.Assistant, BuildGroundedReply(inspectedDwarf))));
        }

        if (observation.ValueKind == JsonValueKind.Object
            && observation.TryGetProperty("cells", out _)
            && observation.TryGetProperty("bounds", out _))
        {
            var lookAround = Deserialize<LookAroundToolResult>(observation);
            return Task.FromResult(new ChatResponse(new ChatMessage(AiChatRole.Assistant, BuildGroundedReply(lookAround))));
        }

        throw new LlmProviderException(LlmProviderErrorCode.InvalidResponse, "The chat provider response was malformed.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    private static HashSet<string> GetAvailableToolNames(ChatOptions? options) =>
        options?.Tools?.OfType<AIFunctionDeclaration>().Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal)
        ?? [];

    private static bool LooksLikeLookAroundRequest(string normalizedMessage)
    {
        var words = ExtractWords(normalizedMessage);
        var mentionsLocalSurroundings = words.Contains("around")
            || words.Contains("nearby")
            || words.Any(word => word.StartsWith("surround", StringComparison.Ordinal));

        if (!mentionsLocalSurroundings)
        {
            return false;
        }

        return (words.Contains("look") && words.Contains("around"))
            || words.Contains("see")
            || words.Contains("observe");
    }

    private static bool LooksLikeOtherDwarfRequest(string normalizedMessage)
    {
        var words = ExtractWords(normalizedMessage);
        var mentionsAnotherDwarf = (words.Contains("another") || words.Contains("other")) && words.Contains("dwarf");
        var mentionsSomeoneElse = words.Contains("someone") && words.Contains("else");

        if (!mentionsAnotherDwarf && !mentionsSomeoneElse)
        {
            return false;
        }

        return words.Contains("about")
            || words.Contains("tell")
            || words.Contains("who")
            || words.Contains("what")
            || words.Contains("inspect")
            || words.Contains("check");
    }

    private static HashSet<string> ExtractWords(string value)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var currentWord = new System.Text.StringBuilder();

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                currentWord.Append(char.ToLowerInvariant(character));
                continue;
            }

            FlushWord(currentWord, words);
        }

        FlushWord(currentWord, words);
        return words;
    }

    private static void FlushWord(System.Text.StringBuilder currentWord, HashSet<string> words)
    {
        if (currentWord.Length == 0)
        {
            return;
        }

        words.Add(currentWord.ToString());
        currentWord.Clear();
    }

    private static T Deserialize<T>(JsonElement observation)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(observation.GetRawText(), SerializerOptions);
            return value is null
                ? throw new LlmProviderException(LlmProviderErrorCode.InvalidResponse, "The chat provider response was malformed.")
                : value;
        }
        catch (JsonException exception)
        {
            throw new LlmProviderException(LlmProviderErrorCode.InvalidResponse, "The chat provider response was malformed.", exception);
        }
    }

    private static bool TryGetObservation(FunctionResultContent resultContent, out JsonElement observation)
    {
        switch (resultContent.Result)
        {
            case JsonElement element:
                observation = element;
                return true;
            case JsonDocument document:
                observation = document.RootElement.Clone();
                return true;
            case string json:
                try
                {
                    using var parsed = JsonDocument.Parse(json);
                    observation = parsed.RootElement.Clone();
                    return true;
                }
                catch (JsonException)
                {
                    observation = default;
                    return false;
                }
            default:
                try
                {
                    observation = JsonSerializer.SerializeToElement(resultContent.Result, SerializerOptions);
                    return true;
                }
                catch (Exception) when (resultContent.Result is not null)
                {
                    observation = default;
                    return false;
                }
        }
    }

    private static JsonElement UnwrapObservation(JsonElement observation)
    {
        if (observation.ValueKind != JsonValueKind.String)
        {
            return observation;
        }

        var json = observation.GetString();
        if (string.IsNullOrWhiteSpace(json))
        {
            return observation;
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.Clone();
        }
        catch (JsonException)
        {
            return observation;
        }
    }

    private static string? SelectInspectableDwarfId(ListDwarvesToolResult listResult, string userMessage)
    {
        var words = ExtractWords(userMessage);

        var namedMatch = listResult.Dwarves.FirstOrDefault(dwarf =>
            ExtractWords(dwarf.DisplayName).Any(words.Contains));
        if (namedMatch is not null)
        {
            return namedMatch.DwarfId;
        }

        return listResult.Dwarves.Skip(1).FirstOrDefault()?.DwarfId
            ?? listResult.Dwarves.FirstOrDefault()?.DwarfId;
    }

    private static string BuildGroundedReply(LookAroundToolResult lookAround)
    {
        var visibleTerrain = lookAround.Cells
            .Where(cell => string.Equals(cell.Visibility, "visible", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(cell.TerrainClass))
            .Select(cell => cell.TerrainClass!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var visibleUnits = lookAround.Cells
            .Where(cell => string.Equals(cell.Visibility, "visible", StringComparison.Ordinal))
            .Sum(cell => cell.UnitCount ?? 0);
        var terrainText = visibleTerrain.Length switch
        {
            0 => "little worth naming",
            1 => visibleTerrain[0],
            2 => $"{visibleTerrain[0]} and {visibleTerrain[1]}",
            _ => $"{string.Join(", ", visibleTerrain[..^1])}, and {visibleTerrain[^1]}"
        };

        if (visibleUnits > 0)
        {
            return $"I can see {terrainText} nearby, and I count {visibleUnits} visible units.";
        }

        return $"I can see {terrainText} nearby.";
    }

    private static string BuildGroundedReply(InspectDwarfToolResult inspectedDwarf)
    {
        var name = inspectedDwarf.Identity.ReadableName;
        var profession = inspectedDwarf.Identity.ProfessionName;
        var work = string.IsNullOrWhiteSpace(inspectedDwarf.Work.CurrentJobType)
            ? "between tasks"
            : $"busy with {inspectedDwarf.Work.CurrentJobType}";

        if (inspectedDwarf.StrongNeeds.Any(need => string.Equals(need.Token, "BeWithFamily", StringComparison.Ordinal)))
        {
            return $"{name}, a {profession}, is {work}; the pull of family is plain enough.";
        }

        return $"{name}, a {profession}, is {work}.";
    }
}