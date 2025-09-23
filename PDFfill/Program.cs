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
        [FromForm] string? pdfUrl,
        [FromForm] string fields,
        [FromForm] bool flatten = true)
    {
        if (string.IsNullOrWhiteSpace(fields))
            return BadRequest("fields (JSON) erforderlich");

        var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(fields) ?? new();

        await using var inStream = new MemoryStream();
        if (!string.IsNullOrWhiteSpace(pdfUrl))
        {
            var url = NormalizeGoogleDriveUrl(pdfUrl!.Trim());
            await DownloadPdfToStream(url, inStream);
        }
        else if (pdf is not null)
        {
            await pdf.CopyToAsync(inStream);
        }
        else
        {
            return BadRequest("pdfUrl oder pdf erforderlich");
        }

        inStream.Position = 0;
        // Sicherheitscheck: Magic "%PDF"
        if (!IsPdfMagic(inStream)) return BadRequest("Quelle ist keine PDF-Datei");
        inStream.Position = 0;

        using var msOut = new MemoryStream();
        using (var reader = new PdfReader(inStream))
        using (var writer = new PdfWriter(msOut))
        using (var pdfDoc = new PdfDocument(reader, writer))
        {
            var form = PdfFormCreator.GetAcroForm(pdfDoc, true);
            form.SetNeedAppearances(true);
            var fieldsDict = form.GetAllFormFields();

            foreach (var kv in map)
            {
                if (!fieldsDict.TryGetValue(kv.Key, out var field)) continue;

                if (field is PdfButtonFormField btn && btn.GetFormType() == iText.Kernel.Pdf.PdfName.Btn)
                {
                    if (bool.TryParse(kv.Value, out var b) && !b) btn.SetValue("Off");
                    else btn.SetValue("Yes"); // Standardwert für Checkboxen
                }
                else
                {
                    field.SetValue(kv.Value ?? "");
                }
            }

            if (flatten) form.FlattenFields();
        }

        return File(msOut.ToArray(), "application/pdf", "filled.pdf");
    }

    [HttpPost("fields")]
    public async Task<IActionResult> GetFields(
        [FromForm] IFormFile? pdf,
        [FromForm] string? pdfUrl)
    {
        await using var inStream = new MemoryStream();
        if (!string.IsNullOrWhiteSpace(pdfUrl))
        {
            var url = NormalizeGoogleDriveUrl(pdfUrl!.Trim());
            await DownloadPdfToStream(url, inStream);
        }
        else if (pdf is not null)
        {
            await pdf.CopyToAsync(inStream);
        }
        else
        {
            return BadRequest("pdfUrl oder pdf erforderlich");
        }

        inStream.Position = 0;
        using var reader = new PdfReader(inStream);
        using var pdfDoc = new PdfDocument(reader);
        var form = PdfAcroForm.GetAcroForm(pdfDoc, false);
        if (form is null) return Ok(Array.Empty<PdfFieldInfo>());

        var dict = form.GetAllFormFields();
        var list = new List<PdfFieldInfo>(dict.Count);

        foreach (var kv in dict)
        {
            var name = kv.Key;
            var field = kv.Value;
            var type = field.GetFormType()?.ToString() ?? "Unknown";   // /Tx, /Btn, /Ch, /Sig
            string? value = field.GetValueAsString() ?? "";

            string[]? options = null;
            if (field is PdfChoiceFormField ch)
            {
                // versucht sowohl sichtbare Texte als auch Exportwerte zu lesen
                var opt = ch.GetOptions();
                if (opt != null && opt.Size() > 0)
                {
                    options = new string[opt.Size()];
                    for (int i = 0; i < opt.Size(); i++)
                    {
                        var arr = opt.GetAsArray(i);
                        // [export, display] oder nur [display]
                        options[i] = arr != null && arr.Size() == 2
                            ? arr.GetAsString(1)?.ToString() ?? arr.GetAsString(0)?.ToString()
                            : opt.GetAsString(i)?.ToString();
                    }
                }
            }

            int? page = null;
            float[]? rect = null;
            var widgets = field.GetWidgets();
            if (widgets != null && widgets.Count > 0)
            {
                var w = widgets[0];
                var pg = w.GetPage();
                if (pg != null) page = pdfDoc.GetPageNumber(pg);
                var r = w.GetRectangle();
                if (r != null && r.Size() == 4)
                {
                    rect = new float[]
                    {
                        r.GetAsNumber(0).FloatValue(),
                        r.GetAsNumber(1).FloatValue(),
                        r.GetAsNumber(2).FloatValue(),
                        r.GetAsNumber(3).FloatValue()
                    };
                }
            }

            // Flags (robust, falls Getter fehlen)
            var flags = field.GetFieldFlags();
            bool readOnly = (flags & PdfFormField.FF_READ_ONLY) != 0;
            bool required = (flags & PdfFormField.FF_REQUIRED) != 0;

#pragma warning disable CS8601
            list.Add(new PdfFieldInfo(name, type, value, options, page, rect, readOnly, required));
#pragma warning restore CS8601
        }

        return Ok(list);
    }

    // ===== Hilfsfunktionen (wie zuvor beim /fill Endpoint) =====

    private async Task DownloadPdfToStream(string url, Stream target)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Download fehlgeschlagen: {(int)resp.StatusCode}");
        await using var s = await resp.Content.ReadAsStreamAsync();
        await s.CopyToAsync(target);
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
            if (!string.IsNullOrWhiteSpace(idParam))
                return $"https://drive.google.com/uc?export=download&id={idParam}";
            return url;
        }
        catch { return url; }
    }
    
    private static bool IsPdfMagic(Stream stream)
    {
        var magic = new byte[4];
        var read = stream.Read(magic, 0, 4);
        if (read != 4) return false;
        return magic[0] == 0x25 && magic[1] == 0x50 && magic[2] == 0x44 && magic[3] == 0x46; // %PDF
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
