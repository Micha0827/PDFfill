using iText.Kernel.Pdf;
using iText.Forms;
using iText.Forms.Fields;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#pragma warning disable CS8601

public record PdfFieldInfo(
    string Name,
    string Type,
    string? Value,
    string[]? Options,
    int? Page,
    float[]? Rect,
    bool ReadOnly,
    bool Required
);

[ApiController]
[Route("[controller]")]
public class PdfController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    public PdfController(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    [HttpPost("fill")]
    public async Task<IActionResult> Fill(
        [FromForm] IFormFile? pdf,
        [FromForm] string? password,
        [FromForm] string fields,
        [FromForm] bool flatten = true)
    {
        if (pdf is null) return BadRequest("pdf (Datei) erforderlich");
        if (string.IsNullOrWhiteSpace(fields)) return BadRequest("fields (JSON) erforderlich");

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(fields) ?? new();

        await using var inStream = new MemoryStream();
        await pdf.CopyToAsync(inStream);
        inStream.Position = 0;

        using var msOut = new MemoryStream();
        var readerProps = new ReaderProperties();
        if (!string.IsNullOrWhiteSpace(password))
            readerProps.SetPassword(System.Text.Encoding.UTF8.GetBytes(password));

        try
        {
            using var reader = new PdfReader(inStream, readerProps);
            using var writer = new PdfWriter(msOut);
            using var pdfDoc = new PdfDocument(reader, writer);
            var form = PdfFormCreator.GetAcroForm(pdfDoc, true);
            form.SetNeedAppearances(true);
            var fieldsDict = form.GetAllFormFields();

            foreach (var kv in map)
            {
                if (!fieldsDict.TryGetValue(kv.Key, out var field)) continue;
                if (field is PdfButtonFormField btn && btn.GetFormType() == iText.Kernel.Pdf.PdfName.Btn)
                {
                    if (bool.TryParse(kv.Value, out var b) && !b) btn.SetValue("Off");
                    else btn.SetValue("Yes");
                }
                else
                {
                    field.SetValue(kv.Value ?? "");
                }
            }

            if (flatten) form.FlattenFields();
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Fehler beim Lesen der PDF: {ex.Message}" });
        }

        return File(msOut.ToArray(), "application/pdf", "filled.pdf");
    }

    [HttpPost("fields")]
    public async Task<IActionResult> GetFields(
        [FromForm] IFormFile? pdf,
        [FromForm] string? password)
    {
        if (pdf is null) return BadRequest("pdf (Datei) erforderlich");

        await using var inStream = new MemoryStream();
        await pdf.CopyToAsync(inStream);
        inStream.Position = 0;

        var readerProps = new ReaderProperties();
        if (!string.IsNullOrWhiteSpace(password))
            readerProps.SetPassword(System.Text.Encoding.UTF8.GetBytes(password));

        PdfDocument pdfDoc;
        try
        {
            var reader = new PdfReader(inStream, readerProps);
            pdfDoc = new PdfDocument(reader);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Fehler beim Öffnen der PDF: {ex.Message}" });
        }

        var form = PdfAcroForm.GetAcroForm(pdfDoc, false);
        if (form is null) return Ok(Array.Empty<PdfFieldInfo>());

        var dict = form.GetAllFormFields();
        var list = new List<PdfFieldInfo>(dict.Count);
        foreach (var kv in dict)
        {
            var name = kv.Key;
            var field = kv.Value;
            var type = field.GetFormType()?.ToString() ?? "Unknown";
            var value = field.GetValueAsString() ?? "";
            string[]? options = null;
            if (field is PdfChoiceFormField ch)
            {
                var opt = ch.GetOptions();
                if (opt != null && opt.Size() > 0)
                {
                    options = new string[opt.Size()];
                    for (int i = 0; i < opt.Size(); i++)
                    {
                        var arr = ch.GetOptions().GetAsArray(i);
                        options[i] = arr != null && arr.Size() == 2 
                            ? arr.GetAsString(1)?.ToString() ?? arr.GetAsString(0)?.ToString() 
                            : ch.GetOptions().GetAsString(i)?.ToString();
                    }
                }
            }
            int? page = null;
            float[]? rect = null;
            var widgets = field.GetWidgets();
            if (widgets != null && widgets.Count > 0)
            {
                var w = widgets[0];
                if (w.GetPage() is { } pg) page = pdfDoc.GetPageNumber(pg);
                var r = w.GetRectangle();
                if (r != null && r.Size() == 4)
                    rect = new float[] { r.GetAsNumber(0).FloatValue(), r.GetAsNumber(1).FloatValue(), r.GetAsNumber(2).FloatValue(), r.GetAsNumber(3).FloatValue() };
            }
            var flags = field.GetFieldFlags();
            bool readOnly = (flags & PdfFormField.FF_READ_ONLY) != 0;
            bool required = (flags & PdfFormField.FF_REQUIRED) != 0;
#pragma warning disable CS8601
            list.Add(new PdfFieldInfo(name, type, value, options, page, rect, readOnly, required));
#pragma warning restore CS8601
        }
        return Ok(list);
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddControllers();
        var app = builder.Build();
        if (app.Environment.IsDevelopment()) app.UseDeveloperExceptionPage();
        app.UseRouting();
        app.MapControllers();
        app.Run();
    }
}
