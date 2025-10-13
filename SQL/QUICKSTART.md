# 🚀 Quick Start - SQL Migracija

## Najbrži Način - PowerShell

Otvorite PowerShell terminal u ovom folderu (`SQL`) i izvršite:

```powershell
.\Run-Migration.ps1
```

**To je to!** Skripta će automatski izvršiti sve potrebne izmene na bazi.

---

## Alternativa - SQL*Plus

```bash
sqlplus APPUSER/appPass@localhost:1521/FREEPDB1 @001_Extend_Staging_Tables.sql
```

---

## Alternativa - Oracle SQL Developer

1. Konektuj se na bazu (APPUSER@FREEPDB1)
2. Otvori `001_Extend_Staging_Tables.sql`
3. Klikni "Run Script" (F5)

---

## Šta Se Dodaje?

- ✅ **16 novih kolona** u `DOC_STAGING`
- ✅ **20 novih kolona** u `FOLDER_STAGING`
- ✅ **11 novih indeksa** za performanse
- ✅ **Komentari na kolonama** sa dokumentacijom

---

## Verifikacija

Proveri da li je sve ok:

```sql
-- Treba da vrati: 16
SELECT COUNT(*) FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'DOC_STAGING'
  AND COLUMN_NAME LIKE '%TYPE%' OR COLUMN_NAME = 'SOURCE';

-- Treba da vrati: 20
SELECT COUNT(*) FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'FOLDER_STAGING'
  AND COLUMN_NAME = 'CORE_ID' OR COLUMN_NAME = 'UNIQUE_IDENTIFIER';

-- Treba da vrati: 11 indeksa
SELECT COUNT(*) FROM USER_INDEXES
WHERE TABLE_NAME IN ('DOC_STAGING', 'FOLDER_STAGING')
  AND INDEX_NAME LIKE 'IDX_%';
```

---

## Sledeći Koraci

Nakon SQL migracije, pogledajte:
- `INTEGRATION_INSTRUCTIONS.md` - Kako integrisati sa kodom
- `IMPLEMENTATION_SUMMARY.md` - Pregled cele implementacije

---

## Problem?

- **PowerShell execution policy**: `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`
- **Tabela ne postoji**: Kreiraj `DOC_STAGING` i `FOLDER_STAGING` prvo
- **Kolona već postoji**: Ignoriši grešku ili izvrši rollback

Za detaljnije troubleshooting: `RUN_MIGRATION.md`
