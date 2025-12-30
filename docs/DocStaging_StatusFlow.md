# DocStaging Status Flow - MigrationByDocument

## 🔄 Status Lifecycle

```
READY
  ↓
  └─→ (FolderPreparation uzme dokument)
       └─→ PREPARATION
            ↓
            └─→ (Folder kreiran, DestinationFolderId popunjen)
                 └─→ PREPARED
                      ↓
                      └─→ (Move uzme dokument)
                           └─→ IN_PROGRESS
                                ↓
                                └─→ (Move završen)
                                     └─→ DONE

                                     ili

                                     └─→ ERROR (na bilo kojoj fazi)
```

---

## 📊 Status Definicije

| Status | Faza | Opis | Kada se postavlja |
|--------|------|------|-------------------|
| **READY** | DocumentSearch | Dokument je pronađen u starom Alfresco-u i upisan u DocStaging. Spreman za folder preparation. | DocumentSearchService nakon uspešnog API poziva |
| **PREPARATION** | FolderPreparation | FolderPreparation service je uzeo dokument i kreira destination folder. | `TakeReadyForProcessingAsync()` atomski update |
| **PREPARED** | FolderPreparation | Destination folder je kreiran, DestinationFolderId je popunjen. Dokument je spreman za Move fazu. | `UpdateDestinationFolderIdAsync()` nakon uspešne kreacije foldera |
| **IN_PROGRESS** | Move | Move service je uzeo dokument i vrši move operaciju. | `TakeReadyForMoveAsync()` atomski update |
| **DONE** | Move | Move je uspešno završen. Dokument je sada u novom Alfresco-u. | `SetStatusAsync('DONE')` nakon uspešnog move-a |
| **ERROR** | Bilo koja | Greška se desila tokom procesiranja. | `FailAsync()` ili `SetStatusAsync('ERROR')` |

---

## 🔍 Metode po Statusima

### 1. DocumentSearchService
**Šta radi:**
- Pretraživanje dokumenata iz starog Alfresco-a
- Insertovanje u DocStaging

**Status flow:**
```
(Prazan) → INSERT → READY
```

**Metode:**
- `InsertManyAsync()` - insertuje sa Status='READY'

---

### 2. FolderPreparationService

**Šta radi:**
- Uzima READY dokumente
- Kreira destination foldere
- Popunjava DestinationFolderId

**Status flow:**
```
READY → PREPARATION → PREPARED
```

**Metode:**
- `GetUniqueDestinationFoldersAsync()` - uzima sve DISTINCT foldere iz READY dokumenata (implicitno)
- `TakeReadyForProcessingAsync()` - **atomski** uzima READY dokumente i postavlja ih na PREPARATION
  ```sql
  WHERE Status = 'READY'
  UPDATE SET Status = 'PREPARATION'
  ```

- `UpdateDestinationFolderIdAsync()` - update-uje DestinationFolderId i postavlja Status='PREPARED'
  ```sql
  WHERE Status = 'PREPARATION'
  UPDATE SET DestinationFolderId = ..., Status = 'PREPARED'
  ```

---

### 3. MoveService

**Šta radi:**
- Uzima PREPARED dokumente
- Move-uje ih u destination folder
- Markira kao DONE

**Status flow:**
```
PREPARED → IN_PROGRESS → DONE (ili ERROR)
```

**Metode:**
- `TakeReadyForMoveAsync()` - **atomski** uzima PREPARED dokumente i postavlja ih na IN_PROGRESS
  ```sql
  WHERE Status = 'PREPARED'
  UPDATE SET Status = 'IN_PROGRESS'
  ```

- `SetStatusAsync(id, 'DONE', null)` - markira kao završeno
- `FailAsync(id, error)` - markira kao ERROR

---

## ✅ Prednosti Novog Status Flow-a

### 1. **Jasna separacija faza**
- READY = čeka folder preparation
- PREPARED = čeka move
- IN_PROGRESS = move u toku
- DONE = završeno

### 2. **Eliminacija konfuzije**
- Stari problem: TakeReadyForProcessingAsync i TakeReadyForMoveAsync oba uzimaju READY i postavljaju IN_PROGRESS
- Novo rešenje: Različiti statusi za različite faze

### 3. **Atomske operacije**
- `TakeReadyForProcessingAsync`: READY → PREPARATION (atomski)
- `UpdateDestinationFolderIdAsync`: PREPARATION → PREPARED (atomski)
- `TakeReadyForMoveAsync`: PREPARED → IN_PROGRESS (atomski)

### 4. **Lakše praćenje progresa**
```sql
-- Koliko je u svakoj fazi?
SELECT Status, COUNT(*)
FROM DocStaging
GROUP BY Status

-- Očekivani rezultat:
-- READY        : 10,000  (čeka folder preparation)
-- PREPARATION  : 50      (folder preparation u toku)
-- PREPARED     : 5,000   (čeka move)
-- IN_PROGRESS  : 100     (move u toku)
-- DONE         : 35,000  (završeno)
-- ERROR        : 20      (failovano)
```

---

## 🛡️ Error Handling

### Šta ako greška se desi?

**Tokom FolderPreparation:**
```
READY → PREPARATION → ERROR
```
- `FailAsync(id, error)` postavlja Status='ERROR'

**Tokom Move:**
```
PREPARED → IN_PROGRESS → ERROR
```
- `FailAsync(id, error)` postavlja Status='ERROR'

**ERROR dokumenti:**
- Ostaju u ERROR statusu
- Mogu se manually reset-ovati nazad na READY (UPDATE Status='READY' WHERE Id=...)
- Ili se brišu sa `PrepareForMigrationAsync()` pre ponovnog pokretanja

---

## 🔄 PrepareForMigration - Clean Start

**Šta briše:**
```sql
DELETE FROM DocStaging
WHERE Status != 'DONE'
   OR Status IS NULL
```

**Briše:**
- READY (nisu započeti)
- PREPARATION (stuck tokom folder preparation)
- PREPARED (nisu move-ovani)
- IN_PROGRESS (stuck tokom move-a)
- ERROR (failovani)
- NULL (nevalidni)

**NE briše:**
- DONE ✅ (uspešno završeni)

---

## 📝 Primeri Query-ja

### Koliko dokumenata čeka folder preparation?
```sql
SELECT COUNT(*) FROM DocStaging WHERE Status = 'READY'
```

### Koliko dokumenata čeka move?
```sql
SELECT COUNT(*) FROM DocStaging WHERE Status = 'PREPARED'
```

### Koliko je ukupno završeno?
```sql
SELECT COUNT(*) FROM DocStaging WHERE Status = 'DONE'
```

### Statistika po statusima
```sql
SELECT
    Status,
    COUNT(*) AS Count,
    CAST(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER() AS DECIMAL(5,2)) AS Percentage
FROM DocStaging
GROUP BY Status
ORDER BY Count DESC
```

### Resetuj ERROR dokumente nazad u READY
```sql
UPDATE DocStaging
SET Status = 'READY',
    ErrorMsg = NULL,
    UpdatedAt = GETUTCDATE()
WHERE Status = 'ERROR'
```

---

## 🎯 Best Practices

1. **Uvek pozovi PrepareForMigrationAsync PRE pokretanja migracije**
   - Briše stuck items
   - Omogućava clean start

2. **Monitor progress sa GROUP BY Status**
   - Vidi gde je bottleneck
   - Detektuj stuck items

3. **Ne menjaj statusе manually sem za debugging**
   - Servisи automatski upravljaju statusima
   - Manual izmene mogu da dovedu do data inconsistency

4. **ERROR dokumenti treba investigirati**
   - Log ErrorMsg
   - Fix problem
   - Resetuj na READY ili obriši

---

## 🔧 Troubleshooting

### Problem: Dokumenti zaglavljeni u PREPARATION statusu

**Uzrok:** FolderPreparation service crashovao pre nego što je završio folder kreaciju

**Rešenje:**
```sql
-- Proveri koliko ima
SELECT COUNT(*) FROM DocStaging WHERE Status = 'PREPARATION'

-- Opcija A: Resetuj na READY (će se ponovo procesirati)
UPDATE DocStaging SET Status = 'READY', UpdatedAt = GETUTCDATE() WHERE Status = 'PREPARATION'

-- Opcija B: Pozovi PrepareForMigrationAsync (briše SVE osim DONE)
```

---

### Problem: Dokumenti zaglavljeni u PREPARED statusu

**Uzrok:** Move service nije pokrenut ili je stao

**Rešenje:**
```sql
-- Proveri koliko ima
SELECT COUNT(*) FROM DocStaging WHERE Status = 'PREPARED'

-- Pokreni Move service - preuzećе ih automatski
```

---

### Problem: Puno dokumenata u ERROR statusu

**Uzrok:** Greška tokom folder preparation ili move-a

**Rešenje:**
```sql
-- Vidi koji errors se dešavaju
SELECT ErrorMsg, COUNT(*) AS Count
FROM DocStaging
WHERE Status = 'ERROR'
GROUP BY ErrorMsg
ORDER BY Count DESC

-- Posle fix-a problema, resetuj na READY
UPDATE DocStaging SET Status = 'READY', ErrorMsg = NULL WHERE Status = 'ERROR'
```

---

## 📚 Zaključak

Novi status flow sa **READY → PREPARATION → PREPARED → IN_PROGRESS → DONE** omogućava:
- ✅ Jasnu separaciju faza migracije
- ✅ Atomske operacije bez race conditions
- ✅ Lako praćenje progresa
- ✅ Efikasniji error handling

Svaka faza ima svoj status, što eliminiše konfuziju i omogućava robustan migration pipeline.
