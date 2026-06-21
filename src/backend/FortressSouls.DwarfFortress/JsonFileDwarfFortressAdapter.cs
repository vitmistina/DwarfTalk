namespace FortressSouls.DwarfFortress;

using System.Text.Json;
using FortressSouls.Application;
using FortressSouls.Domain;

public sealed class JsonFileDwarfFortressAdapter : IDwarfFortressAdapter
{
    private readonly JsonFileDwarfFortressAdapterOptions _options;

    public JsonFileDwarfFortressAdapter(JsonFileDwarfFortressAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Validate();
    }

    public async Task<DwarfListResult> ListDwarvesAsync(CancellationToken cancellationToken)
    {
        using var document = await LoadDocumentAsync(
            _options.DwarfListPath,
            _options.MaxListFileBytes,
            "dwarf list",
            cancellationToken);

        return MapList(document.RootElement);
    }

    public async Task<DwarfSnapshot> GetDwarfSnapshotAsync(DwarfId dwarfId, CancellationToken cancellationToken)
    {
        using var document = await LoadDocumentAsync(
            _options.DwarfSnapshotPath,
            _options.MaxSnapshotFileBytes,
            "dwarf snapshot",
            cancellationToken);

        return MapSnapshot(document.RootElement, dwarfId);
    }

    private async Task<JsonDocument> LoadDocumentAsync(
        string path,
        int maxFileBytes,
        string sourceName,
        CancellationToken cancellationToken)
    {
        byte[] bytes;

        try
        {
            bytes = await ReadBoundedBytesAsync(path, maxFileBytes, sourceName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DwarfFortressDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.SourceUnavailable,
                $"The configured {sourceName} source is unavailable.",
                exception);
        }

        try
        {
            return JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = _options.MaxJsonDepth
                });
        }
        catch (JsonException exception)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.MalformedJson,
                $"The configured {sourceName} source contains malformed JSON.",
                exception);
        }
    }

    private async Task<byte[]> ReadBoundedBytesAsync(
        string path,
        int maxFileBytes,
        string sourceName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Access = FileAccess.Read,
                    Mode = FileMode.Open,
                    Options = FileOptions.SequentialScan,
                    Share = FileShare.Read
                });

            if (stream.Length > maxFileBytes)
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.DataTooLarge,
                    $"The configured {sourceName} source exceeds the size limit.");
            }

            var buffer = new byte[checked((int)stream.Length)];
            var totalRead = 0;

            while (totalRead < buffer.Length)
            {
                var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, buffer.Length - totalRead),
                    cancellationToken);

                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
            }

            if (totalRead != buffer.Length)
            {
                Array.Resize(ref buffer, totalRead);
            }

            return buffer;
        }
        catch (FileNotFoundException exception)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.MissingSource,
                $"The configured {sourceName} source does not exist.",
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.MissingSource,
                $"The configured {sourceName} source does not exist.",
                exception);
        }
    }

    internal DwarfListResult MapList(JsonElement root)
    {
        var schemaVersion = GetRequiredString(root, "schemaVersion");
        EnsureSchemaVersion(schemaVersion, DwarfSchemaVersions.List, "dwarf list");

        var items = GetRequiredProperty(root, "items");
        EnsureKind(items, JsonValueKind.Array, "items");
        EnsureMaxCount(items.GetArrayLength(), _options.MaxListItems, "dwarf list items");

        var mappedItems = items
            .EnumerateArray()
            .Select(MapDwarfSummary)
            .ToArray();

        EnsureCountMatches(root, mappedItems.Length, "count", "dwarf list items");

        return new DwarfListResult(
            SchemaVersion: schemaVersion,
            Source: new DwarfListSourceMetadata(
                WorldLoaded: GetRequiredBoolean(root, "worldLoaded"),
                SiteLoaded: GetRequiredBoolean(root, "siteLoaded"),
                MapLoaded: GetRequiredBoolean(root, "mapLoaded")),
            Items: mappedItems);
    }

    private DwarfSummary MapDwarfSummary(JsonElement element)
    {
        var flags = GetRequiredProperty(element, "flags");
        EnsureKind(flags, JsonValueKind.Object, "flags");

        return new DwarfSummary(
            Id: ParseDwarfId(GetRequiredString(element, "id")),
            DisplayName: GetRequiredString(element, "displayName"),
            ProfessionName: GetRequiredString(element, "professionName"),
            ProfessionToken: GetRequiredString(element, "professionToken"),
            CurrentJobType: GetOptionalString(element, "currentJobType"),
            StressCategory: GetRequiredInt32(element, "stressCategory"),
            StressCategoryScale: GetRequiredString(element, "stressCategoryScale"),
            SoulPresent: GetRequiredBoolean(element, "soulPresent"),
            Flags: new DwarfStatusFlags(
                IsActive: GetRequiredBoolean(flags, "isActive"),
                IsAlive: GetRequiredBoolean(flags, "isAlive"),
                IsCitizen: GetRequiredBoolean(flags, "isCitizen"),
                IsResident: GetRequiredBoolean(flags, "isResident"),
                IsSane: GetRequiredBoolean(flags, "isSane")));
    }

    internal DwarfSnapshot MapSnapshot(JsonElement root, DwarfId requestedDwarfId)
    {
        var schemaVersion = GetRequiredString(root, "schemaVersion");
        EnsureSchemaVersion(schemaVersion, DwarfSchemaVersions.Snapshot, "dwarf snapshot");

        var requestedUnitId = ParseDwarfId(GetRequiredString(root, "requestedUnitId"));
        if (requestedUnitId != requestedDwarfId)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InconsistentData,
                "The configured dwarf snapshot source does not match the requested dwarf ID.");
        }

        var identity = MapIdentity(GetRequiredProperty(root, "identity"));
        if (identity.Id != requestedDwarfId)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InconsistentData,
                "The configured dwarf snapshot source does not match the requested dwarf ID.");
        }

        return new DwarfSnapshot(
            SchemaVersion: schemaVersion,
            Source: new DwarfSnapshotSourceMetadata(
                WorldLoaded: GetRequiredBoolean(root, "worldLoaded"),
                SiteLoaded: GetRequiredBoolean(root, "siteLoaded"),
                MapLoaded: GetRequiredBoolean(root, "mapLoaded"),
                SoulPresent: GetRequiredBoolean(root, "soulPresent")),
            RequestedDwarfId: requestedDwarfId,
            Identity: identity,
            Work: MapWork(root),
            Stress: MapStress(GetRequiredProperty(root, "stress")),
            Skills: MapSkillCollection(GetRequiredProperty(root, "skills")),
            Personality: MapPersonality(GetRequiredProperty(root, "personality")),
            PromptCandidates: MapPromptCandidates(GetRequiredProperty(root, "promptCandidates")));
    }

    private DwarfIdentity MapIdentity(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "identity");

        return new DwarfIdentity(
            Id: ParseDwarfId(GetRequiredString(element, "id")),
            ReadableName: GetRequiredString(element, "readableName"),
            ProfessionName: GetRequiredString(element, "professionName"),
            ProfessionToken: GetRequiredString(element, "professionToken"),
            CreatureId: GetRequiredString(element, "creatureId"),
            CasteId: GetRequiredString(element, "casteId"));
    }

    private DwarfWork MapWork(JsonElement root)
    {
        if (!root.TryGetProperty("work", out var work))
        {
            return new DwarfWork(CurrentJobType: null);
        }

        if (work.ValueKind == JsonValueKind.Array)
        {
            if (work.GetArrayLength() != 0)
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.InvalidData,
                    "The configured DFHack JSON source has an invalid 'work' shape.");
            }

            return new DwarfWork(CurrentJobType: null);
        }

        EnsureKind(work, JsonValueKind.Object, "work");

        if (!work.TryGetProperty("currentJob", out var currentJob) || currentJob.ValueKind == JsonValueKind.Null)
        {
            return new DwarfWork(CurrentJobType: null);
        }

        EnsureKind(currentJob, JsonValueKind.Object, "work.currentJob");
        return new DwarfWork(CurrentJobType: GetRequiredString(currentJob, "token"));
    }

    private DwarfStress MapStress(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "stress");

        return new DwarfStress(
            Raw: GetRequiredInt32(element, "raw"),
            Longterm: GetRequiredInt32(element, "longterm"),
            Category: GetRequiredInt32(element, "category"),
            CategoryScale: GetRequiredString(element, "categoryScale"));
    }

    private DwarfSkillCollection MapSkillCollection(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "skills");

        var items = GetRequiredProperty(element, "items");
        EnsureKind(items, JsonValueKind.Array, "skills.items");
        EnsureMaxCount(items.GetArrayLength(), _options.MaxSkills, "skills");

        var mappedItems = items
            .EnumerateArray()
            .Select(MapSkill)
            .ToArray();

        EnsureCountMatches(element, mappedItems.Length, "count", "skills");

        return new DwarfSkillCollection(
            Count: mappedItems.Length,
            Items: mappedItems);
    }

    private DwarfSkill MapSkill(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "skill");

        return new DwarfSkill(
            Token: GetRequiredString(element, "token"),
            Rating: GetRequiredInt32(element, "rating"),
            Effective: GetRequiredInt32(element, "effective"),
            Nominal: GetRequiredInt32(element, "nominal"),
            Experience: GetRequiredInt32(element, "experience"),
            TotalExperience: GetRequiredInt32(element, "totalExperience"),
            Rust: GetRequiredInt32(element, "rust"));
    }

    private DwarfPersonality MapPersonality(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "personality");

        return new DwarfPersonality(
            Present: GetRequiredBoolean(element, "present"),
            Traits: MapTraitCollection(GetRequiredProperty(element, "traits")),
            Values: MapValueCollection(GetRequiredProperty(element, "values")),
            Needs: MapNeedCollection(GetRequiredProperty(element, "needs")),
            Mannerisms: MapMannerismCollection(GetRequiredProperty(element, "mannerisms")));
    }

    private DwarfTraitCollection MapTraitCollection(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "personality.traits");

        var items = GetRequiredProperty(element, "items");
        EnsureKind(items, JsonValueKind.Array, "personality.traits.items");
        EnsureMaxCount(items.GetArrayLength(), _options.MaxTraits, "traits");

        var mappedItems = items
            .EnumerateArray()
            .Select(MapTrait)
            .ToArray();

        EnsureCountMatches(element, mappedItems.Length, "count", "traits");

        return new DwarfTraitCollection(
            Count: mappedItems.Length,
            Items: mappedItems);
    }

    private DwarfPersonalityTrait MapTrait(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "trait");

        return new DwarfPersonalityTrait(
            Token: GetRequiredString(element, "token"),
            Value: GetRequiredInt32(element, "value"),
            DeviationFromNeutral50: GetRequiredInt32(element, "deviationFromNeutral50"),
            AbsDeviationFromNeutral50: GetRequiredInt32(element, "absDeviationFromNeutral50"));
    }

    private DwarfValueCollection MapValueCollection(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "personality.values");

        var items = GetRequiredProperty(element, "items");
        EnsureKind(items, JsonValueKind.Array, "personality.values.items");
        EnsureMaxCount(items.GetArrayLength(), _options.MaxValues, "values");

        var mappedItems = items
            .EnumerateArray()
            .Select(MapValue)
            .ToArray();

        EnsureCountMatches(element, mappedItems.Length, "count", "values");

        return new DwarfValueCollection(
            Count: mappedItems.Length,
            Items: mappedItems);
    }

    private DwarfValue MapValue(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "value");

        return new DwarfValue(
            Token: GetRequiredString(element, "token"),
            Type: GetRequiredInt32(element, "type"),
            Strength: GetRequiredInt32(element, "strength"));
    }

    private DwarfNeedCollection MapNeedCollection(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "personality.needs");

        var items = GetRequiredProperty(element, "items");
        EnsureKind(items, JsonValueKind.Array, "personality.needs.items");
        EnsureMaxCount(items.GetArrayLength(), _options.MaxNeeds, "needs");

        var mappedItems = items
            .EnumerateArray()
            .Select(MapNeed)
            .ToArray();

        EnsureCountMatches(element, mappedItems.Length, "count", "needs");

        return new DwarfNeedCollection(
            Count: mappedItems.Length,
            Items: mappedItems);
    }

    private DwarfNeed MapNeed(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "need");

        return new DwarfNeed(
            Token: GetRequiredString(element, "token"),
            Id: GetRequiredInt32(element, "id"),
            DeityId: GetRequiredInt32(element, "deityId"),
            FocusLevel: GetRequiredInt32(element, "focusLevel"),
            NeedLevel: GetRequiredInt32(element, "needLevel"),
            IsUnmet: GetRequiredBoolean(element, "isUnmet"),
            IsDeeplyUnmet: GetRequiredBoolean(element, "isDeeplyUnmet"));
    }

    private DwarfMannerismCollection MapMannerismCollection(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "personality.mannerisms");

        var items = GetRequiredProperty(element, "items");
        EnsureKind(items, JsonValueKind.Array, "personality.mannerisms.items");
        EnsureMaxCount(items.GetArrayLength(), _options.MaxMannerisms, "mannerisms");

        var mappedItems = items
            .EnumerateArray()
            .Select(MapMannerism)
            .ToArray();

        EnsureCountMatches(element, mappedItems.Length, "count", "mannerisms");

        return new DwarfMannerismCollection(
            Count: mappedItems.Length,
            Items: mappedItems);
    }

    private DwarfMannerism MapMannerism(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "mannerism");

        return new DwarfMannerism(
            Token: GetRequiredString(element, "token"),
            SituationToken: GetRequiredString(element, "situationToken"));
    }

    private DwarfPromptCandidates MapPromptCandidates(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Object, "promptCandidates");

        return new DwarfPromptCandidates(
            TopSkills: MapPromptSkillArray(GetRequiredProperty(element, "topSkills")),
            ExtremeTraits: MapPromptTraitArray(GetRequiredProperty(element, "extremeTraits")),
            StrongValues: MapPromptValueArray(GetRequiredProperty(element, "strongValues")),
            StrongNeeds: MapPromptNeedArray(GetRequiredProperty(element, "strongNeeds")),
            Mannerisms: MapPromptMannerismArray(GetRequiredProperty(element, "mannerisms")));
    }

    private IReadOnlyList<DwarfSkill> MapPromptSkillArray(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Array, "promptCandidates.topSkills");
        EnsureMaxCount(element.GetArrayLength(), _options.MaxSkills, "prompt candidate skills");
        return element.EnumerateArray().Select(MapSkill).ToArray();
    }

    private IReadOnlyList<DwarfPersonalityTrait> MapPromptTraitArray(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Array, "promptCandidates.extremeTraits");
        EnsureMaxCount(element.GetArrayLength(), _options.MaxTraits, "prompt candidate traits");
        return element.EnumerateArray().Select(MapTrait).ToArray();
    }

    private IReadOnlyList<DwarfValue> MapPromptValueArray(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Array, "promptCandidates.strongValues");
        EnsureMaxCount(element.GetArrayLength(), _options.MaxValues, "prompt candidate values");
        return element.EnumerateArray().Select(MapValue).ToArray();
    }

    private IReadOnlyList<DwarfNeed> MapPromptNeedArray(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Array, "promptCandidates.strongNeeds");
        EnsureMaxCount(element.GetArrayLength(), _options.MaxNeeds, "prompt candidate needs");
        return element.EnumerateArray().Select(MapNeed).ToArray();
    }

    private IReadOnlyList<DwarfMannerism> MapPromptMannerismArray(JsonElement element)
    {
        EnsureKind(element, JsonValueKind.Array, "promptCandidates.mannerisms");
        EnsureMaxCount(element.GetArrayLength(), _options.MaxMannerisms, "prompt candidate mannerisms");
        return element.EnumerateArray().Select(MapMannerism).ToArray();
    }

    private static void EnsureSchemaVersion(string actual, string expected, string sourceName)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.UnsupportedSchema,
                $"The configured {sourceName} source uses an unsupported schema version.");
        }
    }

    private static JsonElement GetRequiredProperty(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var property))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source is missing required property '{name}'.");
        }

        return property;
    }

    private void EnsureCountMatches(JsonElement parent, int actualCount, string countPropertyName, string context)
    {
        var declaredCount = GetRequiredInt32(parent, countPropertyName);
        if (declaredCount != actualCount)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source has an inconsistent {context} count.");
        }
    }

    private void EnsureMaxCount(int count, int maxCount, string context)
    {
        if (count > maxCount)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source exceeds the {context} limit.");
        }
    }

    private static void EnsureKind(JsonElement element, JsonValueKind expectedKind, string context)
    {
        if (element.ValueKind != expectedKind)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source has an invalid '{context}' shape.");
        }
    }

    private string GetRequiredString(JsonElement parent, string name)
    {
        var property = GetRequiredProperty(parent, name);
        EnsureKind(property, JsonValueKind.String, name);

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source has an empty required property '{name}'.");
        }

        if (value.Length > _options.MaxStringLength)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source exceeds the string limit for '{name}'.");
        }

        return value;
    }

    private string? GetOptionalString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        EnsureKind(property, JsonValueKind.String, name);
        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > _options.MaxStringLength)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source exceeds the string limit for '{name}'.");
        }

        return value;
    }

    private static int GetRequiredInt32(JsonElement parent, string name)
    {
        var property = GetRequiredProperty(parent, name);
        if (!property.TryGetInt32(out var value))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source has an invalid integer property '{name}'.");
        }

        return value;
    }

    private static bool GetRequiredBoolean(JsonElement parent, string name)
    {
        var property = GetRequiredProperty(parent, name);
        if (property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                $"The configured DFHack JSON source has an invalid boolean property '{name}'.");
        }

        return property.GetBoolean();
    }

    private static DwarfId ParseDwarfId(string value)
    {
        try
        {
            return DwarfId.Parse(value);
        }
        catch (ArgumentException exception)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "The configured DFHack JSON source contains an invalid dwarf ID.",
                exception);
        }
    }
}
