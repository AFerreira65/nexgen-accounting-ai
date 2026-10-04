using NexGenAccountingAI.Core.Configuration;

namespace NexGenAccountingAI.Core.Phc;

public static class PhcStockPolicy
{
    public static bool MovesStock(PhcInvoiceImportMode importMode, int articleStns)
    {
        var grouped = importMode is
            PhcInvoiceImportMode.OneDocumentForGenericCustomer or
            PhcInvoiceImportMode.DocumentsWithLinesGroupedByVat;
        return !grouped && articleStns != 0;
    }
}
