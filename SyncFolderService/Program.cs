using Microsoft.Extensions.Options;
using SyncFolderService;
using SyncFolderService.SyncLogic;
using Serilog;

const string CONST_EventLogSourceName = "SyncFolderService";
const string CONST_EventLogName = "Application";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// configure services

//var configuration = new ConfigurationBuilder()
//    .SetBasePath(Directory.GetCurrentDirectory())    
//    .AddJsonFile(builder.Environment.IsDevelopment()? "appsettings.Development.json": "appsettings.json", optional: false, reloadOnChange: true)
//    .Build();

builder.Services.AddWindowsService();

builder.Services.Configure<SyncFolderServiceOptions>(
    builder.Configuration.GetSection(SyncFolderServiceOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<CReplicator>();

builder.Services.AddHostedService<Worker>();

// Configure logging
builder.Logging.ClearProviders();


if (OperatingSystem.IsWindows() && !builder.Environment.IsDevelopment())
{
    var logDirectory = Path.Combine(
    AppContext.BaseDirectory,
    "logs");

    var logFilePath = Path.Combine(
        logDirectory,
        builder.Configuration["Serilog:FileName"] ?? throw new InvalidOperationException("Serilog:FileName was not found in appsettings.json"));

    builder.Logging.AddEventLog(logging =>
    {
        logging.SourceName = CONST_EventLogSourceName;
        logging.LogName = CONST_EventLogName;
    });

    Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.File(
        path: logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 10,
        shared: true
    )
    .CreateLogger();

    builder.Services.AddSerilog();
}
else
{
    builder.Logging.AddConsole();
}

// The Event Log source has to exist before the first entry is written, creating it requires administrative rights.
string? v_EventLogWarning = EventLogSourceInitializer.TryCreateSource(CONST_EventLogSourceName, CONST_EventLogName);

IHost host = builder.Build();

if (v_EventLogWarning is not null)
{
    // The entry itself is lost when the source does not exist, so the problem is reported on the standard error output as well.
    Console.Error.WriteLine(v_EventLogWarning);
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(CONST_EventLogSourceName).LogWarning("{Warning}", v_EventLogWarning);
}

await host.RunAsync();
