namespace FortressSouls.Prompting;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FortressSouls.Observability;

public sealed class PromptAssembler
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public PromptAssemblyResult Assemble(PromptInputs inputs, PromptAssemblyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        options ??= PromptAssemblyOptions.Default;

        using var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(
            FortressSoulsTelemetry.PromptAssembleActivityName,
            ActivityKind.Internal);

        activity?.SetTag(FortressSoulsTelemetry.PromptTemplateVersionTagName, PromptContract.TemplateVersion);

        PromptAssemblyResult result;
        try
        {
            result = AssembleCore(inputs, options);
        }
        catch
        {
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
            throw;
        }

        var outcome = result.Succeeded ? FortressSoulsTelemetry.SuccessOutcome : FortressSoulsTelemetry.ErrorOutcome;
        activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, outcome);

        FortressSoulsTelemetry.RecordPromptTokensEstimated(
            result.Diagnostics.EstimatedTokenCount,
            PromptContract.TemplateVersion,
            result.Diagnostics.Truncation.Any,
            outcome);

        return result;
    }

    public PromptAssemblyResult AssembleAgentTurn(AgentPromptInputs inputs, PromptAssemblyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        options ??= PromptAssemblyOptions.Default;

        using var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(
            FortressSoulsTelemetry.PromptAssembleActivityName,
            ActivityKind.Internal);

        activity?.SetTag(FortressSoulsTelemetry.PromptTemplateVersionTagName, PromptContract.TemplateVersion);

        PromptAssemblyResult result;
        try
        {
            result = AssembleAgentTurnCore(inputs, options);
        }
        catch
        {
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
            throw;
        }

        var outcome = result.Succeeded ? FortressSoulsTelemetry.SuccessOutcome : FortressSoulsTelemetry.ErrorOutcome;
        activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, outcome);

        FortressSoulsTelemetry.RecordPromptTokensEstimated(
            result.Diagnostics.EstimatedTokenCount,
            PromptContract.TemplateVersion,
            result.Diagnostics.Truncation.Any,
            outcome);

        return result;
    }

    private static PromptAssemblyResult AssembleCore(PromptInputs inputs, PromptAssemblyOptions options)
    {
        if (inputs.Snapshot is null)
        {
            return CreateValidationFailure();
        }

        if (options.MaxPromptCharacters <= 0
            || options.MaxConversationMessages < 0
            || options.MaxConversationMessageCharacters <= 0
            || options.MaxPlayerMessageCharacters <= 0
            || options.MaxStaticGuideCharacters <= 0)
        {
            return CreateValidationFailure();
        }

        if (inputs.PlayerMessage is null)
        {
            return CreateValidationFailure();
        }

        var staticGuide = inputs.StaticInterpretationGuide ?? PromptContract.DefaultStaticInterpretationGuide;
        var normalizedGuide = NormalizeNewlines(staticGuide);
        var normalizedPlayerMessage = NormalizeNewlines(inputs.PlayerMessage);

        if (string.IsNullOrWhiteSpace(normalizedPlayerMessage))
        {
            return CreateValidationFailure();
        }

        var guideTruncated = false;
        var playerMessageTruncated = false;
        var conversationMessageTextTruncated = false;
        var conversationMessagesDroppedForCount = false;
        var conversationMessagesDroppedForBudget = false;

        normalizedGuide = Truncate(normalizedGuide, options.MaxStaticGuideCharacters, ref guideTruncated);
        normalizedPlayerMessage = Truncate(normalizedPlayerMessage, options.MaxPlayerMessageCharacters, ref playerMessageTruncated);

        var normalizedConversation = new List<PromptConversationPayload>();
        var sourceConversation = inputs.Conversation ?? [];
        foreach (var message in sourceConversation)
        {
            if (message is null || message.Text is null)
            {
                return CreateValidationFailure();
            }

            if (!TryNormalizeConversationRole(message.Role, out var normalizedRole))
            {
                return CreateValidationFailure();
            }

            var messageText = NormalizeNewlines(message.Text);
            messageText = Truncate(messageText, options.MaxConversationMessageCharacters, ref conversationMessageTextTruncated);
            normalizedConversation.Add(new PromptConversationPayload(normalizedRole, messageText));
        }

        if (normalizedConversation.Count > options.MaxConversationMessages)
        {
            var skipCount = normalizedConversation.Count - options.MaxConversationMessages;
            normalizedConversation = [.. normalizedConversation.Skip(skipCount)];
            conversationMessagesDroppedForCount = true;
        }

        var snapshotJson = JsonSerializer.Serialize(BuildPromptDwarfState(inputs.Snapshot), SerializerOptions);
        var conversationJson = JsonSerializer.Serialize(normalizedConversation, SerializerOptions);
        var playerJson = JsonSerializer.Serialize(new PromptPlayerPayload(normalizedPlayerMessage), SerializerOptions);

        var promptText = BuildPrompt(snapshotJson, normalizedGuide, conversationJson, playerJson);
        while (promptText.Length > options.MaxPromptCharacters && normalizedConversation.Count > 0)
        {
            normalizedConversation.RemoveAt(0);
            conversationMessagesDroppedForBudget = true;
            conversationJson = JsonSerializer.Serialize(normalizedConversation, SerializerOptions);
            promptText = BuildPrompt(snapshotJson, normalizedGuide, conversationJson, playerJson);
        }

        var truncation = new PromptTruncationInfo(
            ConversationMessagesDroppedForCount: conversationMessagesDroppedForCount,
            ConversationMessagesDroppedForBudget: conversationMessagesDroppedForBudget,
            ConversationMessageTextTruncated: conversationMessageTextTruncated,
            PlayerMessageTruncated: playerMessageTruncated,
            StaticGuideTruncated: guideTruncated);

        if (promptText.Length > options.MaxPromptCharacters)
        {
            return CreateFailure(
                PromptAssemblyFailureCategory.PromptTooLarge,
                truncation,
                promptText.Length,
                normalizedConversation.Count);
        }

        var diagnostics = CreateDiagnostics(PromptAssemblyFailureCategory.None, truncation, promptText.Length, normalizedConversation.Count);
        return new PromptAssemblyResult(promptText, diagnostics);
    }

    private static PromptAssemblyResult AssembleAgentTurnCore(AgentPromptInputs inputs, PromptAssemblyOptions options)
    {
        if (inputs.Snapshot is null
            || inputs.PlayerMessage is null
            || inputs.EnabledTools is null
            || inputs.EnabledTools.Count == 0)
        {
            return CreateValidationFailure();
        }

        if (options.MaxPromptCharacters <= 0
            || options.MaxConversationMessages < 0
            || options.MaxConversationMessageCharacters <= 0
            || options.MaxPlayerMessageCharacters <= 0
            || options.MaxStaticGuideCharacters <= 0)
        {
            return CreateValidationFailure();
        }

        var staticGuide = inputs.StaticInterpretationGuide ?? PromptContract.DefaultStaticInterpretationGuide;
        var normalizedGuide = NormalizeNewlines(staticGuide);
        var normalizedPlayerMessage = NormalizeNewlines(inputs.PlayerMessage);

        if (string.IsNullOrWhiteSpace(normalizedPlayerMessage))
        {
            return CreateValidationFailure();
        }

        var guideTruncated = false;
        var playerMessageTruncated = false;
        var conversationMessageTextTruncated = false;
        var conversationMessagesDroppedForCount = false;
        var conversationMessagesDroppedForBudget = false;

        normalizedGuide = Truncate(normalizedGuide, options.MaxStaticGuideCharacters, ref guideTruncated);
        normalizedPlayerMessage = Truncate(normalizedPlayerMessage, options.MaxPlayerMessageCharacters, ref playerMessageTruncated);

        var normalizedConversation = new List<PromptConversationPayload>();
        var sourceConversation = inputs.Conversation ?? [];
        foreach (var message in sourceConversation)
        {
            if (message is null || message.Text is null)
            {
                return CreateValidationFailure();
            }

            if (!TryNormalizeConversationRole(message.Role, out var normalizedRole))
            {
                return CreateValidationFailure();
            }

            var messageText = NormalizeNewlines(message.Text);
            messageText = Truncate(messageText, options.MaxConversationMessageCharacters, ref conversationMessageTextTruncated);
            normalizedConversation.Add(new PromptConversationPayload(normalizedRole, messageText));
        }

        if (normalizedConversation.Count > options.MaxConversationMessages)
        {
            var skipCount = normalizedConversation.Count - options.MaxConversationMessages;
            normalizedConversation = [.. normalizedConversation.Skip(skipCount)];
            conversationMessagesDroppedForCount = true;
        }

        var orderedTools = inputs.EnabledTools
            .Select(tool => tool is null
                || string.IsNullOrWhiteSpace(tool.Name)
                || string.IsNullOrWhiteSpace(tool.ArgumentsSchemaVersion)
                || string.IsNullOrWhiteSpace(tool.ResultSchemaVersion)
                    ? null
                    : new AgentToolPromptPayload(
                        tool.Name,
                        tool.ArgumentsSchemaVersion,
                        tool.ResultSchemaVersion))
            .OrderBy(tool => tool?.Tool, StringComparer.Ordinal)
            .ToArray();

        if (orderedTools.Any(tool => tool is null))
        {
            return CreateValidationFailure();
        }

        var snapshotJson = JsonSerializer.Serialize(BuildPromptDwarfState(inputs.Snapshot), SerializerOptions);
        var conversationJson = JsonSerializer.Serialize(normalizedConversation, SerializerOptions);
        var playerJson = JsonSerializer.Serialize(new PromptPlayerPayload(normalizedPlayerMessage), SerializerOptions);
        var toolsJson = JsonSerializer.Serialize(orderedTools!, SerializerOptions);
        var promptText = BuildAgentPrompt(
            snapshotJson,
            normalizedGuide,
            conversationJson,
            playerJson,
            string.Join(", ", orderedTools!.Select(tool => tool!.Tool)),
            toolsJson);

        while (promptText.Length > options.MaxPromptCharacters && normalizedConversation.Count > 0)
        {
            normalizedConversation.RemoveAt(0);
            conversationMessagesDroppedForBudget = true;
            conversationJson = JsonSerializer.Serialize(normalizedConversation, SerializerOptions);
            promptText = BuildAgentPrompt(
                snapshotJson,
                normalizedGuide,
                conversationJson,
                playerJson,
                string.Join(", ", orderedTools!.Select(tool => tool!.Tool)),
                toolsJson);
        }

        var truncation = new PromptTruncationInfo(
            ConversationMessagesDroppedForCount: conversationMessagesDroppedForCount,
            ConversationMessagesDroppedForBudget: conversationMessagesDroppedForBudget,
            ConversationMessageTextTruncated: conversationMessageTextTruncated,
            PlayerMessageTruncated: playerMessageTruncated,
            StaticGuideTruncated: guideTruncated);

        if (promptText.Length > options.MaxPromptCharacters)
        {
            return CreateFailure(
                PromptAssemblyFailureCategory.PromptTooLarge,
                truncation,
                promptText.Length,
                normalizedConversation.Count);
        }

        var diagnostics = CreateDiagnostics(PromptAssemblyFailureCategory.None, truncation, promptText.Length, normalizedConversation.Count);
        return new PromptAssemblyResult(promptText, diagnostics);
    }

    private static string BuildPrompt(string snapshotJson, string normalizedGuide, string conversationJson, string playerJson)
    {
        var normalizedSystemInstruction = NormalizeNewlines(PromptContract.SystemInstruction);
        var builder = new StringBuilder(capacity: snapshotJson.Length + normalizedGuide.Length + conversationJson.Length + playerJson.Length + 512);
        builder.Append("TEMPLATE_VERSION: ").Append(PromptContract.TemplateVersion).Append('\n');
        builder.Append("STATIC_GUIDE_VERSION: ").Append(PromptContract.StaticGuideVersion).Append('\n');
        builder.Append('\n');
        builder.Append("SYSTEM:\n").Append(normalizedSystemInstruction).Append('\n');
        builder.Append("DWARF_STATE_JSON:\n").Append(snapshotJson).Append('\n');
        builder.Append("INTERPRETATION_GUIDE:\n").Append(normalizedGuide).Append('\n');
        builder.Append("CONVERSATION_JSON:\n").Append(conversationJson).Append('\n');
        builder.Append("PLAYER_MESSAGE_JSON:\n").Append(playerJson).Append('\n');
        return builder.ToString();
    }

    private static string BuildAgentPrompt(
        string snapshotJson,
        string normalizedGuide,
        string conversationJson,
        string playerJson,
        string enabledTools,
        string toolsJson)
    {
        var normalizedSystemInstruction = NormalizeNewlines(PromptContract.SystemInstruction);
        var normalizedToolInstructions = NormalizeNewlines(PromptContract.ToolInstructionBlock);
        var builder = new StringBuilder(capacity: snapshotJson.Length + normalizedGuide.Length + conversationJson.Length + playerJson.Length + toolsJson.Length + 640);
        builder.Append("TEMPLATE_VERSION: ").Append(PromptContract.TemplateVersion).Append('\n');
        builder.Append("STATIC_GUIDE_VERSION: ").Append(PromptContract.StaticGuideVersion).Append('\n');
        builder.Append("TOOL_INSTRUCTION_VERSION: ").Append(PromptContract.ToolInstructionVersion).Append('\n');
        builder.Append("ENABLED_TOOLS: ").Append(enabledTools).Append('\n');
        builder.Append('\n');
        builder.Append("SYSTEM:\n").Append(normalizedSystemInstruction).Append('\n');
        builder.Append("AGENT_TOOL_RULES:\n").Append(normalizedToolInstructions).Append('\n');
        builder.Append("TOOL_SCHEMAS_JSON:\n").Append(toolsJson).Append('\n');
        builder.Append("DWARF_STATE_JSON:\n").Append(snapshotJson).Append('\n');
        builder.Append("INTERPRETATION_GUIDE:\n").Append(normalizedGuide).Append('\n');
        builder.Append("CONVERSATION_JSON:\n").Append(conversationJson).Append('\n');
        builder.Append("PLAYER_MESSAGE_JSON:\n").Append(playerJson).Append('\n');
        return builder.ToString();
    }

    private static PromptDwarfStatePayload BuildPromptDwarfState(FortressSouls.Domain.DwarfSnapshot snapshot) =>
        new(
            SchemaVersion: snapshot.SchemaVersion,
            DwarfId: snapshot.RequestedDwarfId.ToString(),
            DisplayName: snapshot.Identity.ReadableName,
            Profession: snapshot.Identity.ProfessionName,
            CurrentJob: snapshot.Work.CurrentJobType,
            Stress: new PromptStressPayload(
                Raw: snapshot.Stress.Raw,
                Category: snapshot.Stress.Category,
                CategoryScale: snapshot.Stress.CategoryScale),
            TopSkills: [.. snapshot.PromptCandidates.TopSkills.Select(skill => new PromptSkillSummaryPayload(
                Token: skill.Token,
                Effective: skill.Effective,
                TotalExperience: skill.TotalExperience))],
            ExtremeTraits: [.. snapshot.PromptCandidates.ExtremeTraits.Select(trait => new PromptTraitSummaryPayload(
                Token: trait.Token,
                Value: trait.Value,
                DeviationFromNeutral50: trait.DeviationFromNeutral50))],
            StrongValues: [.. snapshot.PromptCandidates.StrongValues.Select(value => new PromptValueSummaryPayload(
                Token: value.Token,
                Strength: value.Strength))],
            StrongNeeds: [.. snapshot.PromptCandidates.StrongNeeds.Select(need => new PromptNeedSummaryPayload(
                Token: need.Token,
                FocusLevel: need.FocusLevel,
                NeedLevel: need.NeedLevel,
                IsUnmet: need.IsUnmet,
                IsDeeplyUnmet: need.IsDeeplyUnmet))],
            Mannerisms: [.. snapshot.PromptCandidates.Mannerisms.Select(mannerism => new PromptMannerismSummaryPayload(
                Token: mannerism.Token,
                SituationToken: mannerism.SituationToken))]);

    private static PromptAssemblyResult CreateFailure(
        PromptAssemblyFailureCategory category,
        PromptTruncationInfo truncation,
        int estimatedCharacterCount,
        int conversationMessagesIncluded)
    {
        var diagnostics = CreateDiagnostics(category, truncation, estimatedCharacterCount, conversationMessagesIncluded);
        return new PromptAssemblyResult(null, diagnostics);
    }

    private static PromptAssemblyResult CreateValidationFailure() =>
        CreateFailure(
            PromptAssemblyFailureCategory.ValidationError,
            new PromptTruncationInfo(false, false, false, false, false),
            estimatedCharacterCount: 0,
            conversationMessagesIncluded: 0);

    private static PromptAssemblyDiagnostics CreateDiagnostics(
        PromptAssemblyFailureCategory failureCategory,
        PromptTruncationInfo truncation,
        int estimatedCharacterCount,
        int conversationMessagesIncluded) =>
        new(
            TemplateVersion: PromptContract.TemplateVersion,
            EstimatedCharacterCount: estimatedCharacterCount,
            EstimatedTokenCount: EstimateTokens(estimatedCharacterCount),
            ConversationMessagesIncluded: conversationMessagesIncluded,
            Truncation: truncation,
            FailureCategory: failureCategory);

    private static int EstimateTokens(int characterCount) =>
        characterCount <= 0 ? 0 : (characterCount + 3) / 4;

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private static string Truncate(string input, int maxLength, ref bool truncated)
    {
        if (input.Length <= maxLength)
        {
            return input;
        }

        truncated = true;
        return input[..maxLength];
    }

    private static bool TryNormalizeConversationRole(PromptMessageRole role, out string normalizedRole)
    {
        switch (role)
        {
            case PromptMessageRole.Player:
                normalizedRole = "player";
                return true;
            case PromptMessageRole.Assistant:
                normalizedRole = "assistant";
                return true;
            default:
                normalizedRole = string.Empty;
                return false;
        }
    }

    private sealed record PromptConversationPayload(
        string Role,
        string Text);

    private sealed record PromptDwarfStatePayload(
        string SchemaVersion,
        string DwarfId,
        string DisplayName,
        string Profession,
        string? CurrentJob,
        PromptStressPayload Stress,
        IReadOnlyList<PromptSkillSummaryPayload> TopSkills,
        IReadOnlyList<PromptTraitSummaryPayload> ExtremeTraits,
        IReadOnlyList<PromptValueSummaryPayload> StrongValues,
        IReadOnlyList<PromptNeedSummaryPayload> StrongNeeds,
        IReadOnlyList<PromptMannerismSummaryPayload> Mannerisms);

    private sealed record PromptStressPayload(
        int Raw,
        int Category,
        string CategoryScale);

    private sealed record PromptSkillSummaryPayload(
        string Token,
        int Effective,
        int TotalExperience);

    private sealed record PromptTraitSummaryPayload(
        string Token,
        int Value,
        int DeviationFromNeutral50);

    private sealed record PromptValueSummaryPayload(
        string Token,
        int Strength);

    private sealed record PromptNeedSummaryPayload(
        string Token,
        int FocusLevel,
        int NeedLevel,
        bool IsUnmet,
        bool IsDeeplyUnmet);

    private sealed record PromptMannerismSummaryPayload(
        string Token,
        string SituationToken);

    private sealed record PromptPlayerPayload(
        string Text);

    private sealed record AgentToolPromptPayload(
        string Tool,
        string ArgumentsSchemaVersion,
        string ResultSchemaVersion);
}
