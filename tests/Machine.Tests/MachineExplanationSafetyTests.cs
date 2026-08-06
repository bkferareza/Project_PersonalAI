using Machine.Core;

namespace Machine.Tests;

public sealed class MachineExplanationSafetyTests
{
    private const string Opening = "Stable ako ngayon.";

    [Fact]
    public void ValidatorAcceptsExactOpeningAndOneObservation()
    {
        var isValid = MachineExplanationValidator.IsValid(
            "Stable ako ngayon. Kumpleto ang current inventory data.",
            Opening,
            ["render-worker"],
            CreateFindings());

        Assert.True(isValid);
    }

    [Theory]
    [InlineData("Iba ang opening.")]
    [InlineData("Stable ako ngayon.Ito ay dikit.")]
    public void ValidatorRejectsMissingExactOpening(string text)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            Opening,
            [],
            CreateFindings()));
    }

    [Fact]
    public void ValidatorRejectsMoreThanFortyFiveWords()
    {
        var text = Opening + " " + string.Join(
            ' ',
            Enumerable.Repeat("salita", 43));

        Assert.Equal(
            46,
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            Opening,
            [],
            CreateFindings()));
    }

    [Fact]
    public void ValidatorRejectsQuestion()
    {
        Assert.False(MachineExplanationValidator.IsValid(
            "Stable ako ngayon. Okay ba talaga?",
            Opening,
            [],
            CreateFindings()));
    }

    [Theory]
    [InlineData("Wala akong right na i-fix ito.")]
    [InlineData("Hindi ko kayang i-stop ito.")]
    [InlineData("Sabihin mo lang kung gusto mong ipa-optimize.")]
    [InlineData("Pwede kong linisin ito.")]
    [InlineData("I can fix this for you.")]
    public void ValidatorRejectsPermissionOrActionLanguage(
        string observation)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            $"{Opening} {observation}",
            Opening,
            [],
            CreateFindings()));
    }

    [Fact]
    public void ValidatorRejectsCurrentProcessName()
    {
        Assert.False(MachineExplanationValidator.IsValid(
            $"{Opening} render-worker ang pinakamataas ngayon.",
            Opening,
            ["render-worker", "System"],
            CreateFindings()));
    }

    [Theory]
    [InlineData("Mabigat ito kasi maraming trabaho.")]
    [InlineData("Sila ang nag-o-occupy ng resources.")]
    [InlineData("The pressure is caused by background work.")]
    [InlineData("The load is due to background work.")]
    public void ValidatorRejectsUnsupportedCausalLanguage(
        string observation)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            $"{Opening} {observation}",
            Opening,
            [],
            CreateFindings()));
    }

    [Fact]
    public void ValidatorAllowsExactCausalFindingDetail()
    {
        const string detail =
            "Measured folder sizes are lower bounds because " +
            "the latest inspection is partial.";
        var findings = CreateFindings(
            new MachineFinding(
                Code: "data.folder-scan.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Storage inspection is partial",
                Detail: detail));

        Assert.True(MachineExplanationValidator.IsValid(
            $"{Opening} {detail}",
            Opening,
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
            new MachineFinding(
                Code: "data.folder-scan.partial",
                Severity: MachineFindingSeverity.Info,
                Title: "Storage inspection is partial",
                Detail: detail));

        Assert.False(MachineExplanationValidator.IsValid(
            $"{Opening} {detail} Mabagal dahil sa background work.",
            Opening,
            [],
            findings));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Stable ako ngayon.\nPangalawang paragraph.")]
    public void ValidatorRejectsEmptyOrMalformedText(string? text)
    {
        Assert.False(MachineExplanationValidator.IsValid(
            text,
            Opening,
            [],
            CreateFindings()));
    }

    [Fact]
    public void FallbackUsesOpeningAndAtMostOneApplicableFinding()
    {
        var findings = CreateFindings(
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

        var fallback = MachineExplanationFallbackComposer.Compose(
            Opening,
            findings);

        Assert.Equal(
            "Stable ako ngayon. Partial pa ang storage inspection, " +
                "kaya lower bounds lang ang measured folder sizes.",
            fallback);
        Assert.DoesNotContain("startup", fallback);
    }

    private static MachineFindingsSnapshot CreateFindings(
        params MachineFinding[] findings) =>
        new(MachineOverallState.Stable, findings);
}
