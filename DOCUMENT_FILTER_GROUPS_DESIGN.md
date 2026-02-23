# Design: Napredno grupno filtriranje dokumenata u DocumentSelectionWindow

## Kontekst

U tabeli `DocumentMappings` postoji ~70k+ redova gde mnogi dokumenti dele isti
"bazni tip" razlikujući se samo po broju računa, npr:

- `TEK RACUN 1`, `TEK RACUN 2`, ... `TEK RACUN 70000` (~70k varijanti)
- `GOLD PAKET RACUN 1`, ... `GOLD PAKET RACUN 1000` (~1k varijanti)
- I još ~10 sličnih slučajeva

**Problem:** Ako korisnik selektuje celu grupu "TEK RACUN", Alfresco AFTS query
bi imao 70k OR uslova → timeout/greška.

**Rešenje:** Automatska detekcija grupnih tipova u SQL-u (bez izmene sheme baze),
prikaz u grupisanom modu UI-a, i generisanje wildcard Alfresco AFTS querija.

---

## Pregled izmena po slojevima

```
Sloj            │ Fajlovi
────────────────┼──────────────────────────────────────────────────────
Contracts       │ + DocumentGroupType.cs          (novi)
                │ + GroupSelection.cs             (novi)
                │ + DocumentSelectionResult.cs    (novi)
────────────────┼──────────────────────────────────────────────────────
Repository      │ ~ IDocumentMappingRepository.cs (+2 metode)
(SQL Server)    │ ~ DocumentMappingRepository.cs  (implementacija)
────────────────┼──────────────────────────────────────────────────────
Service         │ ~ IDocumentSearchService.cs     (+1 metoda)
(Migration)     │ ~ DocumentSearchService.cs      (izmene query + filter)
────────────────┼──────────────────────────────────────────────────────
UI (WPF)        │ ~ DocumentSelectionWindow.xaml  (novi mod)
                │ ~ DocumentSelectionWindow.xaml.cs
                │ + GroupDrilldownWindow.xaml     (novi popup)
                │ + GroupDrilldownWindow.xaml.cs  (novi popup)
                │ ~ MigrationPhaseMonitor.xaml.cs (koristi novi result)
```

---

## 1. Novi Contract modeli

### `Alfresco.Contracts/Models/DocumentGroupType.cs`

DTO koji vraća repozitorijum — predstavlja jednu otkrivenu grupu dokumenata.

```csharp
public class DocumentGroupType
{
    public string BaseNaziv { get; set; } = "";   // "TEK RACUN"
    public int VariantCount { get; set; }          // 70000
    public long TotalDocuments { get; set; }       // suma BROJ_DOKUMENATA svih varijanti
    public string? TipDosijea { get; set; }
}
```

---

### `Alfresco.Contracts/Models/GroupSelection.cs`

Predstavlja korisnikovu nameru selekcije grupe. Čuva se kao deo `DocumentSelectionResult`.

```csharp
public class GroupSelection
{
    public string BaseNaziv { get; set; } = "";  // "TEK RACUN"
    public string? InvoiceFilter { get; set; }    // null = uzmi sve, "5" = 5*, "51" = 51*
    public int VariantCount { get; set; }          // za prikaz u UI

    // Alfresco AFTS wildcard pattern (BEZ = prefiksa!)
    public string ToAlfrescoPattern() =>
        InvoiceFilter == null
            ? $"{BaseNaziv} *"
            : $"{BaseNaziv} {InvoiceFilter}*";

    // Za prikaz u desnom panelu (Selected)
    public string DisplayName =>
        InvoiceFilter == null
            ? $"{BaseNaziv} (sve ~{VariantCount:N0} var.)"
            : $"{BaseNaziv} {InvoiceFilter}* (filter)";
}
```

---

### `Alfresco.Contracts/Models/DocumentSelectionResult.cs`

Novi return type iz `DocumentSelectionWindow` — zamenjuje `List<string>`.

```csharp
public class DocumentSelectionResult
{
    // Individualni dokumenti → exact match u Alfresco (=ecm:docDesc:"...")
    public List<string> ExactDescriptions { get; set; } = new();

    // Grupne selekcije → wildcard match u Alfresco (ecm:docDesc:"BaseNaziv *")
    public List<GroupSelection> GroupSelections { get; set; } = new();

    public bool HasAny => ExactDescriptions.Any() || GroupSelections.Any();
    public int TotalSelectionCount => ExactDescriptions.Count + GroupSelections.Count;
}
```

---

## 2. Repository sloj

### Dve nove metode u `IDocumentMappingRepository`

```csharp
/// <summary>
/// Vraća grupisane prikaz dokumenata:
///   - GROUP redovi: dokumenti čiji NAZIV prati "BaseNaziv <broj>" pattern, 2+ varijanti
///   - SINGLE redovi: svi ostali (bez numeričkog sufiksa, ili singleton)
/// Bez izmene sheme baze — logika je u SQL-u.
/// </summary>
Task<(IReadOnlyList<GroupedDocumentRow> Items, int TotalCount)> GetGroupedViewAsync(
    string? searchText,
    string? tipDosijea,
    int pageNumber,
    int pageSize,
    CancellationToken ct = default);

/// <summary>
/// Straničena pretraga dokumenata unutar jedne grupe.
/// Koristi NAZIV LIKE 'BaseNaziv %' → koristi postojeći index.
/// </summary>
Task<(IReadOnlyList<DocumentMapping> Items, int TotalCount)> SearchWithinGroupAsync(
    string baseNaziv,
    string? invoiceNumberFilter,  // null = svi, "5" = počinju sa 5
    int pageNumber,
    int pageSize,
    CancellationToken ct = default);
```

> **Napomena:** `GroupedDocumentRow` je DTO koji objedinjuje GROUP i SINGLE redove
> (videti sekciju 5. UI ViewModeli).

---

### SQL logika: detekcija BaseNaziv bez ALTER TABLE

Princip: ako **poslednji token u NAZIV** (iza zadnjeg razmaka) sadrži samo cifre
→ taj dokument je varijanta grupe.

```sql
-- Ekstrakcija BaseNaziv (SQL Server string funkcije):
-- Za "TEK RACUN 123":
--   REVERSE(NAZIV) = "321 NUCAR KET"
--   CHARINDEX(' ', REVERSE(NAZIV)) = 4   ← pozicija zadnjeg razmaka (od kraja)
--   Poslednji token = SUBSTRING(NAZIV, LEN-4+2, LEN) = "123"
--   BaseNaziv = LEFT(NAZIV, LEN-4) = "TEK RACUN"

CASE
    WHEN CHARINDEX(' ', NAZIV) > 0
     AND LEN(SUBSTRING(NAZIV,
             LEN(NAZIV) + 2 - CHARINDEX(' ', REVERSE(NAZIV)),
             LEN(NAZIV))) > 0
     AND SUBSTRING(NAZIV,
             LEN(NAZIV) + 2 - CHARINDEX(' ', REVERSE(NAZIV)),
             LEN(NAZIV)) NOT LIKE '%[^0-9]%'   -- poslednji token = samo cifre
    THEN LEFT(NAZIV, LEN(NAZIV) - CHARINDEX(' ', REVERSE(NAZIV)))
    ELSE NULL
END AS BaseNaziv
```

Primeri:

| NAZIV | Poslednji token | BaseNaziv (rezultat) |
|---|---|---|
| `TEK RACUN 123` | `123` ✓ | `TEK RACUN` |
| `GOLD PAKET RACUN 1000` | `1000` ✓ | `GOLD PAKET RACUN` |
| `TEK RACUN` | `RACUN` ✗ | `NULL` — izuzet |
| `UGOVOR O KREDITU` | `KREDITU` ✗ | `NULL` — izuzet |
| `IZJAVA SAGLASNOST 1` | `1` ✓ ali singleton | prikazuje se kao SINGLE |

---

### Puna SQL implementacija `GetGroupedViewAsync`

```sql
WITH DocWithBase AS (
    -- Korak 1: svaki red dobija BaseNaziv (ili NULL ako nije varijanta)
    SELECT
        ID,
        NAZIV,
        ISNULL(BROJ_DOKUMENATA, 0) AS BROJ_DOKUMENATA,
        TipDosijea,
        SifraDokumenta,
        CASE
            WHEN CHARINDEX(' ', NAZIV) > 0
             AND LEN(SUBSTRING(NAZIV,
                     LEN(NAZIV) + 2 - CHARINDEX(' ', REVERSE(NAZIV)),
                     LEN(NAZIV))) > 0
             AND SUBSTRING(NAZIV,
                     LEN(NAZIV) + 2 - CHARINDEX(' ', REVERSE(NAZIV)),
                     LEN(NAZIV)) NOT LIKE '%[^0-9]%'
            THEN LEFT(NAZIV, LEN(NAZIV) - CHARINDEX(' ', REVERSE(NAZIV)))
            ELSE NULL
        END AS BaseNaziv
    FROM DocumentMappings WITH (NOLOCK)
    WHERE NAZIV IS NOT NULL
      AND (@hasSearch = 0 OR NAZIV LIKE @searchPattern)
      AND (@hasTipDosijea = 0 OR TipDosijea = @tipDosijea)
),
GroupCounts AS (
    -- Korak 2: broj varijanti po BaseNaziv
    SELECT BaseNaziv, COUNT(*) AS Cnt
    FROM DocWithBase
    WHERE BaseNaziv IS NOT NULL
    GROUP BY BaseNaziv
),
Result AS (
    -- Grana A: grupe sa 2+ varijanti → jedan sažeti red
    SELECT
        'GROUP'           AS RowType,
        gc.BaseNaziv      AS DisplayNaziv,
        gc.Cnt            AS VariantCount,
        SUM(d.BROJ_DOKUMENATA) AS TotalDocuments,
        MIN(d.TipDosijea) AS TipDosijea,
        NULL              AS SifraDokumenta,
        NULL              AS ID
    FROM DocWithBase d
    INNER JOIN GroupCounts gc ON d.BaseNaziv = gc.BaseNaziv AND gc.Cnt > 1
    GROUP BY gc.BaseNaziv, gc.Cnt

    UNION ALL

    -- Grana B: individualni dokumenti
    --   (bez numeričkog sufiksa, ILI singleton — jedina varijanta svog BaseNaziv-a)
    SELECT
        'SINGLE'          AS RowType,
        d.NAZIV           AS DisplayNaziv,
        1                 AS VariantCount,
        d.BROJ_DOKUMENATA AS TotalDocuments,
        d.TipDosijea,
        d.SifraDokumenta,
        d.ID
    FROM DocWithBase d
    LEFT JOIN GroupCounts gc ON d.BaseNaziv = gc.BaseNaziv
    WHERE d.BaseNaziv IS NULL        -- nema numerički sufiks
       OR ISNULL(gc.Cnt, 1) = 1     -- singleton
)

-- Count query (zasebno):
SELECT COUNT(*) FROM Result;

-- Data query (zasebno, paginovano):
SELECT * FROM Result
ORDER BY DisplayNaziv
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
```

---

### `SearchWithinGroupAsync` SQL

Jednostavniji — koristi LIKE koji može da iskoristi index na NAZIV:

```sql
-- WHERE uslov:
WHERE NAZIV LIKE @baseNazivLike          -- npr. 'TEK RACUN %'
  AND CHARINDEX(' ', NAZIV) > 0
  AND SUBSTRING(NAZIV, LEN(NAZIV)+2-CHARINDEX(' ',REVERSE(NAZIV)), LEN(NAZIV))
      NOT LIKE '%[^0-9]%'                -- verifikacija da suffix = samo cifre
  AND (@hasInvoiceFilter = 0
       OR NAZIV LIKE @invoiceFilterLike) -- npr. 'TEK RACUN 5%'
ORDER BY NAZIV
OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

-- Parametri:
--   @baseNazivLike     = 'TEK RACUN %'       (uvek)
--   @invoiceFilterLike = 'TEK RACUN 5%'      (samo kad InvoiceFilter != null)
```

> **Performanse:** `@baseNazivLike` počinje sa konkretnom vrednošću (ne `%`),
> pa SQL Server može da koristi index na NAZIV koloni za range scan.

> **Keširanje:** `GetGroupedViewAsync` je kandidat za kratko keširanje (5–15 min)
> jer uključuje full scan sa CASE expression. `SearchWithinGroupAsync` ne keširamo
> jer zavisi od korisničkog unosa.

---

## 3. Service sloj

### Nova metoda u `IDocumentSearchService`

```csharp
/// <summary>
/// Postavlja selekciju sa podrškom za grupne wildcard selekcije.
/// Zamenjuje SetDocDescriptions() kada selekcija sadrži grupe.
/// SetDocDescriptions() ostaje za backwards compat (manual text unos).
/// </summary>
void SetDocumentSelection(DocumentSelectionResult selection);
```

---

### Izmene u `DocumentSearchService`

**Novo interno stanje:**
```csharp
private DocumentSelectionResult? _selectionOverride = null;
// Postojeće ostaje nepromenjeno:
private List<string>? _docDescriptionsOverride = null;
```

**Izmena `BuildDocumentSearchQuery`** — prima `DocumentSelectionResult`:

```csharp
private string BuildDocumentSearchQuery(string ancestorId, DocumentSelectionResult selection)
{
    var conditions = new List<string>();

    // Individualni dokumenti → exact match, SA = prefiksom (kao i sada)
    foreach (var desc in selection.ExactDescriptions)
        conditions.Add($"=ecm\\:docDesc:\"{EscapeAfts(desc)}\"");

    // Grupne selekcije → wildcard, BEZ = prefiksa
    // =ecm:docDesc ne podržava wildcard; bez = koristi full-text sa wildcard
    foreach (var group in selection.GroupSelections)
        conditions.Add($"ecm\\:docDesc:\"{EscapeAfts(group.ToAlfrescoPattern())}\"");

    var query = $"({string.Join(" OR ", conditions)}) " +
                $"AND ANCESTOR:\"{ancestorId}\" " +
                $"AND TYPE:\"cm:content\"";

    // date filter ostaje isti...
    return query;
}
```

**Izmena post-filter verifikacije** (trenutno ~linija 172 u `RunBatchAsync`):

```csharp
// Staro (samo exact):
var docDescHashSet = new HashSet<string>(docDescriptions, StringComparer.OrdinalIgnoreCase);
finalDocuments = docs.Where(o => docDescHashSet.Contains(docDesc)).ToList();

// Novo (exact + wildcard regex):
var verifier = BuildDocDescVerifier(_selectionOverride, docDescriptions);
finalDocuments = docs.Where(o => verifier(docDesc)).ToList();
```

```csharp
private static Func<string, bool> BuildDocDescVerifier(
    DocumentSelectionResult? selection,
    List<string> legacyList)
{
    if (selection == null)
    {
        // Legacy path — backwards compat
        var hashSet = new HashSet<string>(legacyList, StringComparer.OrdinalIgnoreCase);
        return desc => hashSet.Contains(desc);
    }

    var exactSet = new HashSet<string>(
        selection.ExactDescriptions,
        StringComparer.OrdinalIgnoreCase);

    // Regex za svaku grupu: "BaseNaziv <cifre>" na kraju stringa
    // Ovo je post-filter zaštita — Alfresco wildcard može da vrati false positives
    var groupRegexes = selection.GroupSelections
        .Select(g => new Regex(
            // InvoiceFilter=null:   "^TEK RACUN \d+$"
            // InvoiceFilter="5":    "^TEK RACUN 5\d*$"
            $@"^{Regex.Escape(g.BaseNaziv)} " +
            (g.InvoiceFilter != null ? Regex.Escape(g.InvoiceFilter) : "") +
            @"\d*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToList();

    return desc =>
        exactSet.Contains(desc) ||
        groupRegexes.Any(r => r.IsMatch(desc));
}
```

> **Zašto post-filter ostaje važan:** Alfresco wildcard `ecm:docDesc:"TEK RACUN *"`
> se oslanja na full-text analizer koji tokenizuje vrednost. Ako analizer nije
> phrase-aware, može da vrati "TEK RACUN DUPLIKAT". Regex post-filter osigurava
> da se migriraju SAMO dokumenti sa numeričkim sufiksom — false positive je
> nemoguć, najgore što može da se desi je false negative (Alfresco ne vrati nešto).

---

## 4. Alfresco AFTS — Exact vs. Wildcard

| Tip selekcije | AFTS query | Napomena |
|---|---|---|
| Individualni dok. | `=ecm\:docDesc:"UGOVOR O KREDITU"` | `=` = term equality, brz, bez analize |
| Grupa (sve) | `ecm\:docDesc:"TEK RACUN *"` | Phrase + wildcard, bez `=` |
| Grupa s filterom | `ecm\:docDesc:"TEK RACUN 5*"` | Phrase + prefix wildcard |

**Ključna razlika `=` vs bez `=` u AFTS:**
- `=property:"value"` → term equality (ne prolazi kroz text analyzer, ne podržava wildcard)
- `property:"value *"` → full-text phrase search (podržava `*`, prolazi analyzer)

**Primer mešanog query-ja:**
```
(=ecm\:docDesc:"UGOVOR O KREDITU"
 OR =ecm\:docDesc:"LIČNA KARTA"
 OR ecm\:docDesc:"TEK RACUN *"
 OR ecm\:docDesc:"GOLD PAKET RACUN 5*")
AND ANCESTOR:"workspace://SpacesStore/xxx"
AND TYPE:"cm:content"
```

> **Preporuka:** Testirati wildcard preciznost na realnim podacima pre puštanja.
> Post-filter regex je uvek tu kao zaštita.

---

## 5. UI sloj

### `DocumentSelectionWindow` — novi elementi

**Toolbar** (dodati desno od TipDosijea dropdown-a):

```
[Search: ________] [Tip Dosijea: ▼]    ● Standardni  ○ Grupisani
```

`RadioButton` par kontroliše `_viewMode` enum (`Standard` / `Grouped`).

---

### Grupisani mod — izgled grida (levi panel)

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Tip │ Naziv dokumenta         │ Varijanti │ Br. dok.   │ TipDosijea     │
├─────────────────────────────────────────────────────────────────────────┤
│ [G] │ GOLD PAKET RACUN        │     1.000 │     12.543 │ Gold           │ [Otvori ▶]
│ [G] │ PAKET PLUS RACUN        │         2 │         13 │ Pravna lica    │ [Otvori ▶]
│ [G] │ TEK RACUN               │    70.000 │    456.234 │ Fizicka lica   │ [Otvori ▶]
│  ☐  │ IZJAVA SAGLASNOST 1     │         - │         10 │ Fizicka lica   │
│  ☐  │ LIČNA KARTA             │         - │      1.200 │ Dosije FL      │
│  ☐  │ PONUDA 5                │         - │         15 │ Pravna lica    │
│  ☐  │ UGOVOR O KREDITU        │         - │        245 │ Dosije FL      │
└─────────────────────────────────────────────────────────────────────────┘
```

- `[G]` = ljubičasta badge oznaka, bez checkbox-a, sa `[Otvori ▶]` dugmetom
- `☐` = checkbox za SINGLE redove (kao i sada u standardnom modu)
- Kolona Varijanti = `-` za SINGLE (da ne zbunjuje)
- GROUP redovi imaju blago drugačiju pozadinu (npr. `#F5F0FF`)
- Select All checkbox = skriven u grupisanom modu

---

### Desni panel (Selektovano) — unified prikaz

```
┌──────────────────────────────────────────────────────┐
│ Selektovano (3)                                      │
├──────────────────────────────────────────────────────┤
│ [G] TEK RACUN (sve ~70k var.)                  [X]  │  ← ljubičasto
│ [G] GOLD PAKET RACUN 5* (filter)               [X]  │  ← ljubičasto
│ [D] UGOVOR O KREDITU                           [X]  │  ← plavo
└──────────────────────────────────────────────────────┘
```

- `[G]` tag = ljubičast (#8E44AD), GROUP selekcija
- `[D]` tag = plav (#2980B9), individualni dokument

---

### ViewModeli

**`GroupedRowViewModel`** — za levi panel, objedinjuje GROUP i SINGLE:

```csharp
public class GroupedRowViewModel : INotifyPropertyChanged
{
    public bool IsGroup { get; set; }       // true = GROUP red, false = SINGLE red

    // Zajednička polja
    public string DisplayNaziv { get; set; } = "";
    public long TotalDocuments { get; set; }
    public string? TipDosijea { get; set; }

    // Samo GROUP
    public int VariantCount { get; set; }
    public string? BaseNaziv { get; set; }   // prosljeđuje se u GroupDrilldownWindow

    // Samo SINGLE
    public int? DocumentId { get; set; }
    public string SifraDokumenta { get; set; } = "";
    public bool IsSelected { get; set; }

    // Binding helpers
    public string VariantCountDisplay => IsGroup ? $"{VariantCount:N0}" : "-";
    public string TotalDocumentsDisplay => $"{TotalDocuments:N0}";
    public string RowBackground => IsGroup ? "#F5F0FF" : "White";
    public FontWeight NameWeight => IsGroup ? FontWeights.SemiBold : FontWeights.Normal;
}
```

**`SelectedItemViewModel`** — za desni panel (Selektovano):

```csharp
public class SelectedItemViewModel : INotifyPropertyChanged
{
    public bool IsGroup { get; set; }

    // Za SINGLE
    public int? DocumentId { get; set; }
    public string Naziv { get; set; } = "";
    public string SifraDokumenta { get; set; } = "";
    public string TipDosijea { get; set; } = "";

    // Za GROUP
    public GroupSelection? GroupSelection { get; set; }

    // Binding helpers
    public string TagLabel => IsGroup ? "G" : "D";
    public string TagColor => IsGroup ? "#8E44AD" : "#2980B9";
    public string DisplayName => IsGroup ? GroupSelection!.DisplayName : Naziv;
}
```

---

### `DocumentSelectionWindow` — izmena `btnOk_Click`

Sada gradi `DocumentSelectionResult` umesto `List<string>`:

```csharp
// Novi property na prozoru
public DocumentSelectionResult SelectionResult { get; private set; } = new();

// btnOk_Click:
SelectionResult = new DocumentSelectionResult
{
    ExactDescriptions = _selectedItems
        .Where(i => !i.IsGroup)
        .Select(i => i.Naziv)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct()
        .ToList(),
    GroupSelections = _selectedItems
        .Where(i => i.IsGroup)
        .Select(i => i.GroupSelection!)
        .ToList()
};

// Backwards compat — SelectedDocDescriptions ostaje (koristi ga MigrationPhaseMonitor za text prikaz)
SelectedDocDescriptions = SelectionResult.ExactDescriptions
    .Concat(SelectionResult.GroupSelections.Select(g => g.ToAlfrescoPattern()))
    .ToList();
```

---

### Novi `GroupDrilldownWindow` (popup dijalog)

Otvara se klikom na `[Otvori ▶]` na GROUP redu.
Dimenzije: ~750×520px. Konstruktor prima `DocumentGroupType` i `IDocumentMappingRepository`.

```
┌──────────────────────────────────────────────────────────────────┐
│  Pretraga unutar grupe: "TEK RACUN"                              │
│  70.000 varijanti  |  456.234 dokumenta                          │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ── Opcija A: Uzmi sve varijante ──────────────────────────────  │
│  [Dodaj sve varijante (~70.000)]                                  │
│                                                                  │
│  ── Opcija B: Filter po broju računa ──────────────────────────  │
│  Broj počinje sa: [_________]      [Pretraži]                    │
│  (npr. "5" → matchuje "TEK RACUN 5*")                            │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │ Naziv              │ Br. dok.  │                           │  │
│  ├────────────────────────────────────────────────────────────┤  │
│  │ TEK RACUN 5000     │     3     │                           │  │
│  │ TEK RACUN 5001     │     1     │                           │  │
│  │ ...                                                        │  │
│  └────────────────────────────────────────────────────────────┘  │
│  [◀ Prev]  Str 1 / 14  [Next ▶]        Nađeno: ~700 varijanti   │
│                                                                  │
│  [Dodaj filter "5*" (~700 var.)]                                 │
│                                                          [Zatvori]│
└──────────────────────────────────────────────────────────────────┘
```

**Return vrednost:**
```csharp
public GroupSelection? ChosenSelection { get; private set; }
// null = korisnik zatvorio bez selekcije
```

**"Dodaj sve" dugme:**
```csharp
ChosenSelection = new GroupSelection
{
    BaseNaziv = _group.BaseNaziv,
    InvoiceFilter = null,           // sve varijante
    VariantCount = _group.VariantCount
};
DialogResult = true;
```

**"Dodaj filter" dugme:**
```csharp
ChosenSelection = new GroupSelection
{
    BaseNaziv = _group.BaseNaziv,
    InvoiceFilter = txtFilter.Text.Trim(),  // npr. "5"
    VariantCount = _currentFilteredCount    // iz poslednje pretrage
};
DialogResult = true;
```

---

### `MigrationPhaseMonitor` — izmene

**Novo polje:**
```csharp
private DocumentSelectionResult? _currentSelection;
```

**`btnSelectDocuments_Click`:**
```csharp
if (window.ShowDialog() == true)
{
    _currentSelection = window.SelectionResult;          // novi property
    DocDescriptions = BuildSelectionDisplayString(_currentSelection);
    StatusMessage = $"Selected {_currentSelection.TotalSelectionCount} item(s)";
}
```

**Helper za display string:**
```csharp
private static string BuildSelectionDisplayString(DocumentSelectionResult result)
{
    var parts = new List<string>();
    parts.AddRange(result.ExactDescriptions);
    parts.AddRange(result.GroupSelections.Select(g => g.DisplayName));
    return string.Join(", ", parts);
}
```

**Start dugme — koristi novi path ako postoji, stari inače:**
```csharp
if (_currentSelection != null && _currentSelection.HasAny)
    _documentSearchService.SetDocumentSelection(_currentSelection);
else
{
    // Legacy: manual text unos
    var docDescList = DocDescriptions
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(dt => dt.Trim())
        .Where(dt => !string.IsNullOrWhiteSpace(dt))
        .ToList();
    _documentSearchService.SetDocDescriptions(docDescList);
}
```

> `_currentSelection` se resetuje na `null` svaki put kad se ručno izmeni
> `DocDescriptions` text box (da ne bi ostala stara grupa selekcija).

---

## 6. Šta se NE menja

- Tabela `DocumentMappings` — nema `ALTER TABLE`, nema novih kolona
- Postojeći `SearchWithPagingAsync` — ostaje identičan (standardni mod koristi ga i dalje)
- `SetDocDescriptions(List<string>)` — ostaje za backwards compat (manual text unos)
- `GetDistinctTipDosijeaAsync` — radi u oba moda (Standardni i Grupisani)
- Celokupan migration flow za ne-grupne dokumente — nepromenjeno
- `SelectedDocDescriptions` property na `DocumentSelectionWindow` — ostaje (za text prikaz)

---

## 7. Napomene za implementaciju

### Redosled implementacije (preporučeni)

1. **Contracts** — dodati `DocumentGroupType`, `GroupSelection`, `DocumentSelectionResult`
2. **Repository interface** — dodati 2 nove metode + `GroupedDocumentRow` DTO
3. **Repository impl.** — implementirati SQL (testirati query direktno u SSMS pre integracije)
4. **Service** — dodati `SetDocumentSelection`, izmeniti `BuildDocumentSearchQuery` i post-filter
5. **UI** — `GroupDrilldownWindow` pa `DocumentSelectionWindow`, pa `MigrationPhaseMonitor`

### Edge cases za SQL

- `NAZIV = NULL` → WHERE filtrira (već je `NAZIV IS NOT NULL` u WHERE)
- `NAZIV = "1"` → nema razmaka (`CHARINDEX(' ', NAZIV) = 0`), ide kao SINGLE
- `NAZIV = "RACUN"` → nema razmaka, ide kao SINGLE
- Singleton: `IZJAVA SAGLASNOST 1` (jedini sa base `IZJAVA SAGLASNOST`) → prikazuje se kao SINGLE
- Dva dokumenta iste grupe (npr. samo `TEK RACUN 1` i `TEK RACUN 2`) → GroupCounts.Cnt=2, ide kao GROUP

### Wildcard AFTS — šta testirati pre puštanja

```
ecm:docDesc:"TEK RACUN *"        → treba da matchuje "TEK RACUN 1", "TEK RACUN 70000"
ecm:docDesc:"TEK RACUN *"        → NE sme matchovati "TEK RACUN DUPLIKAT" (post-filter štiti)
ecm:docDesc:"GOLD PAKET RACUN 5*"→ treba matchovati "GOLD PAKET RACUN 5", "GOLD PAKET RACUN 500"
```

Ako Alfresco tokenizuje drugačije (phrase nije poštovana), post-filter regex
u `BuildDocDescVerifier` odbacuje false positives — migracija nikad neće
obraditi pogrešan dokument.
