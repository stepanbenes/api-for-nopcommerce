using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using TestAssert = NUnit.Framework.Assert;

namespace Nop.Plugin.Api.Tests.IntegrationTests;

internal sealed class PostgresProbe(string container)
{
    public static PostgresProbe ForConfiguredContainer(Uri apiBaseAddress)
    {
        var container = Environment.GetEnvironmentVariable("NOP_API_TEST_POSTGRES_CONTAINER");
        if (string.IsNullOrWhiteSpace(container))
            TestAssert.Ignore("Set NOP_API_TEST_POSTGRES_CONTAINER to the PostgreSQL container for the isolated test store.");

        TestAssert.That(apiBaseAddress.IsLoopback, Is.True, "Database integration tests require a loopback API URL.");
        return new PostgresProbe(container);
    }

    public async Task<JsonElement> ReadAsync(string select, int id)
    {
        var result = await RunAsync($"SELECT COALESCE(({select}), 'null'::jsonb);", id, readOnly: true);
        using var document = JsonDocument.Parse(result);
        return document.RootElement.Clone();
    }

    public static void RequireDisposableStoreForFaultInjection()
    {
        if (Environment.GetEnvironmentVariable("NOP_API_TEST_POSTGRES_ALLOW_FAULT_INJECTION") != "1")
            TestAssert.Ignore("Set NOP_API_TEST_POSTGRES_ALLOW_FAULT_INJECTION=1 only for a disposable local test store.");
    }

    public async Task ExecuteInDisposableStoreAsync(string sql)
    {
        RequireDisposableStoreForFaultInjection();
        await RunAsync(sql, 0, readOnly: false);
    }

    private async Task<string> RunAsync(string sql, int id, bool readOnly)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "exec", "-i", "--env", readOnly
                ? "PGOPTIONS=-c default_transaction_read_only=on"
                : "PGOPTIONS=-c lock_timeout=5s -c statement_timeout=10s",
            container, "psql", "-XqAtw", "--username=nopcommerce", "--dbname=nopcommerce",
            "--set=ON_ERROR_STOP=1", $"--set=id={id.ToString(CultureInfo.InvariantCulture)}"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start docker exec for PostgreSQL.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.StandardInput.WriteLineAsync(sql.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill();
            throw new TimeoutException("The local PostgreSQL command did not finish in 15 seconds.");
        }

        TestAssert.That(process.ExitCode, Is.Zero, await errors);
        return await output;
    }
}
