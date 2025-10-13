# Post-Migration CLI Example

## Overview

Ovaj dokument pokazuje kako kreirati konzolnu aplikaciju za izvršavanje post-migration taskova.

## Kreiranje CLI Projekta (Opciono)

Ako želite odvojenu CLI aplikaciju za post-migration taskove:

```bash
cd C:\Users\Nikola Preradov\source\repos\Alfresco
dotnet new console -n Alfresco.PostMigration.CLI
dotnet sln add Alfresco.PostMigration.CLI
```

## Dodavanje Dependencies

```bash
cd Alfresco.PostMigration.CLI
dotnet add reference ..\Migration.Abstraction
dotnet add reference ..\Migration.Infrastructure
dotnet add reference ..\Oracle.Abstraction
dotnet add reference ..\Oracle.Infrastructure
dotnet add package Microsoft.Extensions.Hosting
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Microsoft.Extensions.Configuration.Json
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Logging
dotnet add package Microsoft.Extensions.Logging.Console
dotnet add package Oracle.ManagedDataAccess.Core
```

## Program.cs (CLI Application)

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using Migration.Infrastructure.Implementation;
using Migration.Infrastructure.PostMigration;
using Oracle.Abstraction.Interfaces;
using Oracle.Infrastructure.Implementation;
using System;
using System.Threading.Tasks;

namespace Alfresco.PostMigration.CLI
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            var host = CreateHostBuilder(args).Build();

            var command = args[0].ToLower();

            try
            {
                var commands = host.Services.GetRequiredService<PostMigrationCommands>();

                switch (command)
                {
                    case "transform":
                    case "transform-types":
                        Console.WriteLine("Starting document type transformation...");
                        Console.WriteLine();
                        var transformedCount = await commands.TransformDocumentTypesAsync();
                        return transformedCount > 0 ? 0 : 1;

                    case "enrich":
                    case "enrich-accounts":
                        Console.WriteLine("Starting KDP account enrichment...");
                        Console.WriteLine();
                        var enrichedCount = await commands.EnrichKdpAccountNumbersAsync();
                        return enrichedCount > 0 ? 0 : 1;

                    case "validate":
                        Console.WriteLine("Starting migration validation...");
                        Console.WriteLine();
                        var report = await commands.ValidateMigrationAsync();
                        return report.IsValid ? 0 : 1;

                    case "all":
                        Console.WriteLine("Running all post-migration tasks...");
                        Console.WriteLine();

                        // Step 1: Transform document types
                        Console.WriteLine("Step 1/3: Document Type Transformation");
                        await commands.TransformDocumentTypesAsync();
                        Console.WriteLine();

                        // Step 2: Enrich account numbers
                        Console.WriteLine("Step 2/3: KDP Account Enrichment");
                        await commands.EnrichKdpAccountNumbersAsync();
                        Console.WriteLine();

                        // Step 3: Validate
                        Console.WriteLine("Step 3/3: Validation");
                        var finalReport = await commands.ValidateMigrationAsync();
                        Console.WriteLine();

                        if (finalReport.IsValid)
                        {
                            Console.WriteLine("========================================");
                            Console.WriteLine("✓ All post-migration tasks completed successfully!");
                            Console.WriteLine("========================================");
                            return 0;
                        }
                        else
                        {
                            Console.WriteLine("========================================");
                            Console.WriteLine("⚠ Post-migration completed with warnings");
                            Console.WriteLine("========================================");
                            return 1;
                        }

                    default:
                        Console.WriteLine($"Unknown command: {command}");
                        PrintUsage();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("ERROR!");
                Console.WriteLine("========================================");
                Console.WriteLine(ex.Message);
                Console.WriteLine();
                Console.WriteLine("Stack Trace:");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                                       optional: true, reloadOnChange: true)
                          .AddEnvironmentVariables()
                          .AddCommandLine(args);
                })
                .ConfigureServices((context, services) =>
                {
                    // Oracle configuration
                    var connectionString = context.Configuration["Oracle:ConnectionString"];
                    services.AddScoped<IUnitOfWork>(sp => new OracleUnitOfWork(connectionString));

                    // Repositories
                    services.AddTransient<IDocStagingRepository, DocStagingRepository>();
                    services.AddTransient<IFolderStagingRepository, FolderStagingRepository>();

                    // External API configuration (when available)
                    /*
                    services.Configure<ClientApiOptions>(
                        context.Configuration.GetSection(ClientApiOptions.SectionName));
                    services.Configure<DutApiOptions>(
                        context.Configuration.GetSection(DutApiOptions.SectionName));

                    services.AddHttpClient<IClientApi, ClientApi>();
                    services.AddHttpClient<IDutApi, DutApi>();
                    */

                    // Migration services
                    services.AddScoped<IClientEnrichmentService, ClientEnrichmentService>();
                    services.AddScoped<IDocumentTypeTransformationService, DocumentTypeTransformationService>();
                    services.AddScoped<IUniqueFolderIdentifierService, UniqueFolderIdentifierService>();

                    // Post-migration commands
                    services.AddScoped<PostMigrationCommands>();
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                });

        static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("Alfresco Post-Migration CLI");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Usage: Alfresco.PostMigration.CLI <command>");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  transform         Transform document types (00824-migracija -> 00099)");
            Console.WriteLine("  enrich            Enrich KDP documents with account numbers");
            Console.WriteLine("  validate          Validate migration results");
            Console.WriteLine("  all               Run all post-migration tasks");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Alfresco.PostMigration.CLI transform");
            Console.WriteLine("  Alfresco.PostMigration.CLI enrich");
            Console.WriteLine("  Alfresco.PostMigration.CLI validate");
            Console.WriteLine("  Alfresco.PostMigration.CLI all");
            Console.WriteLine();
        }
    }
}
```

## Alternativa: Dodavanje u Postojeću Aplikaciju

Ako ne želite odvojenu CLI aplikaciju, možete dodati komande direktno u postojeću WPF aplikaciju:

### 1. Dodaj Command Handlers u MainWindow.xaml

```xml
<Window x:Class="Alfresco.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Alfresco Migration" Height="600" Width="800">
    <Grid>
        <TabControl>
            <!-- Existing tabs... -->

            <!-- New Post-Migration Tab -->
            <TabItem Header="Post-Migration">
                <StackPanel Margin="20">
                    <TextBlock FontSize="16" FontWeight="Bold" Margin="0,0,0,20">
                        Post-Migration Tasks
                    </TextBlock>

                    <Button Name="BtnTransformTypes"
                            Content="Transform Document Types"
                            Click="BtnTransformTypes_Click"
                            Margin="0,10,0,0"
                            Padding="10,5"/>

                    <Button Name="BtnEnrichAccounts"
                            Content="Enrich KDP Account Numbers"
                            Click="BtnEnrichAccounts_Click"
                            Margin="0,10,0,0"
                            Padding="10,5"/>

                    <Button Name="BtnValidate"
                            Content="Validate Migration"
                            Click="BtnValidate_Click"
                            Margin="0,10,0,0"
                            Padding="10,5"/>

                    <Button Name="BtnRunAll"
                            Content="Run All Post-Migration Tasks"
                            Click="BtnRunAll_Click"
                            Margin="0,20,0,0"
                            Padding="10,5"
                            Background="LightGreen"/>

                    <TextBox Name="TxtPostMigrationOutput"
                             Margin="0,20,0,0"
                             Height="300"
                             IsReadOnly="True"
                             VerticalScrollBarVisibility="Auto"
                             TextWrapping="Wrap"/>
                </StackPanel>
            </TabItem>
        </TabControl>
    </Grid>
</Window>
```

### 2. Dodaj Event Handlers u MainWindow.xaml.cs

```csharp
using Migration.Infrastructure.PostMigration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace Alfresco.App
{
    public partial class MainWindow : Window
    {
        private readonly PostMigrationCommands _postMigrationCommands;

        public MainWindow()
        {
            InitializeComponent();

            // Inject PostMigrationCommands from DI
            _postMigrationCommands = App.AppHost.Services.GetRequiredService<PostMigrationCommands>();
        }

        private async void BtnTransformTypes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtPostMigrationOutput.Text = "Starting document type transformation...\n\n";
                BtnTransformTypes.IsEnabled = false;

                var count = await _postMigrationCommands.TransformDocumentTypesAsync();

                TxtPostMigrationOutput.Text += $"\n✓ Transformation complete! {count} documents transformed.";
            }
            catch (Exception ex)
            {
                TxtPostMigrationOutput.Text += $"\n✗ Error: {ex.Message}";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnTransformTypes.IsEnabled = true;
            }
        }

        private async void BtnEnrichAccounts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtPostMigrationOutput.Text = "Starting KDP account enrichment...\n\n";
                BtnEnrichAccounts.IsEnabled = false;

                var count = await _postMigrationCommands.EnrichKdpAccountNumbersAsync();

                TxtPostMigrationOutput.Text += $"\n✓ Enrichment complete! {count} documents enriched.";
            }
            catch (Exception ex)
            {
                TxtPostMigrationOutput.Text += $"\n✗ Error: {ex.Message}";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnEnrichAccounts.IsEnabled = true;
            }
        }

        private async void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtPostMigrationOutput.Text = "Running validation...\n\n";
                BtnValidate.IsEnabled = false;

                var report = await _postMigrationCommands.ValidateMigrationAsync();

                TxtPostMigrationOutput.Text += $"\n========================================\n";
                TxtPostMigrationOutput.Text += $"Validation Report:\n";
                TxtPostMigrationOutput.Text += $"========================================\n";
                TxtPostMigrationOutput.Text += $"Documents without CoreId: {report.DocumentsWithoutCoreId}\n";
                TxtPostMigrationOutput.Text += $"Untransformed documents: {report.UntransformedDocuments}\n";
                TxtPostMigrationOutput.Text += $"KDP docs without accounts: {report.KdpDocumentsWithoutAccounts}\n";
                TxtPostMigrationOutput.Text += $"Folders without enrichment: {report.FoldersWithoutEnrichment}\n";
                TxtPostMigrationOutput.Text += $"Documents with errors: {report.DocumentsWithErrors}\n";
                TxtPostMigrationOutput.Text += $"\n{(report.IsValid ? "✓ PASSED" : "⚠ ISSUES FOUND")}\n";
            }
            catch (Exception ex)
            {
                TxtPostMigrationOutput.Text += $"\n✗ Error: {ex.Message}";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnValidate.IsEnabled = true;
            }
        }

        private async void BtnRunAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtPostMigrationOutput.Text = "Running all post-migration tasks...\n\n";
                BtnRunAll.IsEnabled = false;

                // Step 1: Transform
                TxtPostMigrationOutput.Text += "Step 1/3: Document Type Transformation\n";
                var transformCount = await _postMigrationCommands.TransformDocumentTypesAsync();
                TxtPostMigrationOutput.Text += $"✓ {transformCount} documents transformed\n\n";

                // Step 2: Enrich
                TxtPostMigrationOutput.Text += "Step 2/3: KDP Account Enrichment\n";
                var enrichCount = await _postMigrationCommands.EnrichKdpAccountNumbersAsync();
                TxtPostMigrationOutput.Text += $"✓ {enrichCount} documents enriched\n\n";

                // Step 3: Validate
                TxtPostMigrationOutput.Text += "Step 3/3: Validation\n";
                var report = await _postMigrationCommands.ValidateMigrationAsync();
                TxtPostMigrationOutput.Text += $"{(report.IsValid ? "✓ PASSED" : "⚠ ISSUES FOUND")}\n\n";

                TxtPostMigrationOutput.Text += "========================================\n";
                TxtPostMigrationOutput.Text += "All post-migration tasks completed!\n";
                TxtPostMigrationOutput.Text += "========================================\n";

                MessageBox.Show("Post-migration tasks completed!", "Success",
                               MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                TxtPostMigrationOutput.Text += $"\n✗ Error: {ex.Message}";
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRunAll.IsEnabled = true;
            }
        }
    }
}
```

### 3. Registruj PostMigrationCommands u App.xaml.cs

```csharp
// Add to ConfigureServices in App.xaml.cs (around line 230)
services.AddScoped<PostMigrationCommands>();
```

## Korišćenje

### CLI Verzija:
```bash
# Transform document types
dotnet run --project Alfresco.PostMigration.CLI -- transform

# Enrich account numbers
dotnet run --project Alfresco.PostMigration.CLI -- enrich

# Validate migration
dotnet run --project Alfresco.PostMigration.CLI -- validate

# Run all tasks
dotnet run --project Alfresco.PostMigration.CLI -- all
```

### WPF Verzija:
1. Pokreni aplikaciju
2. Idi na "Post-Migration" tab
3. Klikni na dugme za željenu operaciju

## Redosled Izvršavanja

**VAŽNO**: Post-migration taskove izvršavati samo NAKON što se završi migracija iz svih izvora!

1. **Transform Document Types** - Transformiše dokumente sa "-migracija" sufiksom
2. **Enrich KDP Accounts** - Popunjava račune za KDP dokumente
3. **Validate** - Proverava da li je migracija uspešna

Ili jednostavno klikni "Run All" da se automatski izvršavaju svi taskovi redom.

## Troubleshooting

### Problem: "Repository method not implemented"
**Rešenje**: Dodaj repository metode koje su navedene u TODO komentarima:
- `GetKdpDocumentsWithoutAccountsAsync()`
- `GetDocumentsRequiringTransformationAsync()`
- `UpdateAsync()`

### Problem: "ClientAPI not available"
**Rešenje**: Odkomentariši ClientAPI registraciju u App.xaml.cs i konfiguriši endpoint u appsettings.json

Za više informacija pogledaj `INTEGRATION_INSTRUCTIONS.md`.
