# 🔐 Connection Configuration Guide

## Kako radi konfiguracioni sistem

Aplikacija **automatski** kreira i učitava konfiguracioni fajl za konekcije.

### 📍 Lokacija fajla

Pri pokretanju, aplikacija:
1. Traži `appsettings.Connections.json` u **root solution folderu**
   ```
   C:\Users\YourName\source\repos\Alfresco\appsettings.Connections.json
   ```

2. Ako fajl ne postoji, **automatski ga kreira** iz template-a
   - Kopira `appsettings.Connections.Example.json` → `appsettings.Connections.json`
   - Prikazuje MessageBox sa informacijom gde je fajl kreiran

3. Učitava konfiguraciju iz tog fajla

### ✅ Prednosti ovog pristupa

- **Jedan fajl za sve**: Isti konfiguracioni fajl za Debug, Release, i sve verzije
- **Izvan projekta**: Fajl je u root folderu, ne mora da se kopira nakon build-a
- **Git ignore**: Fajl se automatski ignoriše (.gitignore)
- **Runtime reload**: Promene se učitavaju bez restarta (reloadOnChange: true)
- **Bezbednost**: Osetljivi podaci nisu u git repozitorijumu

### 🛠️ Setup (prvi put)

1. **Pokreni aplikaciju** - automatski će kreirati `appsettings.Connections.json`
2. **Otvori fajl** koji je kreiran (path će biti prikazan u MessageBox-u)
3. **Ažuriraj** connection stringove sa svojim vrednostima:

```json
{
  "AlfrescoDatabase": {
    "ConnectionString": "Host=YOUR_HOST;Port=5432;Database=alfresco;Username=USER;Password=PASS"
  },
  "Alfresco": {
    "BaseUrl": "http://YOUR_ALFRESCO:8080",
    "Username": "admin",
    "Password": "admin"
  },
  "ClientApi": {
    "BaseUrl": "https://YOUR_API:7102",
    ...
  },
  "SqlServer": {
    "ConnectionString": "Data Source=YOUR_SERVER;Initial Catalog=AlfrescoMigration;...",
    ...
  }
}
```

4. **Sačuvaj** fajl
5. **Pokreni aplikaciju ponovo** - koristi tvoje vrednosti

### 📂 Struktura foldera

```
C:\Users\YourName\source\repos\Alfresco\
├── appsettings.Connections.json          ← OVDE se kreira/čita
├── Alfresco.App\
│   ├── appsettings.json                   ← Ostale konfiguracije
│   ├── appsettings.Connections.Example.json  ← Template
│   └── bin\Debug\net8.0-windows\
│       └── appsettings.Connections.Example.json  ← Kopirano tokom build-a
```

### 🔄 Fallback logika

Ako fajl u parent folderu ne može da se kreira (npr. permissions issue):
- Prikazuje warning MessageBox
- Koristi lokalni `appsettings.Connections.json` u bin folderu (ako postoji)

### 🚀 Deployment

Za production/staging:
1. Kreiraj `appsettings.Connections.json` na serveru
2. Postavi ga u folder gde se nalazi `.exe` fajl ili 5 nivoa gore
3. Popuni sa production vrednostima

### ❓ FAQ

**Q: Zašto 5 nivoa gore od bin foldera?**
A: Struktura je `bin\Debug\net8.0-windows`, što je 3 nivoa, plus još 2 nivoa (`Alfresco.App` → root) = 5 nivoa ukupno do solution root-a.

**Q: Šta ako slučajno commit-ujem fajl?**
A: Fajl je u `.gitignore`, tako da Git automatski ignoriše promene.

**Q: Mogu li da koristim environment variables?**
A: Da! Environment variables imaju najviši prioritet i override-uju vrednosti iz JSON fajlova.

**Q: Kako da delim konfig sa timom?**
A: Koristi `appsettings.Connections.Example.json` kao template. Svaki developer kreira svoj lokalni `appsettings.Connections.json`.
