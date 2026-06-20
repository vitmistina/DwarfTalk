namespace FortressSouls.DwarfFortress;

using System.Collections.ObjectModel;
using FortressSouls.Application;
using FortressSouls.Domain;

public sealed class FakeDwarfFortressAdapter : IDwarfFortressAdapter
{
    private static readonly DwarfFixture[] Fixtures =
    [
        new(
            Summary: new DwarfSummary(
                Id: DwarfId.Parse("4101"),
                DisplayName: "Iden Torrentshade",
                ProfessionName: "Miner",
                ProfessionToken: "MINER",
                CurrentJobType: "DigChannel",
                StressCategory: 1,
                StressCategoryScale: "Low",
                SoulPresent: true,
                Flags: new DwarfStatusFlags(true, true, true, false, true)),
            Snapshot: new DwarfSnapshot(
                SchemaVersion: DwarfSchemaVersions.Snapshot,
                Source: new DwarfSnapshotSourceMetadata(true, true, true, true),
                RequestedDwarfId: DwarfId.Parse("4101"),
                Identity: new DwarfIdentity(
                    DwarfId.Parse("4101"),
                    "Iden Torrentshade",
                    "Miner",
                    "MINER",
                    "DWARF",
                    "MALE"),
                Work: new DwarfWork("DigChannel"),
                Stress: new DwarfStress(12000, 9000, 1, "Low"),
                Skills: new DwarfSkillCollection(
                    3,
                    AsReadOnlyList(
                    [
                        new DwarfSkill("Mining", 9, 9, 9, 4000, 14000, 0),
                        new DwarfSkill("Stonecutting", 6, 6, 6, 1500, 6500, 0),
                        new DwarfSkill("Masonry", 4, 4, 4, 500, 2500, 0)
                    ])),
                Personality: new DwarfPersonality(
                    true,
                    new DwarfTraitCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfPersonalityTrait("PERSEVERANCE", 72, 22, 22),
                            new DwarfPersonalityTrait("DUTIFULNESS", 64, 14, 14)
                        ])),
                    new DwarfValueCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfValue("CRAFTSMANSHIP", 3, 66),
                            new DwarfValue("HARD_WORK", 8, 59)
                        ])),
                    new DwarfNeedCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfNeed("DrinkAlcohol", 1, -1, -1000, 5, true, true),
                            new DwarfNeed("BeCreative", 2, -1, 2500, 2, false, false)
                        ])),
                    new DwarfMannerismCollection(
                        1,
                        AsReadOnlyList(
                        [
                            new DwarfMannerism("TAPS_FOOT", "THINKING")
                        ]))),
                PromptCandidates: new DwarfPromptCandidates(
                    TopSkills: AsReadOnlyList(
                    [
                        new DwarfSkill("Mining", 9, 9, 9, 4000, 14000, 0),
                        new DwarfSkill("Stonecutting", 6, 6, 6, 1500, 6500, 0)
                    ]),
                    ExtremeTraits: AsReadOnlyList(
                    [
                        new DwarfPersonalityTrait("PERSEVERANCE", 72, 22, 22)
                    ]),
                    StrongValues: AsReadOnlyList(
                    [
                        new DwarfValue("CRAFTSMANSHIP", 3, 66)
                    ]),
                    StrongNeeds: AsReadOnlyList(
                    [
                        new DwarfNeed("DrinkAlcohol", 1, -1, -1000, 5, true, true)
                    ]),
                    Mannerisms: AsReadOnlyList(
                    [
                        new DwarfMannerism("TAPS_FOOT", "THINKING")
                    ])))),
        new(
            Summary: new DwarfSummary(
                Id: DwarfId.Parse("4102"),
                DisplayName: "Nil Stonereed",
                ProfessionName: "Farmer",
                ProfessionToken: "FARMER",
                CurrentJobType: "HarvestPlants",
                StressCategory: 0,
                StressCategoryScale: "Calm",
                SoulPresent: true,
                Flags: new DwarfStatusFlags(true, true, true, false, true)),
            Snapshot: new DwarfSnapshot(
                SchemaVersion: DwarfSchemaVersions.Snapshot,
                Source: new DwarfSnapshotSourceMetadata(true, true, true, true),
                RequestedDwarfId: DwarfId.Parse("4102"),
                Identity: new DwarfIdentity(
                    DwarfId.Parse("4102"),
                    "Nil Stonereed",
                    "Farmer",
                    "FARMER",
                    "DWARF",
                    "FEMALE"),
                Work: new DwarfWork("HarvestPlants"),
                Stress: new DwarfStress(4500, 3000, 0, "Calm"),
                Skills: new DwarfSkillCollection(
                    3,
                    AsReadOnlyList(
                    [
                        new DwarfSkill("Growing", 8, 8, 8, 3600, 12600, 0),
                        new DwarfSkill("Brewing", 5, 5, 5, 1100, 5100, 0),
                        new DwarfSkill("Cooking", 3, 3, 3, 500, 1500, 0)
                    ])),
                Personality: new DwarfPersonality(
                    true,
                    new DwarfTraitCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfPersonalityTrait("CHEER_PROPENSITY", 74, 24, 24),
                            new DwarfPersonalityTrait("FRIENDLINESS", 67, 17, 17)
                        ])),
                    new DwarfValueCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfValue("NATURE", 14, 71),
                            new DwarfValue("FAMILY", 10, 58)
                        ])),
                    new DwarfNeedCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfNeed("PrayOrMeditate", 3, 24, 1000, 2, false, false),
                            new DwarfNeed("BeWithFamily", 4, -1, -250, 3, true, false)
                        ])),
                    new DwarfMannerismCollection(
                        1,
                        AsReadOnlyList(
                        [
                            new DwarfMannerism("HUMS", "WORKING")
                        ]))),
                PromptCandidates: new DwarfPromptCandidates(
                    TopSkills: AsReadOnlyList(
                    [
                        new DwarfSkill("Growing", 8, 8, 8, 3600, 12600, 0)
                    ]),
                    ExtremeTraits: AsReadOnlyList(
                    [
                        new DwarfPersonalityTrait("CHEER_PROPENSITY", 74, 24, 24)
                    ]),
                    StrongValues: AsReadOnlyList(
                    [
                        new DwarfValue("NATURE", 14, 71)
                    ]),
                    StrongNeeds: AsReadOnlyList(
                    [
                        new DwarfNeed("BeWithFamily", 4, -1, -250, 3, true, false)
                    ]),
                    Mannerisms: AsReadOnlyList(
                    [
                        new DwarfMannerism("HUMS", "WORKING")
                    ])))),
        new(
            Summary: new DwarfSummary(
                Id: DwarfId.Parse("4103"),
                DisplayName: "Domas Inkgranite",
                ProfessionName: "Bookkeeper",
                ProfessionToken: "BOOKKEEPER",
                CurrentJobType: "UpdateStockpileRecords",
                StressCategory: 2,
                StressCategoryScale: "Elevated",
                SoulPresent: true,
                Flags: new DwarfStatusFlags(true, true, true, false, true)),
            Snapshot: new DwarfSnapshot(
                SchemaVersion: DwarfSchemaVersions.Snapshot,
                Source: new DwarfSnapshotSourceMetadata(true, true, true, true),
                RequestedDwarfId: DwarfId.Parse("4103"),
                Identity: new DwarfIdentity(
                    DwarfId.Parse("4103"),
                    "Domas Inkgranite",
                    "Bookkeeper",
                    "BOOKKEEPER",
                    "DWARF",
                    "MALE"),
                Work: new DwarfWork("UpdateStockpileRecords"),
                Stress: new DwarfStress(28000, 22000, 2, "Elevated"),
                Skills: new DwarfSkillCollection(
                    3,
                    AsReadOnlyList(
                    [
                        new DwarfSkill("Organizer", 10, 10, 10, 5200, 16200, 0),
                        new DwarfSkill("Appraisal", 6, 6, 6, 1400, 6400, 0),
                        new DwarfSkill("JudgeOfIntent", 4, 4, 4, 650, 2650, 0)
                    ])),
                Personality: new DwarfPersonality(
                    true,
                    new DwarfTraitCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfPersonalityTrait("ORDERLINESS", 79, 29, 29),
                            new DwarfPersonalityTrait("ANXIETY_PROPENSITY", 63, 13, 13)
                        ])),
                    new DwarfValueCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfValue("LAW", 6, 68),
                            new DwarfValue("KNOWLEDGE", 4, 62)
                        ])),
                    new DwarfNeedCollection(
                        2,
                        AsReadOnlyList(
                        [
                            new DwarfNeed("AcquireObject", 5, -1, -1200, 4, true, true),
                            new DwarfNeed("Excitement", 6, -1, 2200, 2, false, false)
                        ])),
                    new DwarfMannerismCollection(
                        1,
                        AsReadOnlyList(
                        [
                            new DwarfMannerism("STRAIGHTENS_SCROLLS", "THINKING")
                        ]))),
                PromptCandidates: new DwarfPromptCandidates(
                    TopSkills: AsReadOnlyList(
                    [
                        new DwarfSkill("Organizer", 10, 10, 10, 5200, 16200, 0)
                    ]),
                    ExtremeTraits: AsReadOnlyList(
                    [
                        new DwarfPersonalityTrait("ORDERLINESS", 79, 29, 29)
                    ]),
                    StrongValues: AsReadOnlyList(
                    [
                        new DwarfValue("LAW", 6, 68)
                    ]),
                    StrongNeeds: AsReadOnlyList(
                    [
                        new DwarfNeed("AcquireObject", 5, -1, -1200, 4, true, true)
                    ]),
                    Mannerisms: AsReadOnlyList(
                    [
                        new DwarfMannerism("STRAIGHTENS_SCROLLS", "THINKING")
                    ]))))
    ];

    public Task<DwarfListResult> ListDwarvesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new DwarfListResult(
                SchemaVersion: DwarfSchemaVersions.List,
                Source: new DwarfListSourceMetadata(true, true, true),
                Items: AsReadOnlyList(Fixtures.Select(fixture => fixture.Summary).ToArray())));
    }

    public Task<DwarfSnapshot> GetDwarfSnapshotAsync(DwarfId dwarfId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fixture = Fixtures.FirstOrDefault(candidate => candidate.Summary.Id == dwarfId);
        if (fixture is null)
        {
            throw new DwarfNotFoundException(dwarfId);
        }

        return Task.FromResult(fixture.Snapshot);
    }

    private static IReadOnlyList<T> AsReadOnlyList<T>(T[] items) =>
        new ReadOnlyCollection<T>(items);

    private sealed record DwarfFixture(DwarfSummary Summary, DwarfSnapshot Snapshot);
}
