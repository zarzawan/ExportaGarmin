using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GarminDataExport.Launcher.Models;
using GarminDataExport.Launcher;
using GarminDataExport.Launcher.Services;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--emit-contract", var output])
        {
            AtomicJsonStore.Write(output, ExampleJournal());
            return 0;
        }
        if (args is ["--validate-catalog", var catalog])
        {
            var entries = RecentActivityReader.ReadValidatedCatalog(catalog, new(2026,1,1), new(2026,1,31));
            Check(entries.Single().ToString().Contains("Rodaje ficticio"), "Catálogo real de Python");
            return 0;
        }
        var temporary = Path.Combine(Path.GetTempPath(), "ExportaGarmin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            CheckProfilePresentation();
            Run(temporary).GetAwaiter().GetResult();
            Console.WriteLine($"{_checks} comprobaciones .NET correctas.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static void CheckProfilePresentation()
    {
        // Modo de demostración: no abre ni carga perfiles personales.
        using var form = new MainForm("principal");
        FieldInfo Field(string name) => typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        T Read<T>(string name) => (T)Field(name).GetValue(form)!;
        Field("_lastOutputFiles").SetValue(form, new List<string> { "synthetic.txt" });
        Field("_selectedActivityId").SetValue(form, "activity_012345abcdef");
        Read<TextBox>("_activityId").Text = "activity_012345abcdef";
        form.ClearProfilePresentation();
        Check(Read<List<string>>("_lastOutputFiles").Count == 0, "Cambio de perfil limpia archivos");
        Check(Field("_selectedActivityId").GetValue(form) is null && Read<TextBox>("_activityId").Text == "", "Cambio de perfil limpia actividad");
        Check(Read<TextBox>("_logBox").Text == "" && !Read<Button>("_openFileButton").Enabled, "Cambio de perfil limpia registro y acceso al archivo");
        form.SetRunningState(true);
        Check(!Read<ComboBox>("_profileCombo").Enabled && !Read<TabControl>("_flowTabs").Enabled
              && !Read<Control>("_contextBar").Enabled && !Read<ComboBox>("_formatCombo").Enabled, "Operación bloquea perfil y contexto");
        form.SetRunningState(false);
        Check(Read<ComboBox>("_profileCombo").Enabled && Read<TabControl>("_flowTabs").Enabled, "Fin de operación desbloquea controles");
    }

    private static JournalDocument ExampleJournal() => new()
    {
        Entries = [new JournalEntry
        {
            Date = new DateTime(2026, 1, 11), ActivityId = "", PrivateComment = "Descanso ficticio",
            IncludeCommentInExport = true, PainScore0To10 = 0, PerceivedEffort1To10 = null,
        }, new JournalEntry
        {
            Date = new DateTime(2026, 1, 10), ActivityId = "activity_012345abcdef",
            ActivityDisplayName = "Sábado 10/01/2026 · Rodaje ficticio", PrivateComment = "Nota antigua ficticia",
            IncludeCommentInExport = false,
        }],
    };

    private static async Task Run(string root)
    {
        var journalPath = Path.Combine(root, "journal.json");
        AtomicJsonStore.Write(journalPath, ExampleJournal());
        var roundtrip = AtomicJsonStore.Read<JournalDocument>(journalPath)!;
        Check(roundtrip.Entries[0].ActivityId == "" && roundtrip.Entries[0].PainScore0To10 == 0, "Contrato diario: vacío y cero");
        Check(!roundtrip.Entries[1].IncludeCommentInExport, "Consentimiento antiguo");
        var entries = Enumerable.Range(0, 205).Select(i => new JournalEntry
            { Date = new DateTime(2026,1,1).AddDays(i), PrivateComment = $"Nota {i}" }).ToList();
        Check(JournalHistory.Query(entries, "", 2).Entries.Count == 5, "Página antigua accesible");
        Check(JournalHistory.Query(entries, "01/01/2026", 0).Entries.Single().PrivateComment == "Nota 0", "Búsqueda por fecha");
        Check(JournalHistory.Query(entries, "Nota 204", 0).Total == 1, "Búsqueda por comentario");
        Check(JournalHistory.Query([], "", 50).Index == 0, "Página vacía");
        Check(BackendEvent.TryParse("EXPORT_EVENT {\"protocol_version\":1,\"event\":\"progress\",\"phase\":\"Activities\",\"completed\":2,\"total\":10}", out var ev) && ev!.Completed == 2, "Protocolo de progreso");
        Check(!BackendEvent.TryParse("EXPORT_EVENT {\"protocol_version\":2,\"event\":\"progress\"}", out _), "Versión de protocolo desconocida");
        Check(!BackendEvent.TryParse("EXPORT_EVENT []", out _), "Evento malformado");

        var catalogPath = Path.Combine(root, "catalog.json");
        AtomicJsonStore.Write(catalogPath, new { status = "completed", start_date = "2026-01-01", end_date = "2026-01-31", activities = Array.Empty<object>() });
        Check(RecentActivityReader.ReadValidatedCatalog(catalogPath, new(2026,1,1), new(2026,1,31)).Count == 0, "Catálogo vacío válido");
        AtomicJsonStore.Write(catalogPath, new { status = "completed", start_date = "2026-01-01", end_date = "2026-01-31", activities = new[] {
            new { activity_ref = "activity_012345abcdef", date = "2026-01-10", name = "Rodaje ficticio", distance_m = 10000, duration_s = 3600 }
        }});
        var catalog = RecentActivityReader.ReadValidatedCatalog(catalogPath, new(2026,1,1), new(2026,1,31));
        Check(catalog.Single().ToString().Contains("Rodaje ficticio") && catalog.Single().ToString().Contains("10/01/2026"), "Título y fecha visibles");
        ExpectInvalid(() => RecentActivityReader.ReadValidatedCatalog(catalogPath, new(2026,2,1), new(2026,2,28)));

        var manifestPath = Path.Combine(root, "manifest.json");
        File.WriteAllText(Path.Combine(root, "report.txt"), "Exportación parcial ficticia");
        AtomicJsonStore.Write(manifestPath, new { run_id = "synthetic-run", report_type = "history",
            output_format = "txt", file_stem = "report", start_date = "2026-01-01", end_date = "2026-01-31",
            files = new[] { "report.txt" }, status = "partial", errors = new[] { "Daily Health" } });
        var partial = MainForm.ReadManifest(manifestPath, root, DateTime.UtcNow, "synthetic-run", "history",
            new(2026,1,1), new(2026,1,31), "report", "txt");
        Check(partial?.IsPartial == true && partial.Files.Count == 1, "Lanzador reconoce manifiesto parcial");
        Check(MainForm.ReadManifest(manifestPath, root, DateTime.UtcNow, "other-run", "history",
            new(2026,1,1), new(2026,1,31), "report", "txt") is null, "Manifiesto de otra ejecución rechazado");

        var handler = new FakeHttpHandler();
        using var client = new HttpClient(handler);
        var statePath = Path.Combine(root, "updates.json");
        var now = new DateTime(2026,9,6,10,0,0,DateTimeKind.Utc);
        Check(!(await UpdateChecker.CheckAsync(false, client, statePath, now)).Success, "Fallo de actualización silencioso");
        await UpdateChecker.CheckAsync(false, client, statePath, now.AddHours(1));
        Check(handler.Calls == 1, "Fallo limitado a una consulta diaria");
        handler.Succeed = true;
        var forced = await UpdateChecker.CheckAsync(true, client, statePath, now.AddHours(2));
        Check(forced.Success && forced.UpdateAvailable && handler.Calls == 2, "Consulta forzada tras fallo");
        await UpdateChecker.CheckAsync(false, client, statePath, now.AddHours(3));
        Check(handler.Calls == 2, "Resultado correcto reutilizado");
        await UpdateChecker.CheckAsync(false, client, statePath, now.AddHours(27));
        Check(handler.Calls == 3, "Caducidad de 24 horas");

        AtomicJsonStore.Write(journalPath, new JournalDocument());
        File.WriteAllText(journalPath, "{invalid");
        Check(AtomicJsonStore.Read<JournalDocument>(journalPath)!.Entries.Count == 2, "Recuperación de copia de seguridad");
    }

    private static void Check(bool success, string name)
    {
        if (!success) throw new InvalidOperationException(name);
        _checks++;
    }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidDataException) { _checks++; return; }
        throw new InvalidOperationException("Se esperaba rechazo de catálogo.");
    }
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        public int Calls;
        public bool Succeed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Succeed ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent("{\"tag_name\":\"v99.0.0\"}", Encoding.UTF8, "application/json") });
        }
    }
}
