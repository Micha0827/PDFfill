using iText.Forms.Fields;
using iText.Kernel.Pdf;

namespace PDFfill;

public static class PdfFieldExtensions
{
    public static void SetNewValue(this PdfFormField field, string fieldValue)
    {
        if (field is PdfButtonFormField btn && Equals(btn.GetFormType(), PdfName.Btn))
        {
            btn.SetValue(bool.TryParse(fieldValue, out var b) && !b ? "Off" : "Yes");
        }
        else
        {
            field.SetValue(fieldValue);
        }
    }

    public static string[]? GetFillOptions(this PdfFormField field)
    {
        if (field is not PdfChoiceFormField ch)
            return null;

        var opt = ch.GetOptions();

        if (opt == null || opt.Size() == 0)
            return null;

        var options = new string[opt.Size()];

        for (var i = 0; i < opt.Size(); i++)
        {
            var arr = opt.GetAsArray(i);
            // [export, display] oder nur [display]

            options[i] = arr != null && arr.Size() == 2
                ? arr.GetAsString(1)?.ToString() ?? arr.GetAsString(0)?.ToString() ?? string.Empty
                : opt.GetAsString(i)?.ToString() ?? string.Empty;
        }

        return options;
    }

    public static int GetPageNumber(this PdfFormField field, PdfDocument pdfDoc)
    {
        var widgets = field.GetWidgets();

        if (widgets is { Count: <= 0 })
            return -1;

        var page = widgets.First().GetPage();

        if (page != null)
            return pdfDoc.GetPageNumber(page);

        return -1;
    }

    public static float[] GetPosition(this PdfFormField field)
    {
        var widgets = field.GetWidgets();

        if (widgets is { Count: <= 0 })
            return [];

        var pdfArray = widgets.First().GetRectangle();

        if (pdfArray != null && pdfArray.Size() == 4)
        {
            return
            [
                pdfArray.GetAsNumber(0).FloatValue(),
                pdfArray.GetAsNumber(1).FloatValue(),
                pdfArray.GetAsNumber(2).FloatValue(),
                pdfArray.GetAsNumber(3).FloatValue()
            ];
        }

        return [];
    }
}