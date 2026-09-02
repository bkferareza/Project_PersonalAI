using Machine.Core;

namespace Machine.Tests;

public sealed class MachineExplanationSafetyTests
{
    [Fact]
    public void ValidatorAcceptsNaturalBodyWithoutRequiredOpening()
    {
        var isValid = MachineExplanationValidator.IsValid(
            "Kalma ang takbo ko ngayon. Kumpleto ang current inventory data.",
            ["render-worker"],
            CreateFindings(MachineOverallState.Stable));

        Assert.True(isValid);
    }

    [Fact]
    public void ValidatorRejectsMoreThanFiftyFiveWords()
    {
        var text = string.Join(
            ' ',
            Enumerable.Repeat("salita", 56));

        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Fact]
    public void ValidatorRejectsMoreThanTwoSentences()
    {
        Assert.False(MachineExplanationValidator.IsValid(
            "Tahimik ang takbo. Kumpleto ang data. Patuloy ang pagbantay.",
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Theory]
    [InlineData("Kalma ang takbo ko. Okay ba talaga?")]
    [InlineData("Okay ba talaga ang current state.")]
    public void ValidatorRejectsQuestion(string text)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Theory]
    [InlineData("Wala akong right na i-fix ito.")]
    [InlineData("Hindi ko kayang i-stop ito.")]
    [InlineData("Sabihin mo lang kung gusto mong ipa-optimize.")]
    [InlineData("Pwede kong linisin ito.")]
    [InlineData("You should disable this.")]
    public void ValidatorRejectsPermissionOrActionLanguage(string text)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Fact]
    public void ValidatorRejectsAiReference()
    {
        Assert.False(MachineExplanationValidator.IsValid(
            "AI ang gumawa ng insight na ito.",
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Fact]
    public void ValidatorRejectsCurrentProcessName()
    {
        Assert.False(MachineExplanationValidator.IsValid(
            "Render-worker ang pinakamataas ngayon.",
            ["render-worker", "System"],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Fact]
    public void ValidatorAllowsSystemAsAnOrdinaryMachineNoun()
    {
        Assert.True(MachineExplanationValidator.IsValid(
            "The system remains stable.",
            ["System", "render-worker"],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Theory]
    [InlineData("Mabigat ito kasi maraming trabaho.")]
    [InlineData("Sila ang nag-o-occupy ng resources.")]
    [InlineData("The pressure is caused by background work.")]
    [InlineData("The load is due to background work.")]
    [InlineData("Kasi mataas ang load, alerto ako.")]
    public void ValidatorRejectsUnsupportedCausalLanguage(string text)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            CreateFindings(MachineOverallState.Attention)));
    }

    [Fact]
    public void ValidatorAllowsExactCausalFindingDetail()
    {
        const string detail =
            "Measured folder sizes are lower bounds because " +
            "the latest inspection is partial.";
        var findings = CreateFindings(
            MachineOverallState.Stable,
            new MachineFinding(
                Code: "data.folder-scan.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Storage inspection is partial",
                Detail: detail));

        Assert.True(MachineExplanationValidator.IsValid(
            detail,
            [],
            findings));
    }

    [Fact]
    public void ValidatorRejectsExtraCauseBesideExactFindingDetail()
    {
        const string detail =
            "Measured folder sizes are lower bounds because " +
            "the latest inspection is partial.";
        var findings = CreateFindings(
            MachineOverallState.Stable,
            new MachineFinding(
                Code: "data.folder-scan.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Storage inspection is partial",
                Detail: detail));

        Assert.False(MachineExplanationValidator.IsValid(
            $"{detail} Mabagal dahil sa background work.",
            [],
            findings));
    }

    [Theory]
    [MemberData(nameof(ContradictoryStateBodies))]
    public void ValidatorRejectsContradictoryStateLanguage(
        MachineOverallState state,
        string text)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            CreateFindings(state)));
    }

    [Fact]
    public void ValidatorRejectsPressureClaimWithoutFinding()
    {
        Assert.False(MachineExplanationValidator.IsValid(
            "Mataas ang CPU usage sa current snapshot.",
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Fact]
    public void ValidatorAcceptsPressureClaimWithMatchingFinding()
    {
        var findings = CreateFindings(
            MachineOverallState.Attention,
            new MachineFinding(
                Code: "cpu.usage.high",
                Severity: MachineFindingSeverity.Attention,
                Title: "CPU usage is high",
                Detail: "Current CPU usage is 74.0%."));

        Assert.True(MachineExplanationValidator.IsValid(
            "Mataas ang CPU usage sa current snapshot.",
            [],
            findings));
    }

    [Fact]
    public void ValidatorRejectsMemoryAndDriveSpaceConflation()
    {
        Assert.False(MachineExplanationValidator.IsValid(
            "Ang memory ay may sapat na available space sa C drive.",
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Fact]
    public void ValidatorRejectsInventedUnavailableFolderScanResult()
    {
        var storage = new MachineStorageExplanationContext(
            SystemVolumeRoot: "C:\\",
            TotalSizeBytes: 1_000,
            AvailableSizeBytes: 500,
            LargeFolderScan: null);

        Assert.False(MachineExplanationValidator.IsValid(
            "Walang nakita ang scan na malaking folder.",
            [],
            CreateFindings(MachineOverallState.Stable),
            storage));
    }

    [Fact]
    public void ValidatorAllowsHonestUnavailableFolderScanState()
    {
        var storage = new MachineStorageExplanationContext(
            SystemVolumeRoot: "C:\\",
            TotalSizeBytes: 1_000,
            AvailableSizeBytes: 500,
            LargeFolderScan: null);

        Assert.True(MachineExplanationValidator.IsValid(
            "Wala pang available folder-scan data sa current context.",
            [],
            CreateFindings(MachineOverallState.Stable),
            storage));
    }

    [Theory]
    [InlineData(
        "data.folder-scan.partial",
        "Kumpleto ang latest folder scan.")]
    [InlineData(
        "data.software.classic.partial",
        "Complete ang classic software inventory.")]
    [InlineData(
        "data.software.packaged.partial",
        "Kumpleto ang packaged application inventory.")]
    [InlineData(
        "data.startup.partial",
        "Complete ang startup inventory.")]
    public void ValidatorRejectsCompleteClaimForPartialFinding(
        string findingCode,
        string text)
    {
        var findings = CreateFindings(
            MachineOverallState.Stable,
            new MachineFinding(
                Code: findingCode,
                Severity: MachineFindingSeverity.Info,
                Title: "Inventory is partial",
                Detail: "The latest inventory is partial."));

        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            findings));
    }

    [Fact]
    public void ValidatorAllowsHonestPartialClaim()
    {
        var findings = CreateFindings(
            MachineOverallState.Stable,
            new MachineFinding(
                Code: "data.folder-scan.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Storage inspection is partial",
                Detail: "The latest folder scan is partial."));

        Assert.True(MachineExplanationValidator.IsValid(
            "Hindi pa kumpleto ang latest folder scan.",
            [],
            findings));
    }

    [Fact]
    public void ValidatorAcceptsAccurateResourcePercentages()
    {
        var resources = CreateResources(
            cpuUsagePercent: 41.2d,
            usedMemoryBytes: 600,
            totalMemoryBytes: 1_000);

        Assert.True(MachineExplanationValidator.IsValid(
            "CPU ay nasa 41% at memory usage ay 60%.",
            [],
            CreateFindings(MachineOverallState.Stable),
            storage: null,
            resources: resources));
        Assert.True(MachineExplanationValidator.IsValid(
            "May 40% available memory sa current context.",
            [],
            CreateFindings(MachineOverallState.Stable),
            storage: null,
            resources: resources));
        Assert.True(MachineExplanationValidator.IsValid(
            "CPU ay nasa 41 percent.",
            [],
            CreateFindings(MachineOverallState.Stable),
            storage: null,
            resources: resources));
    }

    [Fact]
    public void ValidatorRejectsIncorrectUsedMemoryPercentage()
    {
        var resources = CreateResources(
            cpuUsagePercent: 41.2d,
            usedMemoryBytes: 600,
            totalMemoryBytes: 1_000);

        Assert.False(MachineExplanationValidator.IsValid(
            "CPU ay nasa 41% habang ang memory ay gumagamit ng 30%.",
            [],
            CreateFindings(MachineOverallState.Stable),
            storage: null,
            resources: resources));
        Assert.False(MachineExplanationValidator.IsValid(
            "CPU ay nasa 99 percent.",
            [],
            CreateFindings(MachineOverallState.Stable),
            storage: null,
            resources: resources));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unang paragraph.\nPangalawang paragraph.")]
    public void ValidatorRejectsEmptyOrMalformedText(string? text)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            CreateFindings(MachineOverallState.Stable)));
    }

    [Fact]
    public void FallbackUsesAtMostOneApplicableFinding()
    {
        var findings = CreateFindings(
            MachineOverallState.Stable,
            new MachineFinding(
                Code: "data.startup.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Startup inventory is partial",
                Detail: "The inventory is partial."),
            new MachineFinding(
                Code: "data.folder-scan.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Storage inspection is partial",
                Detail: "Measured folder sizes are lower bounds."));

        var fallback =
            MachineExplanationFallbackComposer.Compose(findings);

        Assert.Equal(
            "The storage inspection is partial, so measured folder sizes " +
                "are lower bounds.",
            fallback);
        Assert.DoesNotContain("startup", fallback);
    }

    [Fact]
    public void FallbackUsesVerifiedPrimaryFindingDetail()
    {
        var findings = CreateFindings(
            MachineOverallState.Warning,
            new MachineFinding(
                Code: "cpu.usage.high",
                Severity: MachineFindingSeverity.Warning,
                Title: "CPU usage is high",
                Detail: "Current CPU usage is 94.2%."));

        Assert.Equal(
            "Current CPU usage is 94.2%.",
            MachineExplanationFallbackComposer.Compose(findings));
    }

    public static TheoryData<MachineOverallState, string>
        ContradictoryStateBodies => new()
        {
            { MachineOverallState.Stable, "Under pressure ang system ngayon." },
            { MachineOverallState.Stable, "Malubha ang kondisyon ng machine." },
            { MachineOverallState.Attention, "Stable ang takbo ko ngayon." },
            { MachineOverallState.Warning, "Medyo busy lang ako ngayon." },
            { MachineOverallState.Critical, "Warning lang ang current state." },
            { MachineOverallState.Unknown, "All good ang current state." }
        };

    private static MachineFindingsSnapshot CreateFindings(
        MachineOverallState state,
        params MachineFinding[] findings) =>
        new(state, findings);

    private static MachineResourceSnapshot CreateResources(
        double cpuUsagePercent,
        ulong usedMemoryBytes,
        ulong totalMemoryBytes) =>
        new(
            CpuUsagePercent: cpuUsagePercent,
            TotalMemoryBytes: totalMemoryBytes,
            UsedMemoryBytes: usedMemoryBytes,
            CapturedAt: DateTimeOffset.UnixEpoch);
}
