using System.Text;
using NexGenAccountingAI.Application.Banking;

namespace NexGenAccountingAI.Infrastructure.Files.Tests;

[TestClass]
public sealed class BankStatementReaderTests
{
    static BankStatementReaderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Task<BankStatementFile> Read(string content, Encoding? encoding = null) =>
        new BankStatementReader().ReadAsync(new MemoryStream((encoding ?? Encoding.UTF8).GetBytes(content)), "extrato");

    [TestMethod]
    public async Task ReadsACsvWithAPreambleAndDebitAndCreditColumns()
    {
        // Como os bancos exportam: dados da conta antes do cabeçalho, Windows-1252, vírgula decimal.
        var csv = """
            Consulta de movimentos
            Conta;0012 3456 7890;;;;
            ;;;;;
            Data Mov.;Data Valor;Descrição;Débito;Crédito;Saldo Contabilístico
            02-03-2026;02-03-2026;TRF DE CLIENTE UM LDA;;1.230,00;11.230,00
            03-03-2026;03-03-2026;COMISSAO MANUTENCAO CONTA;7,50;;11.222,50
            05-03-2026;04-03-2026;PAG SERV EDP;123,45;;11.099,05
            ;;Total;130,95;1.230,00;
            """;

        var result = await Read(csv, Encoding.GetEncoding(1252)).ConfigureAwait(false);

        Assert.AreEqual("CSV", result.Format);
        Assert.AreEqual(3, result.Rows.Count, "A linha de totais sem data é ignorada.");
        Assert.IsTrue(result.Rows.All(row => row.IsValid));
        Assert.AreEqual(new DateOnly(2026, 3, 2), result.Rows[0].BookingDate);
        Assert.AreEqual("TRF DE CLIENTE UM LDA", result.Rows[0].Description);
        Assert.AreEqual(1230m, result.Rows[0].Amount);
        Assert.AreEqual(-7.50m, result.Rows[1].Amount);
        Assert.AreEqual(new DateOnly(2026, 3, 4), result.Rows[2].ValueDate);
        Assert.AreEqual(10000m, result.OpeningBalance, "O saldo inicial deduz-se do saldo da primeira linha.");
        Assert.AreEqual(11099.05m, result.ClosingBalance);
        Assert.AreEqual(new DateOnly(2026, 3, 5), result.ClosingDate);
    }

    [TestMethod]
    public async Task ReadsACsvWithASignedAmountInDescendingOrder()
    {
        var csv = """
            Data,Descritivo,Montante,Saldo
            2026-03-05,"Pagamento, fornecedor",-1234.56,8765.44
            2026-03-01,Depósito,10000.00,10000.00
            """;

        var result = await Read(csv).ConfigureAwait(false);

        Assert.AreEqual(2, result.Rows.Count);
        Assert.AreEqual("Pagamento, fornecedor", result.Rows[0].Description);
        Assert.AreEqual(-1234.56m, result.Rows[0].Amount);
        Assert.AreEqual(0m, result.OpeningBalance, "Por ordem descendente, o saldo inicial vem da linha mais antiga.");
        Assert.AreEqual(8765.44m, result.ClosingBalance);
    }

    [TestMethod]
    public async Task FlagsUnreadableRows()
    {
        var result = await Read("Data;Descrição;Montante\n31-02-2026;Data impossível;10,00\n01-03-2026;Sem montante;abc\n").ConfigureAwait(false);

        Assert.AreEqual(2, result.Rows.Count);
        StringAssert.Contains(result.Rows[0].Error, "Data ilegível");
        Assert.AreEqual("Montante ilegível.", result.Rows[1].Error);
    }

    [TestMethod]
    public async Task ACsvWithoutRecognisableColumnsIsRejected()
    {
        await Assert.ThrowsExactlyAsync<BankStatementFormatException>(() => Read("a;b;c\n1;2;3\n")).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ReadsACamt053Statement()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Id>EXT-1</Id>
                  <Acct><Id><IBAN>PT50000201231234567890154</IBAN></Id></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">10000.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-03-01</Dt></Dt></Bal>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">11222.50</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-03-31</Dt></Dt></Bal>
                  <Ntry>
                    <Amt Ccy="EUR">1230.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>BOOK</Sts>
                    <BookgDt><Dt>2026-03-02</Dt></BookgDt><ValDt><Dt>2026-03-02</Dt></ValDt>
                    <AcctSvcrRef>REF-1</AcctSvcrRef>
                    <NtryDtls><TxDtls><RmtInf><Ustrd>FT A/1</Ustrd><Ustrd>Cliente Um</Ustrd></RmtInf></TxDtls></NtryDtls>
                  </Ntry>
                  <Ntry>
                    <Amt Ccy="EUR">7.50</Amt><CdtDbtInd>DBIT</CdtDbtInd><Sts><Cd>BOOK</Cd></Sts>
                    <BookgDt><DtTm>2026-03-03T10:00:00</DtTm></BookgDt>
                    <AddtlNtryInf>COMISSAO MANUTENCAO</AddtlNtryInf>
                  </Ntry>
                  <Ntry>
                    <Amt Ccy="EUR">50.00</Amt><CdtDbtInd>DBIT</CdtDbtInd><Sts>PDNG</Sts>
                    <BookgDt><Dt>2026-03-31</Dt></BookgDt>
                  </Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;

        var result = await Read(xml).ConfigureAwait(false);

        Assert.AreEqual("camt.053", result.Format);
        Assert.AreEqual("PT50000201231234567890154", result.Iban);
        Assert.AreEqual(10000m, result.OpeningBalance);
        Assert.AreEqual(11222.50m, result.ClosingBalance);
        Assert.AreEqual(new DateOnly(2026, 3, 31), result.ClosingDate);
        Assert.AreEqual(2, result.Rows.Count, "O movimento pendente fica de fora.");
        Assert.AreEqual("FT A/1 Cliente Um", result.Rows[0].Description);
        Assert.AreEqual("REF-1", result.Rows[0].Reference);
        Assert.AreEqual(-7.50m, result.Rows[1].Amount);
        Assert.AreEqual(new DateOnly(2026, 3, 3), result.Rows[1].BookingDate);
        Assert.AreEqual("COMISSAO MANUTENCAO", result.Rows[1].Description);
    }

    [TestMethod]
    public async Task AnXmlThatIsNotCamtIsRejected()
    {
        await Assert.ThrowsExactlyAsync<BankStatementFormatException>(() => Read("<AuditFile><Header /></AuditFile>")).ConfigureAwait(false);
    }
}
