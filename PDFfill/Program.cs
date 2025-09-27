using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;
using Microsoft.AspNetCore.Mvc;

namespace PDFfill;

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
public class PdfController(IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpPost("fill")]
    public async Task<IActionResult> Fill(
        [FromForm] IFormFile? pdf,
        [FromForm] string? pdfUrl,
        [FromForm] string fields,
        [FromForm] bool flatten = true)
    {
        if (string.IsNullOrWhiteSpace(fields))
            return BadRequest("fields (JSON) erforderlich");

        var fieldValues = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(fields)
                          ?? new Dictionary<string, string>();

        var file = await ValidateAndLoadPdfFileAsync(pdf, pdfUrl);
        var filledPdf = await FillFieldsAsync(file, fieldValues, flatten);
        return File(filledPdf, "application/pdf", "filled.pdf");
    }

    [HttpPost("fields")]
    public async Task<IActionResult> ExtractFields([FromForm] IFormFile? pdf, [FromForm] string? pdfUrl)
    {
        var file = await ValidateAndLoadPdfFileAsync(pdf, pdfUrl);
        var fields = GetFieldInfos(file);
        return Ok(fields);
    }

    private static async Task<byte[]> FillFieldsAsync(
        MemoryStream file,
        Dictionary<string, string> newValues,
        bool flatten)
    {
        using var msOut = new MemoryStream();
        using (var reader = new PdfReader(file))
        await using (var writer = new PdfWriter(msOut))

        using (var pdfDoc = new PdfDocument(reader, writer))
        {
            var form = PdfFormCreator.GetAcroForm(pdfDoc, true);

            // form.SetNeedAppearances(true); tells the PDF viewer to regenerate the visual appearance of form fields
            //after their values are changed programmatically.
            //Without this, some PDF viewers (like Adobe Reader) may not display the updated field values correctly,
            //because the appearance streams (the visual representation) are not updated. Setting this flag ensures
            //that the filled values are visible to users in all PDF viewers.

            form.SetNeedAppearances(true);

            var fields = form.GetAllFormFields();

            foreach (var (fieldName, newValue) in newValues)
            {
                if (!fields.TryGetValue(fieldName, out var field))
                    continue;

                field.SetNewValue(newValue);
            }

            if (flatten)
            {
                form.FlattenFields();
            }
        }

        return msOut.ToArray();
    }


    private static List<PdfFieldInfo> GetFieldInfos(MemoryStream file)
    {
        using var reader = new PdfReader(file);
        using var pdfDoc = new PdfDocument(reader);

        var form = PdfAcroForm.GetAcroForm(pdfDoc, false);

        if (form is null)
            return [];

        var fields = form.GetAllFormFields();

        var list = new List<PdfFieldInfo>(fields.Count);

        foreach (var (name, field) in fields)
        {
            var fieldInfo = new PdfFieldInfo(
                name,
                field.GetFormType().ToString(), // /Tx, /Btn, /Ch, /Sig
                field.GetValueAsString(),
                field.GetFillOptions(),
                field.GetPageNumber(pdfDoc),
                field.GetPosition(),
                field.IsReadOnly(),
                field.IsRequired());

            list.Add(fieldInfo);
        }

        return list;
    }

    private async Task<MemoryStream> ValidateAndLoadPdfFileAsync(IFormFile? pdf, string? pdfUrl)
    {
        var inStream = new MemoryStream();

        if (!string.IsNullOrWhiteSpace(pdfUrl))
        {
            var url = NormalizeGoogleDriveUrl(pdfUrl!.Trim());
            await DownloadPdfToStreamAsync(url, inStream);
        }
        else if (pdf is not null)
        {
            await pdf.CopyToAsync(inStream);
        }
        else
        {
            throw new Exception("pdfUrl oder pdf erforderlich");
        }

        inStream.Position = 0;

        if (!IsPdfFile(inStream))
            throw new Exception("Quelle ist keine PDF-Datei");

        inStream.Position = 0;
        return inStream;
    }


    private async Task DownloadPdfToStreamAsync(string url, Stream target)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Download fehlgeschlagen: {(int)resp.StatusCode}");

        await using var s = await resp.Content.ReadAsStreamAsync();
        await s.CopyToAsync(target);
    }

    private static bool IsPdfFile(Stream stream)
    {
        var magic = new byte[4];
        var read = stream.Read(magic, 0, 4);
        if (read != 4) return false;
        return magic[0] == 0x25 && magic[1] == 0x50 && magic[2] == 0x44 && magic[3] == 0x46; // %PDF
    }

    private static string NormalizeGoogleDriveUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            if (!u.Host.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase)) return url;
            var seg = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var idx = Array.IndexOf(seg, "d");

            if (seg.Length >= 3 && Array.IndexOf(seg, "file") == 0 && idx == 1)
            {
                var id = seg[2];
                return $"https://drive.google.com/uc?export=download&id={id}";
            }

            var qs = System.Web.HttpUtility.ParseQueryString(u.Query);
            var idParam = qs.Get("id");

            return !string.IsNullOrWhiteSpace(idParam)
                ? $"https://drive.google.com/uc?export=download&id={idParam}"
                : url;
        }
        catch
        {
            return url;
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers();
        builder.Services.AddHttpClient();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();
        app.MapControllers();

        app.Run();
    }
}