namespace DeveloperOverwatch.API.Tests.Configuration;

using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using DeveloperOverwatch.API.Tests.Controllers;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

/// <summary>
/// In Azure the API signs in to Azure SQL with a managed-identity token that the app's database context attaches to
/// each connection. A health check that opens its own connection from the bare connection string has no token, so
/// Azure SQL refuses it, the probes fail, and the API never starts taking traffic even though its own queries work.
/// The health check must therefore go through the app's database context.
/// </summary>
public class DatabaseHealthCheckTests : IClassFixture<DatabaseHealthCheckTests.RecordingFactory>
{
    private readonly RecordingFactory _factory;

    public DatabaseHealthCheckTests(RecordingFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_QueriesTheDatabaseThroughTheAppDbContext()
    {
        var client = _factory.CreateClient();
        _factory.Recorder.Commands.Clear();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Recorder.Commands.Should().NotBeEmpty(
            "the database health check must run through the app's DbContext, where Azure SQL sign-in is attached");
    }

    public sealed class CommandRecorder : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    public sealed class RecordingFactory : OverwatchWebApplicationFactory
    {
        private readonly SqliteConnection _recordedConnection = new("DataSource=:memory:");

        public CommandRecorder Recorder { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            _recordedConnection.Open();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<OverwatchDbContext>>();
                services.RemoveAll<DbContextOptions<SqliteOverwatchDbContext>>();
                services.RemoveAll<OverwatchDbContext>();
                services.AddDbContext<OverwatchDbContext, SqliteOverwatchDbContext>(options =>
                    options.UseSqlite(_recordedConnection).AddInterceptors(Recorder));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _recordedConnection.Dispose();
        }
    }
}
