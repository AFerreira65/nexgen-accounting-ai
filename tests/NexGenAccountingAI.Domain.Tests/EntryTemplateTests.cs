using NexGenAccountingAI.Domain.Accounting;

namespace NexGenAccountingAI.Domain.Tests;

[TestClass]
public sealed class EntryTemplateTests
{
    private static readonly Guid Normal = Guid.NewGuid();
    private static readonly Guid Exempt = Guid.NewGuid();
    private static readonly Dictionary<Guid, decimal> Percentages = new() { [Normal] = 23m, [Exempt] = 0m };

    private static EntryTemplate Template(params EntryTemplateLine[] lines)
    {
        var template = EntryTemplate.Create(Guid.NewGuid(), " compra-nor ");
        template.Update("Compra à taxa normal", Guid.NewGuid(), null, null, isActive: true);
        template.ReplaceLines(lines);
        return template;
    }

    private static EntryTemplate Purchase() => Template(
        new EntryTemplateLine(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, Guid.NewGuid(), Normal),
        new EntryTemplateLine(EntryTemplateLineKind.Vat, EntryTemplateSide.Debit, Guid.NewGuid(), Normal),
        new EntryTemplateLine(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, Guid.NewGuid()));

    [TestMethod]
    public void ComputesVatFromTheBaseAndBalancesTheEntry()
    {
        var template = Purchase();

        var lines = template.Compute(100m, amountIncludesVat: false, Percentages);

        Assert.AreEqual("COMPRA-NOR", template.Code);
        Assert.AreEqual(3, lines.Count);
        Assert.AreEqual(100m, lines[0].Debit);
        Assert.AreEqual(23m, lines[1].Debit);
        Assert.AreEqual(123m, lines[2].Credit);
        Assert.AreEqual(lines.Sum(line => line.Debit), lines.Sum(line => line.Credit));
    }

    [TestMethod]
    public void WithVatIncludedTheTotalMatchesTheDocumentToTheCent()
    {
        // 100,00 com IVA a 23%: base 81,30 e IVA 18,70 (e não 18,699).
        var lines = Purchase().Compute(100m, amountIncludesVat: true, Percentages);

        Assert.AreEqual(81.30m, lines[0].Debit);
        Assert.AreEqual(18.70m, lines[1].Debit);
        Assert.AreEqual(100m, lines[2].Credit);
    }

    [TestMethod]
    public void ReverseChargePostsVatOnBothSidesAndBalancesOnTheBase()
    {
        var template = Template(
            new EntryTemplateLine(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, Guid.NewGuid(), Normal),
            new EntryTemplateLine(EntryTemplateLineKind.Vat, EntryTemplateSide.Debit, Guid.NewGuid(), Normal),
            new EntryTemplateLine(EntryTemplateLineKind.Vat, EntryTemplateSide.Credit, Guid.NewGuid(), Normal),
            new EntryTemplateLine(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, Guid.NewGuid()));

        var lines = template.Compute(200m, amountIncludesVat: false, Percentages);

        Assert.AreEqual(46m, lines[1].Debit);
        Assert.AreEqual(46m, lines[2].Credit);
        Assert.AreEqual(200m, lines[3].Credit, "A contrapartida é só a base: o IVA anula-se.");
        Assert.ThrowsExactly<InvalidOperationException>(() => template.Compute(200m, amountIncludesVat: true, Percentages));
    }

    [TestMethod]
    public void ExemptVatLineIsOmitted()
    {
        var template = Template(
            new EntryTemplateLine(EntryTemplateLineKind.Base, EntryTemplateSide.Credit, Guid.NewGuid(), Exempt),
            new EntryTemplateLine(EntryTemplateLineKind.Vat, EntryTemplateSide.Credit, Guid.NewGuid(), Exempt),
            new EntryTemplateLine(EntryTemplateLineKind.Balance, EntryTemplateSide.Debit, Guid.NewGuid()));

        var lines = template.Compute(50m, amountIncludesVat: false, Percentages);

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(50m, lines[0].Credit);
        Assert.AreEqual(50m, lines[1].Debit);
    }

    [TestMethod]
    public void RejectsMalformedTemplatesAndAmounts()
    {
        var account = Guid.NewGuid();
        Assert.ThrowsExactly<ArgumentException>(() => Template(
            new EntryTemplateLine(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, account)));
        Assert.ThrowsExactly<ArgumentException>(() => Template(
            new EntryTemplateLine(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, account)));
        Assert.ThrowsExactly<ArgumentException>(() => Template(
            new EntryTemplateLine(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, account),
            new EntryTemplateLine(EntryTemplateLineKind.Vat, EntryTemplateSide.Debit, account),
            new EntryTemplateLine(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, account)));

        Assert.ThrowsExactly<ArgumentException>(() => Purchase().Compute(0m, false, Percentages));

        // Contrapartida do mesmo lado da base: nunca equilibra.
        var wrongSide = Template(
            new EntryTemplateLine(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, account),
            new EntryTemplateLine(EntryTemplateLineKind.Balance, EntryTemplateSide.Debit, account));
        Assert.ThrowsExactly<InvalidOperationException>(() => wrongSide.Compute(10m, false, Percentages));
    }

    [TestMethod]
    public void ADocumentKeepsItsOwnBaseAndVat()
    {
        // A fatura do e-Fatura traz 18,71 de IVA (arredondamento do fornecedor): fica assim.
        var lines = Purchase().ComputeForDocument(81.30m, 18.71m, reverse: false, Percentages);

        Assert.AreEqual(81.30m, lines[0].Debit);
        Assert.AreEqual(18.71m, lines[1].Debit);
        Assert.AreEqual(100.01m, lines[2].Credit);
    }

    [TestMethod]
    public void ACreditNoteSwapsTheSides()
    {
        var lines = Purchase().ComputeForDocument(-10m, -2.30m, reverse: true, Percentages);

        Assert.AreEqual(10m, lines[0].Credit);
        Assert.AreEqual(2.30m, lines[1].Credit);
        Assert.AreEqual(12.30m, lines[2].Debit);
        Assert.AreEqual(0m, lines.Sum(line => line.Debit) - lines.Sum(line => line.Credit));
    }

    [TestMethod]
    public void WithoutAVatLineTheVatIsACost()
    {
        var template = Template(
            new EntryTemplateLine(EntryTemplateLineKind.Base, EntryTemplateSide.Debit, Guid.NewGuid()),
            new EntryTemplateLine(EntryTemplateLineKind.Balance, EntryTemplateSide.Credit, Guid.NewGuid()));

        var lines = template.ComputeForDocument(50m, 11.50m, reverse: false, Percentages);

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(61.50m, lines[0].Debit);
        Assert.AreEqual(61.50m, lines[1].Credit);
    }

    [TestMethod]
    public void AnExemptDocumentOmitsTheZeroVatLine()
    {
        var lines = Purchase().ComputeForDocument(40m, 0m, reverse: false, Percentages);

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(40m, lines[1].Credit);
    }
}
