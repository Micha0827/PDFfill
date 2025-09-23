# PDFfill

**Version:** 1.0.0

## Beschreibung

PDFfill ist eine ASP.NET Core Web‑API zum Ausfüllen und Auslesen von PDF‑Formularen.

## Funktionen

- **POST /Pdf/fill**: Füllt Formularfelder in einer PDF (als Datei-Upload oder per URL).  
- **POST /Pdf/fields**: Liest alle Formularfelder aus einer PDF (als Datei-Upload oder per URL).  
- Automatischer Download von Google‑Drive‑PDFs  
- Unterstützung von Text-, Auswahl- und Checkbox‑Feldern  
- Optionales Flattening (Verschmelzen) der ausgefüllten Felder  

## Voraussetzungen

- .NET 9.0 SDK  
- Docker & Docker Compose  

## Lokale Ausführung

```bash
dotnet run --project PDFfill/PDFfill.csproj
```

Die API läuft dann standardmäßig auf `http://localhost:8080`.

## Docker

Build und Start:

```bash
docker build -t pdffill:1.0.0 -f PDFfill/Dockerfile .
docker run -p 8080:8080 pdffill:1.0.0
```

## Docker Compose

```bash
docker-compose up --build
```

## Beispielaufrufe

```bash
curl -X POST http://localhost:8080/Pdf/fill \
  -F "pdf=@formular.pdf" \
  -F 'fields={\"Name\":\"Max Mustermann\"}' \
  -o filled.pdf
```

```bash
curl -X POST http://localhost:8080/Pdf/fields \
  -F "pdf=@formular.pdf"
```

## Lizenz

MIT
