ANALIZA MIGRACIJE - Finalna Dokumentacija

  Datum: 2025-11-03Verzija: 2.0Status: Spremno za implementaciju

  ---
  📋 SADRŽAJ

  1. #pregled-strukture-foldera
  2. #format-dosijea
  3. #alfresco-properties
  4. #komponente-za-implementaciju
  5. #tok-migracije
  6. #model-podataka
  7. #akciona-lista

  ---
  PREGLED STRUKTURE FOLDERA

  Pre Migracije (stari Alfresco):

  Root/
  ├── DOSSIERS-PI/
  │   └── PI-102206/                    ← STARI dosije (SA "-")
  │       ├── a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf
  │       │   └── ecm:opisDokumenta = "KYC Questionnaire MDOC"
  │       └── d3f8a9b2-4c1e-4d6f-8e9a-1b2c3d4e5f6a.pdf
  │           └── ecm:opisDokumenta = "Personal Notice"
  │
  └── DOSSIERS-LE/
      └── LE-500342/                    ← STARI dosije (SA "-")
          ├── f1e2d3c4-b5a6-9780-cdef-123456789abc.pdf
          │   └── ecm:opisDokumenta = "Communication Consent"
          ├── a9b8c7d6-e5f4-3210-fedc-ba9876543210.pdf
          │   └── ecm:opisDokumenta = "KYC Questionnaire for LE"
          ├── b2c3d4e5-f6a7-8901-bcde-f12345678901.pdf
          │   └── ecm:opisDokumenta = "Current Accounts Contract"
          └── c3d4e5f6-a7b8-9012-cdef-012345678901.pdf
              └── ecm:opisDokumenta = "Specimen card"

  Posle Migracije (novi Alfresco):

  Root/ (ISTI ROOT!)
  ├── DOSSIERS-ACC/                     ← Kreiran tokom migracije
  │   └── ACC500342/                    ← NOVI dosije (BEZ "-")
  │       ├── b2c3d4e5-f6a7-8901-bcde-f12345678901.pdf
  │       │   └── ecm:opisDokumenta = "Current Accounts Contract"
  │       │   └── ecm:status = "validiran"
  │       │   └── ecm:source = "Heimdall"
  │       └── c3d4e5f6-a7b8-9012-cdef-012345678901.pdf
  │           └── ecm:opisDokumenta = "Specimen card"
  │           └── ecm:status = "validiran"
  │
  ├── DOSSIERS-LE/
  │   ├── LE-500342/                    ← STARI dosije (ostaje, netaknut)
  │   │   └── ... (originalni dokumenti)
  │   └── LE500342/                     ← NOVI dosije (BEZ "-")
  │       ├── f1e2d3c4-b5a6-9780-cdef-123456789abc.pdf
  │       │   └── ecm:opisDokumenta = "Izjava o kanalima komunikacije - migracija"
  │       │   └── ecm:status = "poništen"
  │       │   └── ecm:source = "Heimdall"
  │       └── a9b8c7d6-e5f4-3210-fedc-ba9876543210.pdf
  │           └── ecm:opisDokumenta = "KYC upitnik - migracija"
  │           └── ecm:status = "poništen"
  │
  └── DOSSIERS-PI/
      ├── PI-102206/                    ← STARI dosije (ostaje, netaknut)
      │   └── ... (originalni dokumenti)
      └── PI102206/                     ← NOVI dosije (BEZ "-")
          ├── a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf
          │   └── ecm:opisDokumenta = "KYC upitnik - migracija"
          │   └── ecm:status = "poništen"
          └── d3f8a9b2-4c1e-4d6f-8e9a-1b2c3d4e5f6a.pdf
              └── ecm:opisDokumenta = "GDPR saglasnost - migracija"
              └── ecm:status = "poništen"

  ---
  FORMAT DOSIJEA

  Pravila

  | Tip          | Format                     | Primer                             |
  |--------------|----------------------------|------------------------------------|
  | STARI dosije | {Prefix}-{CoreId} (SA "-") | PI-102206, LE-500342, ACC-13001926 |
  | NOVI dosije  | {Prefix}{CoreId} (BEZ "-") | PI102206, LE500342, ACC500342      |

  FolderDiscoveryService

  Već implementirano:
  WHERE name LIKE '%-%'

  Ovo filtrira i vraća samo STARE dosijee (sa "-" u nazivu).

  ---
  ALFRESCO PROPERTIES

  Prefiks: ecm:

  Svi custom properties iz Alfresca počinju sa ecm: prefiksom.

  Document Properties (iz starog Alfresca)

  | Property          | Opis                                | Primer                  |
  |-------------------|-------------------------------------|-------------------------|
  | ecm:opisDokumenta | Opis dokumenta (ključ za mapiranje) | "Communication Consent" |
  | ecm:tipDokumenta  | Tip dokumenta (šifra)               | "00842"                 |
  | ecm:tipDosijea    | Tip dosijea                         | "Dosije klijenta PL"    |
  | ecm:status        | Status dokumenta                    | "validiran", "poništen" |
  | ecm:source        | Izvor dokumenta                     | "Heimdall", "DUT"       |
  | ecm:clientSegment | Segment klijenta                    | "PI", "LE"              |
  | ecm:coreId        | Core ID klijenta                    | "102206"                |
  | ecm:createdAt     | Datum kreiranja                     | ISO 8601 format         |

  Folder Properties (dosije)

  | Property          | Opis                        | Primer           |
  |-------------------|-----------------------------|------------------|
  | ecm:coreId        | Core ID klijenta            | "102206"         |
  | ecm:clientSegment | Segment klijenta            | "PI", "LE"       |
  | ecm:clientName    | Ime klijenta (iz ClientAPI) | "Petar Petrović" |
  | ecm:clientType    | Tip klijenta (iz ClientAPI) | "Retail", "SME"  |

  ---
  KOMPONENTE ZA IMPLEMENTACIJU

  1. OpisToTipMapper

  Fajl: Migration.Infrastructure/Implementation/OpisToTipMapper.cs

  public static class OpisToTipMapper
  {
      /// <summary>
      /// Mapiranje: ecm:opisDokumenta → ecm:tipDokumenta
      /// </summary>
      private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
      {
          // SRPSKI OPISI
          { "GDPR saglasnost", "00849" },
          { "KYC upitnik", "00841" },
          { "Izjava o kanalima komunikacije", "00842" },
          { "KDP za fizička lica", "00824" },
          { "KDP za pravna lica", "00827" },
          { "KDP za ovlašćena lica (za fizička lica)", "00825" },
          { "Zahtev za otvaranje/izmenu paket računa", "00834" },
          { "Obaveštenje o predugovornoj fazi", "00838" },
          { "GL transakcije", "00844" },
          { "Zahtev za izmenu SMS info servisa", "00835" },
          { "Zahtev za izmenu SMS CA servisa", "00836" },
          { "FX transakcije", "00843" },
          { "GDPR povlačenje saglasnosti", "00840" },
          { "Zahtev za promenu email adrese putem mBankinga", "00847" },
          { "Zahtev za promenu broja telefona putem mBankinga", "00846" },
          { "Ugovor o tekućem računu", "00110" },
          { "Ugovor o tekućem deviznom računu", "00117" },
          { "Izjava o pristupu", "00845" },
          { "Pristanak za obradu ličnih podataka", "00849" },

          // ENGLESKI OPISI (iz starog Alfresca)
          { "Personal Notice", "00849" },
          { "KYC Questionnaire", "00841" },
          { "KYC Questionnaire MDOC", "00841" },
          { "KYC Questionnaire for LE", "00841" },
          { "Communication Consent", "00842" },
          { "Specimen card", "00824" },
          { "Specimen card for LE", "00827" },
          { "Specimen Card for Authorized Person", "00825" },
          { "Account Package", "00834" },
          { "Account Package RSD Instruction for Resident", "00834" },
          { "Pre-Contract Info", "00838" },
          { "GL Transaction", "00844" },
          { "SMS info modify request", "00835" },
          { "SMS card alarm change", "00836" },
          { "FX Transaction", "00843" },
          { "GDPR Revoke", "00840" },
          { "Contact Data Change Email", "00847" },
          { "Contact Data Change Phone", "00846" },
          { "Current Accounts Contract", "00110" },
          { "Current Account Contract for LE", "00110" },

          // DEPOSIT DOKUMENTI
          { "Ugovor o oročenom depozitu", "00008" },
          { "Ponuda", "00889" },
          { "Plan isplate depozita", "00879" },
          { "Obavezni elementi Ugovora", "00882" },
          { "PiVazeciUgovorOroceniDepozitDvojezicniRSD", "00008" },
          { "PiVazeciUgovorOroceniDepozitOstaleValute", "00008" },
          { "PiVazeciUgovorOroceniDepozitDinarskiTekuci", "00008" },
          { "PiVazeciUgovorOroceniDepozitNa36Meseci", "00008" },
          { "PiVazeciUgovorOroceniDepozitNa24MesecaRSD", "00008" },
          { "PiVazeciUgovorOroceniDepozitNa25Meseci", "00008" },
          { "PiPonuda", "00889" },
          { "PiAnuitetniPlan", "00879" },
          { "PiObavezniElementiUgovora", "00882" },
          { "ZahtevZaOtvaranjeRacunaOrocenogDepozita", "00890" }
      };

      public static string GetTipDokumenta(string opisDokumenta)
      {
          if (string.IsNullOrWhiteSpace(opisDokumenta))
              return "UNKNOWN";

          return Mappings.TryGetValue(opisDokumenta.Trim(), out var tipDokumenta)
              ? tipDokumenta
              : "UNKNOWN";
      }

      public static bool IsKnownOpis(string opisDokumenta)
      {
          if (string.IsNullOrWhiteSpace(opisDokumenta))
              return false;

          return Mappings.ContainsKey(opisDokumenta.Trim());
      }
  }

  ---
  2. DossierIdFormatter

  Fajl: Migration.Infrastructure/Implementation/DossierIdFormatter.cs

  public static class DossierIdFormatter
  {
      /// <summary>
      /// Konvertuje STARI format → NOVI format
      /// PI-102206 → PI102206
      /// LE-500342 → LE500342
      /// </summary>
      public static string ConvertToNewFormat(string oldDossierId)
      {
          if (string.IsNullOrWhiteSpace(oldDossierId))
              return string.Empty;

          return oldDossierId.Replace("-", "");
      }

      /// <summary>
      /// Parsira CoreId iz naziva dosijea
      /// PI-102206 → 102206
      /// PI102206 → 102206
      /// </summary>
      public static string ExtractCoreId(string dossierId)
      {
          if (string.IsNullOrWhiteSpace(dossierId))
              return string.Empty;

          var normalized = dossierId.Replace("-", "");
          var coreId = new string(normalized.SkipWhile(c => !char.IsDigit(c)).ToArray());

          return coreId;
      }

      /// <summary>
      /// Parsira prefix iz naziva dosijea
      /// PI-102206 → PI
      /// ACC500342 → ACC
      /// </summary>
      public static string ExtractPrefix(string dossierId)
      {
          if (string.IsNullOrWhiteSpace(dossierId))
              return string.Empty;

          var prefix = new string(dossierId.TakeWhile(c => !char.IsDigit(c) && c != '-').ToArray());

          return prefix.ToUpperInvariant();
      }

      /// <summary>
      /// Kreira NOVI dosije ID
      /// CreateNewDossierId("ACC", "500342") → "ACC500342"
      /// </summary>
      public static string CreateNewDossierId(string prefix, string coreId)
      {
          return $"{prefix.ToUpperInvariant()}{coreId}";
      }
  }

  ---
  3. DestinationRootFolderDeterminator

  Fajl: Migration.Infrastructure/Implementation/DestinationRootFolderDeterminator.cs

  public static class DestinationRootFolderDeterminator
  {
      /// <summary>
      /// Određuje destination root folder na osnovu:
      /// - ecm:tipDokumenta
      /// - ecm:tipDosijea
      /// - ecm:clientSegment
      /// </summary>
      public static string DetermineRootFolder(
          string tipDokumenta,
          string tipDosijea,
          string clientSegment)
      {
          // PRIORITET 1: Deposit → DOSSIERS-D
          if (tipDosijea?.Contains("Dosije depozita", StringComparison.OrdinalIgnoreCase) == true)
              return "DOSSIERS-D";

          // PRIORITET 2: Account Package → DOSSIERS-ACC
          if (tipDokumenta == "00834" || // Account Package
              tipDokumenta == "00102" ||
              tipDosijea?.Contains("Dosije paket računa", StringComparison.OrdinalIgnoreCase) == true)
              return "DOSSIERS-ACC";

          // PRIORITET 3: Na osnovu clientSegment
          if (clientSegment?.ToUpperInvariant() == "PI" ||
              clientSegment?.ToUpperInvariant() == "FL")
              return "DOSSIERS-PI";

          if (clientSegment?.ToUpperInvariant() == "LE" ||
              clientSegment?.ToUpperInvariant() == "PL")
              return "DOSSIERS-LE";

          // PRIORITET 4: Na osnovu tipDosijea
          if (tipDosijea?.Contains("fizičkog lica", StringComparison.OrdinalIgnoreCase) == true ||
              tipDosijea?.Contains("klijenta FL", StringComparison.OrdinalIgnoreCase) == true)
              return "DOSSIERS-PI";

          if (tipDosijea?.Contains("pravnog lica", StringComparison.OrdinalIgnoreCase) == true ||
              tipDosijea?.Contains("klijenta PL", StringComparison.OrdinalIgnoreCase) == true)
              return "DOSSIERS-LE";

          // FALLBACK
          return "DOSSIERS-UNKNOWN";
      }
  }

  ---
  4. DocumentStatusDetector

  Fajl: Migration.Infrastructure/Implementation/DocumentStatusDetector.cs

  public static class DocumentStatusDetector
  {
      /// <summary>
      /// Određuje da li dokument treba da bude aktivan
      /// Proverava sufiks "-migracija" u ecm:opisDokumenta
      /// </summary>
      public static bool ShouldBeActive(
          string? opisDokumenta,
          string? existingStatus = null)
      {
          // TC 11: Provera starog statusa
          if (!string.IsNullOrWhiteSpace(existingStatus))
          {
              var normalized = existingStatus.Trim().ToLowerInvariant();
              if (normalized == "poništen" ||
                  normalized == "inactive" ||
                  normalized == "cancelled" ||
                  normalized == "canceled")
                  return false;
          }

          // TC 1 & 2: Provera sufiksa "-migracija" u OPISU
          if (!string.IsNullOrWhiteSpace(opisDokumenta))
          {
              if (opisDokumenta.Contains(" - migracija", StringComparison.OrdinalIgnoreCase) ||
                  opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase))
                  return false;
          }

          return true;
      }

      public static string GetAlfrescoStatus(bool isActive)
      {
          return isActive ? "validiran" : "poništen";
      }

      /// <summary>
      /// Vraća kompletne informacije o statusu dokumenta
      /// </summary>
      public static DocumentStatusInfo GetStatusInfo(
          string? opisDokumenta,
          string? existingStatus = null)
      {
          var isActive = ShouldBeActive(opisDokumenta, existingStatus);
          var status = GetAlfrescoStatus(isActive);

          var hasMigrationSuffix = !string.IsNullOrWhiteSpace(opisDokumenta) &&
              (opisDokumenta.Contains(" - migracija", StringComparison.OrdinalIgnoreCase) ||
               opisDokumenta.Contains("-migracija", StringComparison.OrdinalIgnoreCase));

          return new DocumentStatusInfo
          {
              IsActive = isActive,
              Status = status,
              HasMigrationSuffixInOpis = hasMigrationSuffix,
              WasInactiveInOldSystem = !string.IsNullOrWhiteSpace(existingStatus) &&
                  existingStatus.Contains("poništen", StringComparison.OrdinalIgnoreCase)
          };
      }
  }

  public record DocumentStatusInfo
  {
      public bool IsActive { get; init; }
      public string Status { get; init; } = string.Empty;
      public bool HasMigrationSuffixInOpis { get; init; }
      public bool WasInactiveInOldSystem { get; init; }
  }

  ---
  5. SourceDetector

  Fajl: Migration.Infrastructure/Implementation/SourceDetector.cs

  public static class SourceDetector
  {
      /// <summary>
      /// Određuje ecm:source na osnovu destination root foldera
      /// TC 6: Heimdall za DOSSIERS-PI/LE/ACC
      /// TC 7: DUT za DOSSIERS-D
      /// </summary>
      public static string GetSource(string destinationRootFolder)
      {
          if (destinationRootFolder == "DOSSIERS-D")
              return "DUT";

          return "Heimdall";
      }
  }

  ---
  TOK MIGRACIJE

  Faza 1: FolderDiscovery (već implementirano)

  ┌─────────────────────────────────────────────────────────┐
  │ SQL Upit: WHERE name LIKE '%-%'                         │
  │ Vraća samo STARE dosijee                                │
  └─────────────────────────┬───────────────────────────────┘
                            │
                            ▼
            ┌───────────────────────────────────┐
            │ Za svaki STARI dosije:            │
            │ - PI-102206                       │
            │ - LE-500342                       │
            │ - ACC-13001926                    │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ Parsiranje:                       │
            │ - CoreId = ExtractCoreId()        │
            │ - Prefix = ExtractPrefix()        │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ Snimanje u FOLDER_STAGING         │
            └───────────────────────────────────┘

  ---
  Faza 2: DocumentDiscovery

  ┌─────────────────────────────────────────────────────────┐
  │ Čitanje dokumenta iz STAROG dosijea                     │
  └─────────────────────────┬───────────────────────────────┘
                            │
                            ▼
            ┌───────────────────────────────────┐
            │ Čitanje Alfresco properties:      │
            │ - ecm:opisDokumenta               │
            │ - ecm:tipDokumenta                │
            │ - ecm:tipDosijea                  │
            │ - ecm:status                      │
            │ - ecm:clientSegment               │
            │ - ecm:coreId                      │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ DetermineTipDokumenta():          │
            │ 1. Proveri ecm:tipDokumenta       │
            │ 2. Ako nema → koristi             │
            │    OpisToTipMapper                │
            │    (ecm:opisDokumenta)            │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ DocumentStatusDetector:           │
            │ Provera "-migracija" u            │
            │ ecm:opisDokumenta                 │
            └───────────┬───────────────────────┘
                        │
           ┌────────────┴────────────┐
           │ Ima sufiks?             │
           ▼                         ▼
  ┌────────────────────┐    ┌────────────────────┐
  │ Status = poništen  │    │ Status = validiran │
  └────────┬───────────┘    └────────┬───────────┘
           │                         │
           └────────────┬────────────┘
                        ▼
            ┌───────────────────────────────────┐
            │ DestinationRootFolderDeterminator │
            │ Određuje novi root folder         │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ Kreiranje NOVOG dosije ID-a:      │
            │ - Isti root → ukloni "-"          │
            │ - Promena root → novi prefix      │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ SourceDetector.GetSource()        │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ Snimanje u DOC_STAGING            │
            └───────────────────────────────────┘

  ---
  Faza 3: Move

  ┌─────────────────────────────────────────────────────────┐
  │ Čitanje batch-a iz DOC_STAGING                          │
  └─────────────────────────┬───────────────────────────────┘
                            │
                            ▼
            ┌───────────────────────────────────┐
            │ Provera da li root folder postoji │
            │ (npr. DOSSIERS-ACC)               │
            └───────────┬───────────────────────┘
                        │
           ┌────────────┴────────────┐
           │ NE                      │ DA
           ▼                         ▼
  ┌────────────────────┐    ┌────────────────────┐
  │ Kreiraj root folder│    │ Nastavi            │
  └────────┬───────────┘    └────────┬───────────┘
           │                         │
           └────────────┬────────────┘
                        ▼
            ┌───────────────────────────────────┐
            │ Provera da li novi dosije postoji │
            │ (npr. ACC500342)                  │
            └───────────┬───────────────────────┘
                        │
           ┌────────────┴────────────┐
           │ NE (TC 8)               │ DA (TC 9)
           ▼                         ▼
  ┌────────────────────┐    ┌────────────────────┐
  │ Kreiraj dosije     │    │ Nastavi            │
  │ + ClientAPI data   │    │                    │
  └────────┬───────────┘    └────────┬───────────┘
           │                         │
           └────────────┬────────────┘
                        ▼
            ┌───────────────────────────────────┐
            │ COPY dokument sa properties:      │
            │ - ecm:opisDokumenta               │
            │ - ecm:tipDokumenta                │
            │ - ecm:tipDosijea                  │
            │ - ecm:status                      │
            │ - ecm:source                      │
            │ - ecm:coreId                      │
            │ - ecm:clientSegment               │
            └───────────┬───────────────────────┘
                        │
                        ▼
            ┌───────────────────────────────────┐
            │ Update DOC_STAGING:               │
            │ MigrationStatus = "DONE"          │
            └───────────────────────────────────┘

  ---
  MODEL PODATAKA

  DocStaging Model

  Fajl: Migration.Abstraction/Models/DocStaging.cs

  public class DocStaging
  {
      public long Id { get; set; }

      // ========================================
      // STARI LOKACIJA
      // ========================================

      /// <summary>
      /// STARI dosije ID (SA "-")
      /// Primer: "PI-102206", "LE-500342"
      /// </summary>
      public string OldDossierId { get; set; }

      /// <summary>
      /// STARI root folder
      /// Primer: "DOSSIERS-PI", "DOSSIERS-LE"
      /// </summary>
      public string OldRootFolder { get; set; }

      /// <summary>
      /// Alfresco node ID originalnog dokumenta
      /// </summary>
      public string OldDocumentNodeId { get; set; }

      // ========================================
      // NOVI LOKACIJA
      // ========================================

      /// <summary>
      /// NOVI dosije ID (BEZ "-")
      /// Primer: "PI102206", "LE500342", "ACC500342"
      /// </summary>
      public string NewDossierId { get; set; }

      /// <summary>
      /// NOVI root folder (može biti isti ili različit)
      /// Primer: "DOSSIERS-PI", "DOSSIERS-ACC"
      /// </summary>
      public string NewRootFolder { get; set; }

      // ========================================
      // DOCUMENT DATA (iz Alfresca)
      // ========================================

      /// <summary>
      /// Naziv dokumenta (često GUID)
      /// Primer: "a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf"
      /// </summary>
      public string DocumentName { get; set; }

      /// <summary>
      /// ecm:opisDokumenta - ključ za mapiranje
      /// Primer: "Communication Consent", "KYC upitnik - migracija"
      /// </summary>
      public string OpisDokumenta { get; set; }

      /// <summary>
      /// ecm:tipDokumenta - šifra dokumenta
      /// Primer: "00842", "00841", "00824"
      /// </summary>
      public string TipDokumenta { get; set; }

      /// <summary>
      /// ecm:tipDosijea
      /// Primer: "Dosije klijenta PL", "Dosije paket računa"
      /// </summary>
      public string TipDosijea { get; set; }

      /// <summary>
      /// ecm:status - status nakon migracije
      /// Vrednosti: "validiran", "poništen"
      /// </summary>
      public string Status { get; set; }

      /// <summary>
      /// ecm:source - izvor dokumenta
      /// Vrednosti: "Heimdall", "DUT"
      /// </summary>
      public string Source { get; set; }

      // ========================================
      // CLIENT DATA
      // ========================================

      /// <summary>
      /// ecm:coreId - Core ID klijenta
      /// Primer: "102206", "500342"
      /// </summary>
      public string CoreId { get; set; }

      /// <summary>
      /// ecm:clientSegment - segment klijenta
      /// Vrednosti: "PI", "LE", "FL", "PL"
      /// </summary>
      public string ClientSegment { get; set; }

      // ========================================
      // METADATA
      // ========================================

      /// <summary>
      /// ecm:createdAt - originalni datum kreiranja
      /// </summary>
      public DateTime? OriginalCreatedAt { get; set; }

      /// <summary>
      /// Datum kada je dokument otkriven tokom discovery faze
      /// </summary>
      public DateTime DiscoveredAt { get; set; }

      /// <summary>
      /// Datum kada je dokument uspešno migriran
      /// </summary>
      public DateTime? MigratedAt { get; set; }

      /// <summary>
      /// Status migracije
      /// Vrednosti: "PENDING", "IN_PROGRESS", "DONE", "ERROR"
      /// </summary>
      public string MigrationStatus { get; set; }

      /// <summary>
      /// Poruka greške (ako postoji)
      /// </summary>
      public string? ErrorMessage { get; set; }
  }

  ---
  AKCIONA LISTA

  ✅ Već Implementirano

  - ✅ FolderDiscoveryService sa WHERE name LIKE '%-%'
  - ✅ Infrastruktura Worker-a
  - ✅ Progress tracking i UI

  🔴 Za Implementaciju - PRIORITET 1

  1. Kreirati nove fajlove

  - Migration.Infrastructure/Implementation/OpisToTipMapper.cs
  - Migration.Infrastructure/Implementation/DossierIdFormatter.cs
  - Migration.Infrastructure/Implementation/DestinationRootFolderDeterminator.cs
  - Migration.Infrastructure/Implementation/DocumentStatusDetector.cs
  - Migration.Infrastructure/Implementation/SourceDetector.cs

  2. Izmeniti postojeće fajlove

  DocumentDiscoveryService.cs:
  private string DetermineTipDokumenta(AlfrescoDocument doc)
  {
      // 1. Proveri ecm:tipDokumenta
      var existingTip = doc.Properties.GetValueOrDefault("ecm:tipDokumenta");
      if (!string.IsNullOrWhiteSpace(existingTip))
          return existingTip;

      // 2. Mapiranje iz ecm:opisDokumenta
      var opisDokumenta = doc.Properties.GetValueOrDefault("ecm:opisDokumenta");
      if (!string.IsNullOrWhiteSpace(opisDokumenta))
      {
          var tipFromOpis = OpisToTipMapper.GetTipDokumenta(opisDokumenta);
          if (tipFromOpis != "UNKNOWN")
              return tipFromOpis;
      }

      return "UNKNOWN";
  }

  private async Task ProcessDocumentAsync(AlfrescoDocument doc, FolderStaging folder)
  {
      // Čitanje properties (sa "ecm:" prefiksom)
      var opisDokumenta = doc.Properties.GetValueOrDefault("ecm:opisDokumenta");
      var tipDosijea = doc.Properties.GetValueOrDefault("ecm:tipDosijea");
      var existingStatus = doc.Properties.GetValueOrDefault("ecm:status");
      var clientSegment = doc.Properties.GetValueOrDefault("ecm:clientSegment");

      // Fallback na ClientAPI
      if (string.IsNullOrWhiteSpace(clientSegment))
      {
          var clientData = await _clientApi.GetClientDetailExtendedAsync(folder.CoreId);
          clientSegment = clientData?.Segment;
      }

      // Određivanje TipDokumenta
      var tipDokumenta = DetermineTipDokumenta(doc);

      // Određivanje statusa
      var isActive = DocumentStatusDetector.ShouldBeActive(opisDokumenta, existingStatus);
      var status = DocumentStatusDetector.GetAlfrescoStatus(isActive);

      // Određivanje destination root foldera
      var newRootFolder = DestinationRootFolderDeterminator.DetermineRootFolder(
          tipDokumenta,
          tipDosijea,
          clientSegment
      );

      // Kreiranje novog dosije ID-a
      string newDossierId;
      if (newRootFolder == folder.OldRootFolder)
      {
          newDossierId = DossierIdFormatter.ConvertToNewFormat(folder.OldDossierId);
      }
      else
      {
          var newPrefix = newRootFolder.Replace("DOSSIERS-", "");
          var coreId = DossierIdFormatter.ExtractCoreId(folder.OldDossierId);
          newDossierId = DossierIdFormatter.CreateNewDossierId(newPrefix, coreId);
      }

      // Određivanje source
      var source = SourceDetector.GetSource(newRootFolder);

      // Snimanje u DOC_STAGING
      var docStaging = new DocStaging
      {
          OldDossierId = folder.OldDossierId,
          OldRootFolder = folder.OldRootFolder,
          OldDocumentNodeId = doc.Id,
          NewDossierId = newDossierId,
          NewRootFolder = newRootFolder,
          DocumentName = doc.Name,
          OpisDokumenta = opisDokumenta,
          TipDokumenta = tipDokumenta,
          TipDosijea = tipDosijea,
          Status = status,
          Source = source,
          CoreId = folder.CoreId,
          ClientSegment = clientSegment,
          OriginalCreatedAt = doc.CreatedAt,
          DiscoveredAt = DateTime.UtcNow,
          MigrationStatus = "PENDING"
      };

      await _dbContext.DocStaging.AddAsync(docStaging);
  }

  MoveService.cs:
  private async Task MoveDocumentAsync(DocStaging doc)
  {
      // 1. Osiguraj da root folder postoji
      var rootFolderNodeId = await EnsureRootFolderExistsAsync(doc.NewRootFolder);

      // 2. Osiguraj da dosije postoji
      var newDossierNodeId = await EnsureDossierExistsAsync(
          rootFolderNodeId,
          doc.NewDossierId,
          doc.CoreId,
          doc.ClientSegment
      );

      // 3. COPY dokument sa "ecm:" properties
      await CopyDocumentAsync(
          sourceNodeId: doc.OldDocumentNodeId,
          destinationFolderNodeId: newDossierNodeId,
          documentName: doc.DocumentName,
          properties: new Dictionary<string, string>
          {
              { "ecm:opisDokumenta", doc.OpisDokumenta },
              { "ecm:tipDokumenta", doc.TipDokumenta },
              { "ecm:tipDosijea", doc.TipDosijea },
              { "ecm:status", doc.Status },
              { "ecm:source", doc.Source },
              { "ecm:coreId", doc.CoreId },
              { "ecm:clientSegment", doc.ClientSegment },
              { "ecm:originalCreatedAt", doc.OriginalCreatedAt?.ToString("o") }
          }
      );

      // 4. Update staging
      doc.MigrationStatus = "DONE";
      doc.MigratedAt = DateTime.UtcNow;
      await _dbContext.SaveChangesAsync();
  }

  private async Task<string> EnsureDossierExistsAsync(
      string rootFolderNodeId,
      string newDossierId,
      string coreId,
      string clientSegment)
  {
      var existingDossier = await _alfrescoApi.SearchFolderByNameAsync(
          parentId: rootFolderNodeId,
          folderName: newDossierId
      );

      if (existingDossier != null)
          return existingDossier.Id;

      // TC 8: Kreiranje dosijea sa ClientAPI podacima
      var clientData = await _clientApi.GetClientDetailExtendedAsync(coreId);

      var newDossier = await _alfrescoApi.CreateFolderAsync(
          parentId: rootFolderNodeId,
          folderName: newDossierId,
          properties: new Dictionary<string, string>
          {
              { "ecm:coreId", coreId },
              { "ecm:clientSegment", clientSegment },
              { "ecm:clientName", clientData?.Name },
              { "ecm:clientType", clientData?.Type }
          }
      );

      return newDossier.Id;
  }

  3. Model izmene

  - Migration.Abstraction/Models/DocStaging.cs - dodati nove propertije

  4. Konfiguracija

  appsettings.json:
  {
    "Migration": {
      "ExcludedDocumentTypes": ["00702"],
      "UnknownDocumentHandling": {
        "Strategy": "MoveToUnknownFolder",
        "MarkAsInactive": true,
        "LogWarning": true
      }
    }
  }

  ---
  PRIMERI MIGRACIJE

  Primer 1: Isti root folder (PI → PI)

  STARI:
  Root/DOSSIERS-PI/PI-102206/a1b2c3d4.pdf
  ecm:opisDokumenta = "KYC Questionnaire MDOC"
  ecm:clientSegment = "PI"

  PROCES:
  - OpisToTipMapper("KYC Questionnaire MDOC") → "00841"
  - DocumentStatusDetector(opisDokumenta) → nema "-migracija" → aktivan
  - DestinationRootFolderDeterminator → "DOSSIERS-PI" (isti)
  - DossierIdFormatter.ConvertToNewFormat("PI-102206") → "PI102206"

  NOVI:
  Root/DOSSIERS-PI/PI102206/a1b2c3d4.pdf
  ecm:opisDokumenta = "KYC Questionnaire MDOC"
  ecm:tipDokumenta = "00841"
  ecm:status = "validiran"
  ecm:source = "Heimdall"

  ---
  Primer 2: Promena root foldera (LE → ACC)

  STARI:
  Root/DOSSIERS-LE/LE-500342/b2c3d4e5.pdf
  ecm:opisDokumenta = "Current Accounts Contract"
  ecm:tipDokumenta = "00110"

  PROCES:
  - DestinationRootFolderDeterminator → "DOSSIERS-ACC" (promena!)
  - DossierIdFormatter.CreateNewDossierId("ACC", "500342") → "ACC500342"

  NOVI:
  Root/DOSSIERS-ACC/ACC500342/b2c3d4e5.pdf
  ecm:opisDokumenta = "Current Accounts Contract"
  ecm:tipDokumenta = "00110"
  ecm:status = "validiran"
  ecm:source = "Heimdall"

  ---
  Primer 3: Dokument sa sufiksom "-migracija"

  STARI:
  Root/DOSSIERS-LE/LE-500342/f1e2d3c4.pdf
  ecm:opisDokumenta = "Communication Consent - migracija"

  PROCES:
  - DocumentStatusDetector → sadrži "-migracija" → neaktivan
  - Status = "poništen"

  NOVI:
  Root/DOSSIERS-LE/LE500342/f1e2d3c4.pdf
  ecm:opisDokumenta = "Communication Consent - migracija"
  ecm:status = "poništen"
  ecm:source = "Heimdall"

  ---
  ZAKLJUČAK

  ✅ Spremno za implementaciju

  - Struktura foldera jasno definisana
  - Format dosijea: STARI (SA "-") → NOVI (BEZ "-")
  - Alfresco properties sa ecm: prefiksom
  - Sve komponente specifikovane
  - Tok migracije dokumentovan
  - Model podataka definisan

  🎯 Sledeći koraci

  1. Kreirati 5 novih fajlova (maperi i detektori)
  2. Izmeniti DocumentDiscoveryService.cs
  3. Izmeniti MoveService.cs
  4. Dodati propertije u DocStaging model
  5. Testirati sa realnim podacima

  Status: 🚀 Spremno za kodiranje!

  ---
  Verzija dokumenta: 2.0Datum poslednje izmene: 2025-11-03Prefiks properties: ecm: