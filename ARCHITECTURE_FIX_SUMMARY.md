# Ispravka arhitekture - Circular Dependency Fix

## Problem koji je rešen

`Alfresco.Contracts` je najniži layer (models/contracts) i **ne sme** da ima reference na druge projekte.

Početni plan je bio da `OpisToTipMapperV2` i `DocumentStatusDetectorV2` budu u `Alfresco.Contracts` projektu, ali to bi zahtevalo referencu na `Migration.Abstraction` što bi kreiralo **cirkularnu zavisnost**:

```
❌ LOŠA ARHITEKTURA (circular dependency):
Alfresco.Contracts → Migration.Abstraction → Alfresco.Contracts
```

## Rešenje

Mapperi su premešteni u **`Migration.Infrastructure`** projekat gde i pripadaju.

### Premešteni fajlovi:

1. **OpisToTipMapperV2.cs**
   - ❌ Staro: `Alfresco.Contracts\Mapper\OpisToTipMapperV2.cs`
   - ✅ Novo: `Migration.Infrastructure\Implementation\OpisToTipMapperV2.cs`
   - Namespace: `Migration.Infrastructure.Implementation`

2. **DocumentStatusDetectorV2.cs**
   - ❌ Staro: `Alfresco.Contracts\Mapper\DocumentStatusDetectorV2.cs`
   - ✅ Novo: `Migration.Infrastructure\Implementation\DocumentStatusDetectorV2.cs`
   - Namespace: `Migration.Infrastructure.Implementation`

## Pravilna arhitektura layera

```
┌─────────────────────────────────────────┐
│   Migration.Infrastructure              │  ← OpisToTipMapperV2
│   (Implementation Layer)                │    DocumentStatusDetectorV2
│                                         │    DocumentMappingService
└─────────────────┬───────────────────────┘
                  │ implements
┌─────────────────▼───────────────────────┐
│   Migration.Abstraction                 │  ← IDocumentMappingService
│   (Interface Layer)                     │
└─────────────────┬───────────────────────┘
                  │ references
┌─────────────────▼───────────────────────┐
│   SqlServer.Infrastructure              │  ← DocumentMappingRepository
│   (Data Access Implementation)          │
└─────────────────┬───────────────────────┘
                  │ implements
┌─────────────────▼───────────────────────┐
│   SqlServer.Abstraction                 │  ← IDocumentMappingRepository
│   (Data Access Interface)               │
└─────────────────┬───────────────────────┘
                  │ references
┌─────────────────▼───────────────────────┐
│   Alfresco.Contracts                    │  ← DocumentMapping (Entity Model)
│   (Models/Contracts - Lowest Layer)    │     NO DEPENDENCIES!
└─────────────────────────────────────────┘
```

## Dodatne izmene

### 1. NuGet paketi

Dodato u `SqlServer.Infrastructure.csproj`:
```xml
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.0.0" />
```

### 2. Dokumentacija

Ažurirana dokumentacija u `MIGRATION_FROM_HEIMDALL_MAPPER.md`:
- Ispravljena lokacija fajlova
- Dodat dijagram arhitekture layera
- Dodato upozorenje o circular dependency

## Import statements

Kod koji koristi nove mappere treba da ima:

```csharp
using Migration.Infrastructure.Implementation;

// Dependency Injection
services.AddScoped<OpisToTipMapperV2>();
services.AddScoped<DocumentStatusDetectorV2>();
```

## Prednosti pravilne arhitekture

✅ **Nema circular dependencies**
✅ **Jasna separacija layera**
✅ **Contracts projekat je čist** (samo modeli, bez business logic-a)
✅ **Lakše testiranje** (svaki layer se može testirati nezavisno)
✅ **Bolja maintainability** (izmene u jednom layeru ne utiču na druge)

## Checklist za buduće reference

Kada dodaješ novi kod, proveri:

- [ ] Da li `Alfresco.Contracts` pokušava da referenciše drugi projekat? → **NE!**
- [ ] Da li maperi/servisi sa business logic-om idu u `Infrastructure` layer? → **DA!**
- [ ] Da li interface-i idu u `Abstraction` layer? → **DA!**
- [ ] Da li entity modeli idu u `Contracts` layer? → **DA!**
