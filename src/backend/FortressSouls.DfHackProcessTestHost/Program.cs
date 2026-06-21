namespace FortressSouls.DfHackProcessTestHost;

using System.Text;

public static class Marker
{
}

public static class Program
{
    public static int Main(string[] args)
    {
        var mode = Environment.GetEnvironmentVariable("FORTRESS_SOULS_DFHACK_TEST_MODE") ?? "success";
        var command = args.Length > 0 ? args[0] : string.Empty;

        return mode switch
        {
            "success" => WriteSuccess(command, args.Skip(1).ToArray()),
            "oem_name" => WriteOemNameSuccess(command, args.Skip(1).ToArray()),
            "invalid_json" => WriteRaw("{"),
            "failed" => WriteFailure(),
            "crashed" => -1073741819,
            "oversize" => WriteRaw(new string('x', 200_000)),
            "hang" => Hang(),
            _ => WriteRaw("""{"schemaVersion":"fortress-souls-dwarf-list.v0.1","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"count":0,"items":[]}""")
        };
    }

    private static int WriteSuccess(string command, string[] args)
    {
        if (string.Equals(command, "fortress-souls/list-dwarves", StringComparison.Ordinal))
        {
            return WriteRaw(
                """
                {"schemaVersion":"fortress-souls-dwarf-list.v0.1","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"count":1,"items":[{"id":"6597","displayName":"Dwarf One","professionName":"Miner","professionToken":"MINER","currentJobType":"Dig","stressCategory":3,"stressCategoryScale":"0-most-stressed-6-least-stressed","soulPresent":true,"flags":{"isActive":true,"isAlive":true,"isCitizen":true,"isResident":false,"isSane":true}}]}
                """);
        }

        if (string.Equals(command, "fortress-souls/get-dwarf-snapshot", StringComparison.Ordinal))
        {
            var unitId = args.FirstOrDefault() ?? "6597";
            var snapshot =
                """
                {"schemaVersion":"fortress-souls-dwarf-snapshot.v0.1","requestedUnitId":"__UNIT_ID__","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"soulPresent":true,"identity":{"id":"__UNIT_ID__","readableName":"Dwarf One","professionName":"Miner","professionToken":"MINER","creatureId":"DWARF","casteId":"MALE"},"stress":{"raw":0,"longterm":0,"category":3,"categoryScale":"0-most-stressed-6-least-stressed"},"skills":{"count":1,"items":[{"token":"MINING","rating":5,"effective":5,"nominal":5,"experience":0,"totalExperience":0,"rust":0}]},"personality":{"present":true,"traits":{"count":1,"items":[{"token":"CHEER_PROPENSITY","value":60,"deviationFromNeutral50":10,"absDeviationFromNeutral50":10}]},"values":{"count":1,"items":[{"token":"FAMILY","type":1,"strength":25}]},"needs":{"count":1,"items":[{"token":"DrinkAlcohol","id":1,"deityId":-1,"focusLevel":0,"needLevel":10,"isUnmet":false,"isDeeplyUnmet":false}]},"mannerisms":{"count":0,"items":[]}},"promptCandidates":{"topSkills":[{"token":"MINING","rating":5,"effective":5,"nominal":5,"experience":0,"totalExperience":0,"rust":0}],"extremeTraits":[{"token":"CHEER_PROPENSITY","value":60,"deviationFromNeutral50":10,"absDeviationFromNeutral50":10}],"strongValues":[{"token":"FAMILY","type":1,"strength":25}],"strongNeeds":[{"token":"DrinkAlcohol","id":1,"deityId":-1,"focusLevel":0,"needLevel":10,"isUnmet":false,"isDeeplyUnmet":false}],"mannerisms":[]}}
                """;
            return WriteRaw(
                snapshot.Replace("__UNIT_ID__", unitId, StringComparison.Ordinal));
        }

        return WriteRaw("""{"schemaVersion":"fortress-souls-diagnose.v0.1","worldLoaded":true,"siteLoaded":true,"mapLoaded":true}""");
    }

    private static int WriteFailure()
    {
        Console.Out.WriteLine("script failed");
        Console.Error.WriteLine("stderr failed");
        return 3;
    }

    private static int WriteOemNameSuccess(string command, string[] args)
    {
        if (string.Equals(command, "fortress-souls/list-dwarves", StringComparison.Ordinal))
        {
            return WriteEncodedRaw(
                """
                {"schemaVersion":"fortress-souls-dwarf-list.v0.1","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"count":2,"items":[{"id":"6601","displayName":"Kadôl Thocitoddom \"Spikescloisters\", Fisherdwarf","professionName":"Fisherdwarf","professionToken":"FISHERY_WORKER","currentJobType":"Fish","stressCategory":3,"stressCategoryScale":"0-most-stressed-6-least-stressed","soulPresent":true,"flags":{"isActive":true,"isAlive":true,"isCitizen":true,"isResident":false,"isSane":true}},{"id":"6599","displayName":"îton Oltarkurik \"Gildthorn\", Woodworker","professionName":"Woodworker","professionToken":"WOODWORKER","currentJobType":"ConstructBed","stressCategory":4,"stressCategoryScale":"0-most-stressed-6-least-stressed","soulPresent":true,"flags":{"isActive":true,"isAlive":true,"isCitizen":true,"isResident":false,"isSane":true}}]}
                """,
                GetDfHackLikeOutputEncoding());
        }

        if (string.Equals(command, "fortress-souls/get-dwarf-snapshot", StringComparison.Ordinal))
        {
            var unitId = args.FirstOrDefault() ?? "6601";
            var displayName = unitId == "6599"
                ? """îton Oltarkurik "Gildthorn", Woodworker"""
                : """Kadôl Thocitoddom "Spikescloisters", Fisherdwarf""";
            var escapedDisplayName = displayName.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);
            var professionName = unitId == "6599" ? "Woodworker" : "Fisherdwarf";
            var professionToken = unitId == "6599" ? "WOODWORKER" : "FISHERY_WORKER";
            var currentJobType = unitId == "6599" ? "ConstructBed" : "Fish";

            var snapshot =
                """
                {"schemaVersion":"fortress-souls-dwarf-snapshot.v0.1","requestedUnitId":"__UNIT_ID__","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"soulPresent":true,"identity":{"id":"__UNIT_ID__","readableName":"__DISPLAY_NAME__","professionName":"__PROFESSION_NAME__","professionToken":"__PROFESSION_TOKEN__","creatureId":"DWARF","casteId":"FEMALE"},"work":{"currentJobType":"__CURRENT_JOB_TYPE__"},"stress":{"raw":0,"longterm":0,"category":3,"categoryScale":"0-most-stressed-6-least-stressed"},"skills":{"count":1,"items":[{"token":"MINING","rating":5,"effective":5,"nominal":5,"experience":0,"totalExperience":0,"rust":0}]},"personality":{"present":true,"traits":{"count":1,"items":[{"token":"CHEER_PROPENSITY","value":60,"deviationFromNeutral50":10,"absDeviationFromNeutral50":10}]},"values":{"count":1,"items":[{"token":"FAMILY","type":1,"strength":25}]},"needs":{"count":1,"items":[{"token":"DrinkAlcohol","id":1,"deityId":-1,"focusLevel":0,"needLevel":10,"isUnmet":false,"isDeeplyUnmet":false}]},"mannerisms":{"count":0,"items":[]}},"promptCandidates":{"topSkills":[{"token":"MINING","rating":5,"effective":5,"nominal":5,"experience":0,"totalExperience":0,"rust":0}],"extremeTraits":[{"token":"CHEER_PROPENSITY","value":60,"deviationFromNeutral50":10,"absDeviationFromNeutral50":10}],"strongValues":[{"token":"FAMILY","type":1,"strength":25}],"strongNeeds":[{"token":"DrinkAlcohol","id":1,"deityId":-1,"focusLevel":0,"needLevel":10,"isUnmet":false,"isDeeplyUnmet":false}],"mannerisms":[]}}
                """;

            return WriteEncodedRaw(
                snapshot
                    .Replace("__UNIT_ID__", unitId, StringComparison.Ordinal)
                    .Replace("__DISPLAY_NAME__", escapedDisplayName, StringComparison.Ordinal)
                    .Replace("__PROFESSION_NAME__", professionName, StringComparison.Ordinal)
                    .Replace("__PROFESSION_TOKEN__", professionToken, StringComparison.Ordinal)
                    .Replace("__CURRENT_JOB_TYPE__", currentJobType, StringComparison.Ordinal),
                GetDfHackLikeOutputEncoding());
        }

        return WriteRaw("""{"schemaVersion":"fortress-souls-diagnose.v0.1","worldLoaded":true,"siteLoaded":true,"mapLoaded":true}""");
    }

    private static int WriteRaw(string value)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Out.Write(value);
        return 0;
    }

    private static int WriteEncodedRaw(string value, Encoding encoding)
    {
        var bytes = encoding.GetBytes(value);
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
        return 0;
    }

    private static Encoding GetDfHackLikeOutputEncoding()
    {
        if (OperatingSystem.IsWindows())
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }

        return new UTF8Encoding(false);
    }

    private static int Hang()
    {
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }
}
