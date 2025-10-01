# PDFfill

API zum Füllen und Auslesen von PDF‑Formularen inklusive Unterstützung für passwortgeschützte PDFs.

## Voraussetzungen

- .NET 9 SDK oder höher (TargetFramework: net9.0)
- iText7 Bibliothek (NuGet-Pakete `itext.kernel.pdf` v9.3.0, `itext.forms` v9.3.0, `itext.bouncy-castle-adapter` v9.3.0)

## Installation und Start

```bash
dotnet build
dotnet run --project PDFfill
```

Standardmäßig läuft der Server auf `http://localhost:5244`.

## Endpoints

### POST /pdf/fill

Füllt Felder eines PDF‑Formulars und gibt das ausgefüllte PDF zurück.  
Bei passwortgeschützten PDFs kann das Öffnungs-Passwort übergeben werden.

**Formulardaten (multipart/form-data):**

| Name     | Typ    | Beschreibung                                                 |
| -------- | ------ | ------------------------------------------------------------ |
| pdf      | Datei  | PDF-Datei (multipart/form-data)                              |
| password | string | Passwort für das Öffnen einer passwortgeschützten PDF (optional) |
| fields   | string | JSON-String mit Feldnamen und Werten (z. B. `{"Name":"Max"} `) |
| flatten  | bool   | Formularfelder nach dem Ausfüllen verflachen (optional, Standard: `true`) |

Beispiele befinden sich in der HTTP-Datei unter `PDFfill/PDFfill.http`.

### POST /pdf/fields

Lieste alle Formularfelder (Name, Typ, Wert, Optionen, Seite, Position, Flags) als JSON-Liste.  
Bei passwortgeschützten PDFs kann das Öffnungs-Passwort übergeben werden.

**Formulardaten (multipart/form-data):**

| Name     | Typ    | Beschreibung                                   |
| -------- | ------ | ---------------------------------------------- |
| pdf      | Datei  | PDF-Datei (multipart/form-data)                |
| password | string | Passwort für das Öffnen einer passwortgeschützten PDF (optional) |

Beispiele befinden sich in der HTTP-Datei unter `PDFfill/PDFfill.http`.
