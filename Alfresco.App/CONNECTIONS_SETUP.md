# Connection Configuration Setup

## Overview
Konekcioni stringovi i API endpointi su izolovani u poseban konfiguracioni fajl `appsettings.Connections.json` radi bezbednosti i lakšeg održavanja.

## Automatsko kreiranje konfiguracije

**Aplikacija automatski kreira konfiguracioni fajl!**

Pri prvom pokretanju, aplikacija će:
1. Proveriti da li postoji `appsettings.Connections.json` u **parent folderu** (folder iznad projekta)
2. Ako ne postoji, **automatski kreirati** fajl iz template-a
3. Prikazati MessageBox sa putem do kreiranog fajla
4. Učitati konfiguraciju iz parent foldera

**Prednosti:**
- Jedan konfiguracioni fajl za sve build verzije (Debug, Release)
- Ne treba da kopiraš fajl svaki put nakon build-a
- Lakše verzionisanje - fajl je izvan projekta

## Manuelno podešavanje (opciono)

Ako želiš manuelno da kreiraš fajl:

1. **Kopiraj template:**
   ```
   Alfresco.App\appsettings.Connections.Example.json → <parent-folder>\appsettings.Connections.json
   ```

2. **Ažuriraj vrednosti u `appsettings.Connections.json`:**

   ### Alfresco Database (PostgreSQL)
   ```json
   "AlfrescoDatabase": {
     "ConnectionString": "Host=localhost;Port=5432;Database=alfresco;Username=alfresco;Password=alfresco"
   }
   ```

   ### Alfresco API
   ```json
   "Alfresco": {
     "BaseUrl": "http://localhost:8080",
     "Username": "admin",
     "Password": "admin"
   }
   ```

   ### Client API
   ```json
   "ClientApi": {
     "BaseUrl": "https://localhost:7102",
     "GetClientDataEndpoint": "/api/Client/GetClientDetailExtended",
     "GetActiveAccountsEndpoint": "/api/Client",
     "ValidateClientEndpoint": "/api/Client/GetClientDetail",
     "TimeoutSeconds": 30,
     "ApiKey": null,
     "RetryCount": 3
   }
   ```

   ### SQL Server
   ```json
   "SqlServer": {
     "ConnectionString": "Data Source=YOUR_SERVER;Initial Catalog=AlfrescoMigration;Integrated Security=True;TrustServerCertificate=True;",
     "CommandTimeoutSeconds": 120,
     "BulkBatchSize": 1000
   }
   ```

## Napomene

- `appsettings.Connections.json` **nije u Git repozitorijumu** (.gitignore)
- Ovaj fajl sadrži osetljive podatke i **ne sme se commit-ovati**
- Svi članovi tima treba da kreiraju svoj lokalni `appsettings.Connections.json` na osnovu Example fajla
- Fajl se automatski kopira u output folder tokom build procesa
- Promene u ovom fajlu se učitavaju u runtime-u (reloadOnChange: true)

## Lokacija fajla

Aplikacija traži `appsettings.Connections.json` na sledećim lokacijama (po prioritetu):

1. **Parent folder** (preporučeno): `<solution-root>\appsettings.Connections.json`
   - Primer: `C:\Users\YourName\source\repos\Alfresco\appsettings.Connections.json`
   - Ovo je **root solution folder**, ne Alfresco.App folder!

2. **Lokalni folder** (fallback): `Alfresco.App\bin\Debug\net8.0-windows\appsettings.Connections.json`

## Redosled prioriteta konfiguracije

Aplikacija učitava konfiguraciju sledećim redom (kasniji override-uje ranije):
1. `appsettings.json` (osnovna konfiguracija)
2. `appsettings.Connections.json` (konekcioni stringovi - override, iz parent ili local foldera)
3. Environment variables (ako postoje)

## Deployment

Za production/staging okruženja, kreirajte odgovarajući `appsettings.Connections.json` fajl na serveru sa production kredencijalima.
